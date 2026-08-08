# KnownFirst database contract

## Status and scope

This document is the binding contract for KnownFirst persisted application
data, schema compatibility, migrations, and database-test safety.

It describes the current SQLite model at schema version 9.

## Storage boundary

- Personal documents, vocabulary state, prepared content, schedules, and
  settings-related workflow state remain local to the device.
- The application database is named `knownfirst.db3` and lives in the
  platform application-data directory.
- Automated tests use isolated temporary databases only.
- A real user database must never be opened, copied, migrated, reset, or
  deleted by an automated test or routine development check.

## Current schema

`DatabaseSchema.CurrentVersion` and `PRAGMA user_version` are both **9**.
A healthy initialized current database reports `PRAGMA user_version = 9`.

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

At schema 9, `ReviewSessions` index constraints change to support multiple Completed sessions per `DocumentId`, while restricting to at most one Active session per `DocumentId`. `ReviewSessionStatus.Active = 0`, `ReviewSessionStatus.Completed = 1`.

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

### Schema-9 activation behavior

Initialization reads `PRAGMA user_version` before touching any table and then
follows exactly one path:

| Source version | Behavior |
| --- | --- |
| Fresh / empty database | Initializes directly to a validated schema 9. |
| 0–6 | Creates or updates the registered tables to reach the schema-7 baseline boundary, applies the legacy enum backfills, and then migrates to schema 8, and finally to schema 9. |
| 7 | Migrates to schema 8, and then to schema 9. |
| 8 | Validates schema-8 shape, then migrates to schema 9. |
| 9 (valid) | Validation only. The database is inspected and never mutated. |
| 9 (malformed) | Fails closed. Nothing is repaired and nothing is written. |
| Greater than 9 | Rejected with `DatabaseSchemaCompatibilityException` before any table or cache change. |

The legacy enum backfills assign deterministic supported values for
`Words.TokenKind`, `Words.PreparationState`, `Words.AutomaticInteractionMode`,
`Meanings.TokenKind`, `WordOccurrences.TechnicalFamily`, and the
`Documents`/`LexicalCache` lookup mode before activation.

The 7 → 8 → 9 migrations run inside real SQLite transactions. They are rollback-safe,
cancellation-safe, and retryable.

**Schema 9 Migration (Source 8, Target 9):**
The schema-9 migration structurally discovers and replaces the legacy unconditional unique single-column index on `ReviewSessions(DocumentId)`.
The migration creates two new indexes:
- `IX_ReviewSessions_DocumentId` (non-unique and non-partial)
- `IX_ReviewSessions_DocumentId_Active` (unique and partial, predicate exactly `Status = 0`)

The legacy entity-level unique index must be absent in a valid schema-9 database, though schema 7/8 bootstrap may still use it before migration. Migration is transactional and changes indexes/version only, not review rows. Schema-9 shape validation occurs after the migration or when already applied.

### Structural validation

Before a schema-8 database is exposed to services it must pass structural and
logical validation covering:

- required tables and required columns;
- declared column nullability and primary-key semantics as the real DDL declares
  them (an `INTEGER PRIMARY KEY` rowid alias is validated by primary-key position
  and affinity, not by the reported `notnull` flag);
- absence of legacy artifacts such as `LearningCards.MeaningId` and the retired
  unconditional `ReviewSessions(DocumentId)` unique index;
- index definitions including ordered columns, collation, uniqueness, and partial
  predicates (e.g. `IX_ReviewSessions_DocumentId_Active`);
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

The supported portable format is the `.kfarchive` archive. A schema-9 database
exports archive format **v2**, and merge safety copies are captured as v2.
Archive format **v1** remains readable and can still be restored into a schema-9
target (upgraded in memory for Schema-9 targets).

Import into an **empty** target uses restore-into-empty. Import into a **populated**
Schema-9 target is merged transactionally: validation → preflight planning →
validated safety copy → transactional merge writer → deterministic card-schedule
replay → atomic commit or rollback. Stale or non-executable plans are rejected.
Multiple imports converge without duplicates. The merge writer reuses existing
entities, inserts missing entities and preserved variants, and applies enrichment
policies. Failure and cancellation roll back completely. See `MergePreflightPlannerV2`,
`MergeWriterService`, and `MergeWriterExecutor`. Archive-v1 upgrades in memory
for Schema-9 targets; archive-v2 into Schema 7 is rejected.

