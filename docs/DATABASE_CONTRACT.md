# KnownFirst database contract

## Status and scope

This document is the binding contract for KnownFirst persisted application
data, schema compatibility, migrations, and database-test safety.

It describes the current SQLite model at schema version 8. It does not define a
backup file format or claim that backup and restore exist.

## Storage boundary

- Personal documents, vocabulary state, prepared content, schedules, and
  settings-related workflow state remain local to the device.
- The application database is named `knownfirst.db3` and lives in the
  platform application-data directory.
- Automated tests use isolated temporary databases only.
- A real user database must never be opened, copied, migrated, reset, or
  deleted by an automated test or routine development check.

## Current schema

`DatabaseSchema.CurrentVersion` and `PRAGMA user_version` are both **8**.

| Table | Responsibility |
| --- | --- |
| `Documents` | Accepted original text, lexical-language settings, fingerprint, and import metadata |
| `Words` | Language-scoped vocabulary identity, knowledge state, preparation state, and aggregate counts |
| `WordForms` | Encountered surface forms and occurrence counts |
| `SentenceSpans` | Exact UTF-16 coordinate ranges into original documents |
| `WordOccurrences` | Exact token coordinates, forms, order, and supported technical-family metadata |
| `Meanings` | User-confirmed prepared learning content and source attribution |
| `ReviewStates` | Retained aggregate review counters from the original model |
| `ReviewSessions` | Resumable document-review workflow |
| `ReviewCandidates` | Ordered review decisions and Undo state |
| `LexicalCache` | Versioned provider reference data and attribution |
| `PreparationSessions` | Resumable preparation batches |
| `PreparationCandidates` | Ordered lookup results and preparation outcomes |
| `ContextSnapshots` | Deduplicated learning contexts with exact target coordinates |
| `LearningCards` | Independent Sense-addressed card-direction scheduling state |
| `LearningReviews` | Persisted rating history including target and matched answer variant |
| `LearningSessions` | Resumable learning-session summary state |
| `LearningSessionCards` | Ordered persisted session queue, frozen answer target, and reveal/check state |
| `Senses` | Meaning-centric Sense identity, provenance, and status per vocabulary identity |
| `AnswerVariants` | Distinct accepted answer expressions per Sense and answer language |
| `SenseAnswerVariantAssignments` | Direction-specific Required/AcceptedOnly assignment, preferred flag, and Required-epoch boundary |
| `AnswerVariantProgress` | Replayable per `(CardId, AnswerVariantId)` mastery progress |

At schema 8 `LearningCards.MeaningId` no longer exists; the card's own preferred
meaning is `LearningCards.PreferredMeaningId`, and the card is addressed by
`SenseId`.

Relationships are represented by entity IDs and enforced by transactional
service operations and tests. Do not introduce a competing representation of
the same document, vocabulary, meaning, sense, context, card, or session.

## Required invariants

1. Original accepted document content is unchanged.
2. Sentence and occurrence ranges resolve to the exact original substrings.
3. One vocabulary identity may have many occurrences and surface forms.
4. Frequency equals accepted occurrences; context deduplication does not lower
   it.
5. Permanently known vocabulary retains only the minimum marker needed to skip
   future review.
6. Prepared meanings and lexical-cache reference data remain distinct from
   personal knowledge and scheduling state.
7. Each enabled card direction has independent scheduling state.
8. Completed-document cleanup removes content only when no unresolved workflow
   or active learning dependency remains.
9. Retry and resume operations do not duplicate documents, occurrences,
   meanings, contexts, cards, cache rows, or ratings.

## Transactions

Transactions are required for accepted import persistence, review creation and
decisions, Undo, discard, preparation acceptance and dispositions, learning
session creation, every rating, permanent-known cleanup, and completed-document
cleanup.

A failed transaction must preserve the last committed user-visible state. Do
not report success before the transaction commits.

## Migration policy

- Migrations are forward-only.
- Never delete or recreate a user database merely because the schema changed.
- Every schema change increments the schema version when appropriate and
  documents old-to-new behavior here.
- New columns need deterministic defaults for existing rows.
- Destructive transformation requires explicit rationale, rollback/recovery
  behavior, and compatibility tests.
- Migrations must be transactional where the SQLite operation permits it.
- Tests must cover at least the oldest explicitly supported source shape and
  the immediately preceding production schema.

### Schema-8 activation behavior

Initialization reads `PRAGMA user_version` before touching any table and then
follows exactly one path:

