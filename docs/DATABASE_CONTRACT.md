# KnownFirst database contract

## Status and scope

This document is the binding contract for KnownFirst persisted application
data, schema compatibility, migrations, and database-test safety.

It describes the current SQLite model at schema version 7. It does not define a
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

`DatabaseSchema.CurrentVersion` and `PRAGMA user_version` are both **7**.

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
| `LearningCards` | Independent card-direction scheduling state |
| `LearningReviews` | Persisted rating history |
| `LearningSessions` | Resumable learning-session summary state |
| `LearningSessionCards` | Ordered persisted session queue and reveal/check state |

Relationships are represented by entity IDs and enforced by transactional
service operations and tests. Do not introduce a competing representation of
the same document, vocabulary, meaning, context, card, or session.

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

Current initialization creates or updates all registered tables and then sets
`PRAGMA user_version = 7`. The checked-in migration regression constructs an
older `Words` table in a temporary database, preserves its existing row, and
verifies defaults for `AutomaticInteractionMode` and
`ConsecutiveRecallSuccessCount`.

That single fixture is not a complete historical migration audit. Expanding the migration fixture matrix remains separately planned work.

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

No supported backup, restore, export, synchronization, or cloud format exists in the current KnownFirst source state. Future work must not infer a format directly from the physical SQLite file.

Before implementation, Data Safety v1 must define:

- included and excluded data;
- format and schema versioning;
- integrity and authenticity checks;
- compatibility and downgrade behavior;
- atomic restore and failure recovery;
- conflict and overwrite semantics;
- free-space and interruption handling; and
- privacy-safe automated fixtures.

Any accepted decision that changes these rules requires an ADR and an update to
this contract, `PROJECT_STATE.md`, tests, and user-facing documentation.