This general populated-target merge support is established. Package B (writer evidence for divergent completed Schema-9 review histories) is merged on `master`: divergent completed `ReviewSession` rows for one Document coexist correctly, exact duplicates are skipped, and reimport of an already-merged history converges to no change.

Package C (cross-installation canonical-ordering hardening) is merged via PR #68 (merge commit `db47de3bf48b49b5258ce16acc6e3e543d96143c`) and is now binding master behavior. For the affected v2 export subgraphs:
- `SourceMaterial` archive-local `sm-*` / `ss-*` assignment is hardened against relevant cross-installation local-row/enumeration differences;
- completed `ReviewSession` `vr-*` / `rc-*` assignment is hardened against the reviewed tied-session/candidate-history case;
- focused two-installation convergence and repeated-exchange evidence exists.

Package D (KF-BACKUP-003, `PreparationSession`/`LearningSession`/`LearningReview` v2 canonical-ordering hardening) is merged via PR #76 (merge commit `17d3f1a031b9f319041ff1034a227d17b1029c4f`) and is now binding master behavior; `POST_MERGE_SYNC_ONLY` completed successfully. For the affected v2 export collections:
- completed `PreparationSession`/`PreparationCandidate` archive-local `pb-*` / `pi-*` assignment is hardened against relevant local-row/enumeration differences;
- completed `LearningSession`/`LearningSessionCard` archive-local `ls-*` / `lq-*` assignment is hardened the same way;
- `LearningReview` export ordering is total over every emitted review field, including `LearningSessionId`, `TargetAnswerVariantId`, and `MatchedAnswerVariantId`;
- this is archive-emission canonical ordering only, never a merge identity — no `MergePreflightPlannerV2`/`MergeWriterExecutor` identity or writer behavior changed.

Populated-target merge remains non-destructive and transactional; exact duplicate histories remain deduplicated; divergent completed histories remain preservable/additive under Schema 9; repeated merge converges/no-changes; these semantics are unchanged by Package D. Schema and archive-format compatibility rules are unchanged. This does not claim universal whole-archive byte equality.

### Populated-target LearningReview merge rules (KF-BACKUP-004 — current branch, not yet on master)

The following rules are the contract carried by the `KF-BACKUP-004` package on branch `fix/schema9-learningreview-merge-integrity-v1`. That package is implemented and automated-validated but **not committed, pushed, in a pull request, or merged**, so these rules are not yet present on `master`; the Package D facts above *are* already merged. See [architecture/backup-merge-v1-design.md](architecture/backup-merge-v1-design.md) §22.

- Every physical archive `LearningReview` row receives its own deterministic positional plan-action lookup key derived from its position in the archive's review collection, so two rows for the same card at the same `ReviewedAtUtc` can never resolve to one another's plan action. Planner and writer derive that key identically.
- The lookup key is addressing only. Duplicate/event identity is a separate, content-derived fingerprint and is never the lookup label.
- The Schema-9 meaning-aware review event identity is content-derived over the card's stable semantic identity, `ReviewedAtUtc`, `Rating`, `WasTypedAnswer`, `WasCorrect`, `DueAtUtc`, `IntervalDays`, `EaseFactor`, and the stable nullable `TargetAnswerVariant` and `MatchedAnswerVariant` identities. Answer-variant references are compared as content-derived identities, never as archive-local or target-local row ids, and absent-versus-present is a significant distinction.
- `LearningSessionId` is preserved as the review's referential relationship to its workflow session — every inserted review keeps the session its source row referenced — but is **not** part of the event identity.
- Deterministic scheduler replay uses the same event distinctness and tie-break semantics as preflight, so events the planner keeps distinct are never re-collapsed during replay.
- Repeated import remains convergent: an unchanged archive re-imported after a merge still reports no change.

No database schema or migration change, and no archive-format change, accompanies these rules. The persisted Slice-1 review-fingerprint API and its `KnownFirst.Merge.LearningReview.v1` domain are unchanged for their existing callers.

Synchronization and cloud formats do not exist.

Any accepted decision that changes these rules requires an ADR and an update to
this contract, `PROJECT_STATE.md`, tests, and user-facing documentation.