| Source version | Behavior |
| --- | --- |
| Fresh / empty database | Initializes directly to a validated schema 8. |
| 0–6 | Creates or updates the registered tables to reach the schema-7 baseline boundary, applies the legacy enum backfills, and then migrates to schema 8 in the same initialization. |
| 7 | Migrates to schema 8. |
| 8 (valid) | Validation only. The database is inspected and never mutated. |
| 8 (malformed) | Fails closed. Nothing is repaired and nothing is written. |
| Greater than 8 | Rejected with `DatabaseSchemaCompatibilityException` before any table or cache change. |

The legacy enum backfills assign deterministic supported values for
`Words.TokenKind`, `Words.PreparationState`, `Words.AutomaticInteractionMode`,
`Meanings.TokenKind`, `WordOccurrences.TechnicalFamily`, and the
`Documents`/`LexicalCache` lookup mode before activation.

The 7 → 8 migration runs inside one real SQLite transaction. It is rollback-safe,
cancellation-safe, and retryable: a failed or cancelled attempt leaves a
byte-for-byte unchanged schema-7 database that can be retried on the next start.
Row IDs are preserved; the single `LearningCards.MeaningId → PreferredMeaningId`
rename is the only non-additive step, and it uses a column-rebuild fallback when
the runtime SQLite lacks `ALTER TABLE ... RENAME COLUMN`.

### Structural validation

Before a schema-8 database is exposed to services it must pass structural and
logical validation covering:

- required tables and required columns;
- declared column nullability and primary-key semantics as the real DDL declares
  them (an `INTEGER PRIMARY KEY` rowid alias is validated by primary-key position
  and affinity, not by the reported `notnull` flag);
- absence of legacy artifacts such as `LearningCards.MeaningId` and the retired
  `IX_LearningCards_Word_Direction` index;
- index definitions including ordered columns, collation, uniqueness, and partial
  predicates;
- enum domains for every persisted enum column;
- ownership relationships (Meaning → Sense → Word, card → Sense, snapshot →
  Document/Meaning, assignment → Sense/AnswerVariant, progress → card/assignment);
- queue and review answer-variant targets; and
- persisted relationship integrity for every foreign entity ID.

Validation never repairs a malformed database and never writes to a valid one.

The checked-in migration regressions construct older table shapes in temporary
databases, preserve their existing rows, and verify both the legacy defaults and
the resulting schema-8 shape. That fixture set is not a complete historical
migration audit; expanding the migration fixture matrix remains separately
planned work.

## Lexical-cache compatibility

Lexical cache rows are reference data, not irreplaceable personal learning
state. The current initialization removes keys outside the `v2|` format so
older keys cannot cross lookup-mode or target-language boundaries.

JSON for lexical results and aliases uses
`LexicalJsonSerializerContext` source-generated metadata. Android
Release/AOT paths must not fall back to reflection-dependent serialization.

## Data deletion

- Reset is an explicit user-confirmed product operation, not a migration.
- Permanent-known and completed-document cleanup follow the binding lifecycle
  rules in [KNOWNFIRST_ARCHITECTURE.md](KNOWNFIRST_ARCHITECTURE.md).
- Cache invalidation must not delete personal meanings, review decisions,
  schedules, or history.
- Maintenance must be idempotent and must not block initial UI rendering.

## Backup and restore boundary

The supported portable format is the `.kfarchive` archive. A schema-8 database
exports archive format **v2**, and merge safety copies are captured as v2.
Archive format **v1** remains readable and can still be restored into a schema-8
target (upgraded in memory for Schema-8 targets).

Import into an **empty** target uses restore-into-empty. Import into a **populated**
Schema-8 target is merged transactionally: validation → preflight planning →
validated safety copy → transactional merge writer → deterministic card-schedule
replay → atomic commit or rollback. Stale or non-executable plans are rejected.
Multiple imports converge without duplicates. The merge writer reuses existing
entities, inserts missing entities and preserved variants, and applies enrichment
policies. Failure and cancellation roll back completely. See `MergePreflightPlannerV2`,
`PortableMergeWriter`, and `MergeWriterExecutor`. Archive-v1 upgrades in memory
for Schema-8 targets; archive-v2 into Schema 7 is rejected.

Synchronization and cloud formats do not exist.

Any accepted decision that changes these rules requires an ADR and an update to
this contract, `PROJECT_STATE.md`, tests, and user-facing documentation.
