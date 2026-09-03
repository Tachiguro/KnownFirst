# KnownFirst database contract

## Status and scope

This document is the binding contract for KnownFirst persisted application
data, schema compatibility, migrations, and database-test safety.

It describes the current SQLite model at schema version **13** on merged `master` (`KF-FSRS-003`). Schema 10, 11, and 12 sections below remain historical/compatibility contracts where explicitly scoped; Schema-13 persistence and FSRS-6 production activation are documented in the [Schema-13 Production Contract](#schema-13-production-contract-kf-fsrs-003) section below. Merged KF-BACKUP-005B changes portable Active learning-workflow behavior without changing the physical schema version or archive format; its binding current-master contract is recorded explicitly below.

## Storage boundary

- Personal documents, vocabulary state, prepared content, schedules, and
  settings-related workflow state remain local to the device.
- The application database is named `knownfirst.db3` and lives in the
  platform application-data directory.
- Automated tests use isolated temporary databases only.
- A real user database must never be opened, copied, migrated, reset, or
  deleted by an automated test or routine development check.

## Current schema

`DatabaseSchema.CurrentVersion` and `PRAGMA user_version` are both **13** in merged production source after `KF-FSRS-003`.
A healthy initialized current production database reports `PRAGMA user_version = 13`. `PRAGMA foreign_keys = ON` is enforced globally for every production connection before any other operation.

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
| `LearningSessions` | Resumable learning-session summary state; on Schema 10 also carries an immutable `StableId` |
| `LearningSessionCards` | Ordered persisted session queue, frozen answer target, and reveal/check state; on Schema 10 also carries an immutable `StableId` |
| `Senses` | Meaning-centric Sense identity, provenance, and status per vocabulary identity |
| `AnswerVariants` | Distinct accepted answer expressions per Sense and answer language |
| `SenseAnswerVariantAssignments` | Direction-specific Required/AcceptedOnly assignment, preferred flag, and Required-epoch boundary |
| `AnswerVariantProgress` | Replayable per `(CardId, AnswerVariantId)` interaction progress; legacy mastery fields are not Schema-13 learning outcomes |
| `DerivedTermEvidenceEntries` | German Enhanced Term Recognition (Schema 11): per-occurrence provenance for a derived compound component, always pointing at the complete whole-compound source span (never a synthetic component occurrence) |
| `LearningDayState` | Learning-Day Infrastructure (Schema 12): singleton record (`Id = 1`) tracking active budget day / Bridge phase, current day ordinal, frozen day boundaries, effective timezone/cutoff, and Bridge target state |
| `LearningDayGrants` | Daily New-Word Budget (Schema 12): durable record of admitted genuinely-new `WordId`s per logical learning day ordinal with immutable `SlotOrdinal` assignments; independent of learning-graph deletions |
| `FsrsCardStates` | Schema 13: one-to-one FSRS-6 scheduling state for each `LearningCard`; state, stability, difficulty, last-reviewed timestamp, step index, and due timestamp |
| `FsrsReviewHistoryEntries` | Schema 13: append-only factual review log; unique `StableId`, card-scoped gapless 1-based `SequenceNumber`, `ReviewedAtUtc`, and `Rating` per event |
| `WordLearningControls` | Schema 13: reversible word-level AlreadyKnown decisions; absence = Default |
| `SenseLearningControls` | Schema 13: reversible sense-level StopLearning decisions; absence = Default |

At schema 8 `LearningCards.MeaningId` no longer exists; the card's own preferred
meaning is `LearningCards.PreferredMeaningId`, and the card is addressed by
`SenseId`.

At schema 9, `ReviewSessions` index constraints change to support multiple Completed sessions per `DocumentId`, while restricting to at most one Active session per `DocumentId`. `ReviewSessionStatus.Active = 0`, `ReviewSessionStatus.Completed = 1`.

At schema 10, `LearningSessions` and `LearningSessionCards` carry immutable `StableId` columns and are constrained by unique indexes `IX_LearningSessions_StableId` and `IX_LearningSessionCards_StableId`.

At schema 11, the new `DerivedTermEvidenceEntries` table records provenance for German derived-compound review candidates (`CandidateProvenanceKind.DerivedFromCompound`). A derived candidate never receives a synthetic `WordOccurrenceEntity`; instead its evidence row carries the source compound's identity, exact surface form, whole-compound `SourceStartPosition`/`SourceLength`, sentence order, and component form, always pointing at the real complete source-compound occurrence. Full binding contract: [Schema-11 contract](#schema-11-derived-term-evidence-contract) below and [docs/WORD_ANALYSIS.md](WORD_ANALYSIS.md) "Conservative German derived compound candidates."

At schema 12, `LearningDayState` and `LearningDayGrants` track durable logical learning days, timezone/cutoff freeze, Bridge intervals, and daily new-word grant ordinals. These tables are strictly installation-local and excluded from portable export/merge. Full binding contract: [Schema-12 contract](#schema-12-learning-day-and-daily-new-word-budget-contract-merged-production-state) below.

At current Schema 13 (merged `KF-FSRS-003`), `FsrsCardStates`, `FsrsReviewHistoryEntries`, `WordLearningControls`, and `SenseLearningControls` are present. `FsrsCardStates` holds the factual FSRS-6 card state per `LearningCard`. `FsrsReviewHistoryEntries` is the append-only factual review log with gapless 1-based `SequenceNumber` per card and a unique `StableId` per event. `WordLearningControls` and `SenseLearningControls` are reversible clean learning-control decisions with separate semantic ownership. Full binding contract: [Schema-13 Production Contract](#schema-13-production-contract-kf-fsrs-003) below.

Relationships are represented by entity IDs and enforced by transactional
service operations and tests. Do not introduce a competing representation of
the same document, vocabulary, meaning, sense, context, card, or session.

## Required invariants

1. Original accepted document content is unchanged.
2. Sentence and occurrence ranges resolve to the exact original substrings.
3. One vocabulary identity may have many occurrences and surface forms.
4. Frequency equals accepted occurrences; context deduplication does not lower
   it.
5. Schema-13 Learn represents `AlreadyKnown` with the word-level clean control,
   preserving the semantic vocabulary graph, accepted content, contexts,
   LearningCards, factual review history, and FSRS state/history. It may remove
   incomplete queue work and normalize affected sessions under the
   [Data deletion](#data-deletion) contract. Historical destructive minimal-marker
   semantics apply only to the explicitly scoped legacy or review/Preparation
   disposition contracts; they are not a global AlreadyKnown invariant.
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

### Schema-13 production activation behavior (KF-FSRS-003)

`DatabaseSchema.InitializeAsync` reads `PRAGMA user_version` before touching any table and then
follows exactly one path. **`PRAGMA foreign_keys = ON` is set first, before version inspection.** Startup
fails immediately if the pragma value does not confirm `1`.

| Source version | Behavior |
| --- | --- |
| Fresh / empty database (version = 0, no user objects) | `Schema13CleanBootstrap.ApplyAsync` initializes directly to a validated Schema 13. |
| 0 with user objects (unknown non-empty unversioned) | Rejected with `DatabaseSchemaCompatibilityException` (reason `UnknownNonEmptyUnversionedDatabase`). Nothing is read, repaired, or written. |
| 1–12 | Rejected with `DatabaseSchemaCompatibilityException` (reason `UnsupportedOlderVersion`). No automatic migration from Schema 1–12 occurs in normal production startup. `Schema13DormantMigration.ApplyAsync` remains available as an explicit callable for Schema 12 → 13 migration, but is not invoked by `InitializeAsync`. |
| 13 (valid) | `Schema13RuntimeIntegrityValidator.Validate` runs. If valid, startup completes normally without any mutation. |
| 13 (malformed) | Rejected with `DatabaseSchemaCompatibilityException` (reason `InvalidCurrentSchema`). Nothing is repaired or written. |
| Greater than 13 | Rejected with `DatabaseSchemaCompatibilityException` (reason `UnsupportedFutureVersion`). Nothing is read, repaired, or written. |


The following legacy backfill and migration descriptions are historical Schema-12 behavior; they are not normal Schema-13 production startup paths.

The legacy enum backfills assign deterministic supported values for
`Words.TokenKind`, `Words.PreparationState`, `Words.AutomaticInteractionMode`,
`Meanings.TokenKind`, `WordOccurrences.TechnicalFamily`, and the
`Documents`/`LexicalCache` lookup mode before activation.

The 7 → 8 → 9 → 10 → 11 → 12 migrations run inside real SQLite transactions. They are rollback-safe,
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
- Schema-13 Learn permanent-known persists the word-level AlreadyKnown control
  and removes incomplete queue work while preserving the semantic graph,
  LearningCards, FSRS state/history, and factual reviews. Separate review/Preparation
  dispositions and historical document cleanup follow the scoped lifecycle rules
  in [KNOWNFIRST_ARCHITECTURE.md](KNOWNFIRST_ARCHITECTURE.md).
- Cache invalidation must not delete personal meanings, review decisions,
  schedules, or history.
- Maintenance must be idempotent and must not block initial UI rendering.

## Backup and restore boundary

The supported portable format is the `.kfarchive` archive. A schema-10 or schema-11 database
exports archive format **v2** (with trailing nullable workflow StableId extensions),
and merge safety copies are captured as v2. Schema 11's own baseline introduced no
archive-format change. German Package 5A-2 (merged via PR #137, merge commit `5d1d3c05bae6ab9f1c56d8c5f9a227121f432f9a`) adds a
`DerivedTermEvidence` collection to the existing V2 payload without bumping the archive
version or activating a required/optional feature — see the
[Schema-11 contract](#schema-11-derived-term-evidence-contract) below for the exact current
portable-export boundary and its merge status.
Archive format **v1** remains readable and can still be restored into a schema-10/11
target (upgraded in memory); the V1-upgrade path supplies an empty `DerivedTermEvidence`
collection, since format v1 never carried Schema-11 material.

Import into an **empty** target uses restore-into-empty. Import into a **populated**
target is merged transactionally: validation → preflight planning →
validated safety copy → transactional merge writer → deterministic card-schedule
replay → atomic commit or rollback. Stale or non-executable plans are rejected.
Multiple imports converge without duplicates. The merge writer reuses existing
entities, inserts missing entities and preserved variants, and applies enrichment
policies. Failure and cancellation roll back completely. See `MergePreflightPlannerV2`,
`MergeWriterService`, and `MergeWriterExecutor`. Archive-v1 upgrades in memory
for Schema-10 targets; archive-v2 into Schema 7 is rejected.

This general populated-target merge support is established. Package B (writer evidence for divergent completed Schema-9 review histories) is merged on `master`: divergent completed `ReviewSession` rows for one Document coexist correctly, exact duplicates are skipped, and reimport of an already-merged history converges to no change.

Merged KF-BACKUP-005B establishes empty-target Active Schema-10 restore through an ordinary portable archive. Binding current-`master` KF-BACKUP-005C behavior, merged via PR #83 (merge commit `bed54d01624e80ca6dd5adf8af097e64fe33e588`), extends the populated Schema-10 path under the contract below; `POST_MERGE_SYNC_ONLY` completed successfully. See [KF-BACKUP-005B portable Active-workflow contract](#kf-backup-005b-portable-active-workflow-contract-merged-master-behavior) and the populated-target contract below.

Package C (cross-installation canonical-ordering hardening) is merged via PR #68 (merge commit `db47de3bf48b49b5258ce16acc6e3e543d96143c`) and is now binding master behavior. For the affected v2 export subgraphs:
- `SourceMaterial` archive-local `sm-*` / `ss-*` assignment is hardened against relevant cross-installation local-row/enumeration differences;
- completed `ReviewSession` `vr-*` / `rc-*` assignment is hardened against the reviewed tied-session/candidate-history case;
- focused two-installation convergence and repeated-exchange evidence exists.

Package D (KF-BACKUP-003, `PreparationSession`/`LearningSession`/`LearningReview` v2 canonical-ordering hardening) is merged via PR #76 (merge commit `17d3f1a031b9f319041ff1034a227d17b1029c4f`) and is now binding master behavior; `POST_MERGE_SYNC_ONLY` completed successfully. For the affected v2 export collections:
- completed `PreparationSession`/`PreparationCandidate` archive-local `pb-*` / `pi-*` assignment is hardened against relevant local-row/enumeration differences;
- completed `LearningSession`/`LearningSessionCard` archive-local `ls-*` / `lq-*` assignment is hardened the same way;
- `LearningReview` export ordering is total over every emitted review field, including `LearningSessionId`, `TargetAnswerVariantId`, and `MatchedAnswerVariantId`;
- this is archive-emission canonical ordering only, never a merge identity — no `MergePreflightPlannerV2`/`MergeWriterExecutor` identity or writer behavior changed.

Populated-target merge remains non-destructive and transactional; exact duplicate histories remain deduplicated; divergent completed histories remain preservable/additive; repeated merge converges/no-changes; these semantics are unchanged by Package D and Schema 10. Schema and archive-format compatibility rules are unchanged. This does not claim universal whole-archive byte equality.

### Populated-target LearningReview merge rules (KF-BACKUP-004 — merged via PR #77)

The following rules are the contract established by `KF-BACKUP-004`, merged to `master` via PR #77 (merge commit `bec861fb8a054beb2804f1132b450da1e45dee90`) with `POST_MERGE_SYNC_ONLY` completed successfully. These rules are binding master behavior. See [architecture/backup-merge-v1-design.md](architecture/backup-merge-v1-design.md) §22.

- Every physical archive `LearningReview` row receives its own deterministic positional plan-action lookup key derived from its position in the archive's review collection, so two rows for the same card at the same `ReviewedAtUtc` can never resolve to one another's plan action. Planner and writer derive that key identically.
- The lookup key is addressing only. Duplicate/event identity is a separate, content-derived fingerprint and is never the lookup label.
- The Schema-9/10 meaning-aware review event identity is content-derived over the card's stable semantic identity, `ReviewedAtUtc`, `Rating`, `WasTypedAnswer`, `WasCorrect`, `DueAtUtc`, `IntervalDays`, `EaseFactor`, and the stable nullable `TargetAnswerVariant` and `MatchedAnswerVariant` identities. Answer-variant references are compared as content-derived identities, never as archive-local or target-local row ids, and absent-versus-present is a significant distinction.
- `LearningSessionId` is preserved as the review's referential relationship to its workflow session — every inserted review keeps the session its source row referenced — but is **not** part of the event identity.
- Deterministic scheduler replay uses the same event distinctness and tie-break semantics as preflight, so events the planner keeps distinct are never re-collapsed during replay.
- Repeated import remains convergent: an unchanged archive re-imported after a merge still reports no change.

No database schema or migration change, and no archive-format change, accompanies these rules. The persisted Slice-1 review-fingerprint API and its `KnownFirst.Merge.LearningReview.v1` domain are unchanged for their existing callers.

Synchronization and cloud formats do not exist.

Any accepted decision that changes these rules requires an ADR and an update to
this contract, `PROJECT_STATE.md`, tests, and user-facing documentation.

---

## Schema-10 Stable Learning-Workflow Identity Contract

### Schema-10 migration intent

Schema 10 establishes immutable stable identifiers (`StableId`) for persisted learning-workflow entities. These identifiers:

- are assigned once, on first creation or on migration from a prior schema;
- are never changed by subsequent workflow operations (rating, pruning, Again/repeat, counters, or completion);
- enable portable archive transport and future cross-device synchronization without depending on installation-local SQLite row ids.

The `StableId` architecture is intentionally reusable by later cross-device synchronization and is not a backup-only disposable identity scheme.

### New physical columns

Schema 10 adds a nullable `TEXT` `StableId` column to:

| Table | Assigned on |
| --- | --- |
| `LearningSessions` | Session creation (new) or migration bootstrap (legacy) |
| `LearningSessionCards` | Queue-row creation (new) or migration bootstrap (legacy) |

No other table receives `StableId` columns in Schema 10. The physical Schema-8/9 entity definitions for `LearningSessions` and `LearningSessionCards` do not contain these columns; they are added by the Schema-10 migration via `ALTER TABLE ... ADD COLUMN`. While physically declared as nullable `TEXT` in SQLite DDL, shape validation and unique indexes (`IX_LearningSessions_StableId` and `IX_LearningSessionCards_StableId`) enforce that every persisted row in a valid Schema-10 database contains a non-null canonical `StableId`.

### Canonical StableId form

A valid `StableId` must satisfy all of:

- **Encoding:** lowercase hexadecimal only (no uppercase, no dashes, no braces).
- **Length:** exactly 32 characters (GUID origin) or exactly 64 characters (SHA-256 origin).
- **Uniqueness:** unique within its table across all rows.
- **Immutability:** never mutated after first assignment.

A `StableId` that fails any of these constraints is invalid. Invalid or absent StableIds on source ≥10 archives are rejected.

### New-row identity allocation

Fresh post-Schema-10 workflow entities receive a 32-character canonical `StableId` derived from `Guid.NewGuid().ToString("N")` (lowercase hexadecimal, no separators). This format is the canonical form for new workflow identities.

### Legacy Completed session identity (deterministic bootstrap)

A legacy Completed `LearningSession` (Status = Completed at migration time) receives a deterministic 64-character lowercase SHA-256 `StableId` computed under the frozen domain:

```
KnownFirst.Identity.LearningSession.LegacyCompletedBootstrap.v1
```

Semantic material included in the hash:

- `StartedAtUtc`
- `CompletedAtUtc`
- Ordered workflow material: for each queue item in `QueueOrder` order, a `(FutureCardIdentity, Rating)` pair

The hash does not depend on installation-local SQLite row ids or archive-local ordinals. Identical Completed sessions on different installations produce identical `StableId` values.

### Legacy Completed queue identity (deterministic bootstrap)

A legacy Completed `LearningSessionCard` row (belonging to a Completed session at migration time) receives a deterministic 64-character lowercase SHA-256 `StableId` computed under the frozen domain:

```
KnownFirst.Identity.LearningQueueItem.LegacyCompletedBootstrap.v1
```

Semantic material included in the hash:

- Parent Completed session `StableId` (already assigned above)
- `QueueOrder`
- `FutureCardIdentity`
- `IsAgainRepeat`

This identity is also installation-independent for semantically identical queue content.

### Legacy Active identity (one-time GUID bootstrap)

A legacy Active `LearningSession` (Status = Active at migration time) and its associated `LearningSessionCard` rows receive fresh 32-character canonical GUID `StableId` values once, at migration time. These identities:

- are not deterministic or independently reproducible from workflow content on another installation; they identify the specific durable Active workflow and are transported unchanged when source ≥10 portability permits it;
- remain stable through all later workflow mutations: rating, pruning, Again/repeat, counter changes, and eventual completion;
- are never reassigned after the initial migration bootstrap.

### Identity immutability

A `StableId` assigned to a `LearningSession` or `LearningSessionCard` row must never be updated, overwritten, or removed by any subsequent operation. This constraint applies to:

- normal workflow operations (rating, pruning, session completion);
- import/merge operations (transported StableIds are preserved unchanged for source ≥10 archives);
- schema migrations (Schema 10 assigns once; later migrations must not reassign).

### Schema-10 activation behavior

This table describes the schema-10 (StableId) migration step in isolation. Schema 10 is no longer the terminal version on `master`: a source-10 database is validated and then migrates onward to schema 11 — see [Schema-11 activation behavior](#schema-11-activation-behavior) above for the full current terminal path.

| Source version | Behavior |
| --- | --- |
| Fresh / empty database | Initializes directly to a validated schema 10, then continues to schema 11. |
| 0–6 | Advances to schema-7 baseline, enum backfills, schema 8, schema 9, then schema 10 (then continues to schema 11). |
| 7 | Migrates to schema 8, then schema 9, then schema 10 (then continues to schema 11). |
| 8 | Validates schema-8 shape, migrates to schema 9, then schema 10 (then continues to schema 11). |
| 9 | Validates schema-9 shape, then applies schema-10 migration (adds StableId columns, assigns bootstrap identities, creates unique indexes; then continues to schema 11). |
| 10 (valid) | Validates schema-10 shape, then continues to schema 11. |
| 10 (malformed) | Fails closed. Nothing is repaired and nothing is written. |

The schema-10 migration is transactional and rollback-safe.

### Archive/source compatibility under Schema 10

| Source schema | Completed portable workflow | Active portable workflow |
| --- | --- | --- |
| ≤9 | Supported; Schema-8/9 ordinary portable export remains Completed-only and Completed sessions/queue rows may receive deterministic bootstrap StableIds during import | Unsupported/rejected with the established Active-workflow boundary |
| ≥10 | Supported; StableIds must be present, canonical, unique, and transported unchanged | Supported on current `master` by KF-BACKUP-005B for ordinary export and restore into an empty Schema-10 target; StableIds must be present, canonical, unique, and transported unchanged |

For source ≥10 archives, transported `StableId` values are preserved unchanged. They are validated for canonical form and uniqueness before any mutation.

The outer `.kfarchive` format remains version V2 (not incremented). However, Schema 10 extends the existing V2 learning-workflow DTOs with trailing nullable `StableId` fields on `BackupLearningWorkflowV2` and `BackupLearningQueueItemV2`. These fields are nullable specifically so pre-Schema10 (source ≤9) archives remain readable without carrying workflow StableIds. Source schema ≤9 may omit these StableIds where compatibility rules allow it. Source schema ≥10 requires the relevant workflow StableIds to be present and pass canonical/uniqueness validation; transported valid source ≥10 StableIds are preserved unchanged. Source schema metadata determines whether missing StableIds are legacy-compatible or invalid.

### KF-BACKUP-005C populated-target Active-workflow contract (merged master behavior)

**Lifecycle status:** binding current-`master` behavior, merged via PR #83 at `bed54d01624e80ca6dd5adf8af097e64fe33e588` (feature head `bc30e9ee9a3689cc4d8b7d108ac83dc037a1b962`); `POST_MERGE_SYNC_ONLY` completed successfully.

For a Schema-10/V2 archive carrying an Active learning workflow, a populated target with no blocking Active target workflow may merge the workflow additively through the existing writer: workflow/queue `StableId` values are preserved, committed reviews attach to the newly allocated target-local `LearningSession.Id`, and existing scheduler replay applies. No separate Active-workflow update engine exists.

For the same Active workflow `StableId`, exact durable equivalence yields `NoChanges` and returns before safety-copy, writer, or scheduler replay. Equivalence compares workflow scalars, queue StableId topology/content and semantic card/nullable answer-variant identities, and a multiplicity-aware multiset of existing KF-BACKUP-004 semantic LearningReview fingerprints. `LearningSessionId` remains referential attachment and is excluded from review-event identity.

Any non-exact same-`StableId` Active workflow, including scalar, queue, review-event, review-multiplicity, or archive-Active/target-Completed mismatch, yields `RequiresUserDecision`, a non-executable plan, and zero target mutation. Archive-Completed/target-Active retains the existing `BlockedByActiveWorkflow` / `ActiveWorkflowUnsupported` boundary. The Active-aware capture is read-only and preflight-only; safety-copy capture and transaction-time stale-plan safeguards remain fail-closed and unchanged. Schema remains 10 and archive format remains V2; no Schema 11, archive V3, DTO redesign, StableId-format change, new public status/error code, UI, or sync transport was introduced.

### KF-BACKUP-004 LearningReview identity boundary

`LearningSessionId` remains **not** part of `LearningReview` merge identity. This boundary established by KF-BACKUP-004 is unchanged by Schema 10. `LearningSessionId` is preserved as referential workflow attachment and provenance for each inserted review row. The KF-BACKUP-004 contract in the [Populated-target LearningReview merge rules](#populated-target-learningreview-merge-rules-kf-backup-004--merged-via-pr-77) section above is unaffected.

### Historical 005A Active portability exclusion

KF-BACKUP-005A itself did not implement portable Active learning-workflow continuation: its historical `master` baseline excluded Active `LearningSessions` from ordinary portable export and rejected unsupported Active workflow archives. KF-BACKUP-005B added Schema-10 export plus empty-target restore; KF-BACKUP-005C now supplies the bounded populated-target convergence contract.

### KF-BACKUP-005B portable Active-workflow contract (merged master behavior)

KF-BACKUP-005B preserves `DatabaseSchema.CurrentVersion = 10` and archive format V2. It introduces neither Schema 11 nor archive V3.

For an ordinary portable archive captured from a Schema-10 source:

- one Active `LearningSession` is included together with its complete persisted `LearningSessionCard` queue state;
- committed `LearningReview` rows belonging to that Active workflow are included;
- the workflow `StableId` and every queue-row `StableId` are mandatory, canonical, unique, and transported unchanged;
- transient/uncommitted UI state is outside the portability claim.

Restore into an **empty Schema-10 installation**:

- recreates the workflow as Active and never fabricates `Completed` status or a completion timestamp;
- preserves already-completed queue items, their persisted ratings/completion state, committed mid-session review history, remaining incomplete queue items, and queue ordering;
- allocates a new installation-local integer `LearningSession.Id` and remaps each restored `LearningReview.LearningSessionId` to it while leaving `LearningSessionId` excluded from review-event merge identity;
- preserves the transported workflow and queue `StableId` values unchanged;
- resumes through the normal production `LearningService` path from the last durably committed application/database state, so finishing the session before export is not required.

Completed Schema-10 workflows remain supported: Completed status, non-null completion timestamp, queue/history, workflow `StableId`, and queue-row `StableId` values survive empty-target restore. This behavior was explicitly regression-tested by the 005B package.

Legacy and unsupported boundaries remain explicit:

- Schema-8 and Schema-9 ordinary portable export remain Completed-only;
- source schema ≤9 Active learning-workflow archives remain unsupported/rejected;
- Active `VocabularyReview` remains unsupported;
- Active `PreparationBatch` remains unsupported.

The populated-target guard is the historical 005B boundary, superseded on current `master` by KF-BACKUP-005C only for a learning-quiescent populated target. Exact same-`StableId` Active state is `NoChanges`; non-exact same-`StableId` state is `RequiresUserDecision`, non-executable, and zero-mutation. Archive-Completed/target-Active remains `BlockedByActiveWorkflow` / `ActiveWorkflowUnsupported`.

The stable workflow representation remains intentionally reusable by a later cross-device synchronization design. KF-BACKUP-005B implements no network/cloud transport, accounts, or remote synchronization service.

### Follow-up packages

- **KF-BACKUP-005A:** Schema-10 stable learning-workflow identity foundation. Merged via PR #79 (merge commit `e56b8bfa27dfe1d630fbacfed24e6d56ea876026`); `POST_MERGE_SYNC_ONLY` completed successfully.
- **KF-BACKUP-005B:** merged via PR #81 (feature commit `e8236bba3d23e942014e6979b661e0c77a2a3bdd`, merge commit `dc56e8412966ac32531c4b0358526582702d6d24`); `POST_MERGE_SYNC_ONLY` completed. It is binding current-master behavior for portable Active learning-workflow export and empty-target restore from durable state.
- **KF-BACKUP-005C:** binding current-master populated-target Active workflow convergence and conflict safety, merged via PR #83 (merge commit `bed54d01624e80ca6dd5adf8af097e64fe33e588`); `POST_MERGE_SYNC_ONLY` completed successfully.

---

## Schema-11 Derived-Term Evidence Contract

**Lifecycle status:** the `DerivedTermEvidenceEntries` table and `DatabaseSchema.CurrentVersion = 11` activation are binding current-`master` behavior, merged via PR #134 (merge commit `6c7a89ed6b4b0fc7701fdca8ec85a38b91bbeeb5`). The post-review-completion **retention/cleanup lifecycle** behavior described below (German Enhanced Term Recognition Package 5A) is also binding current-`master` behavior, merged via PR #135 (merge commit `683f34473dd21417be9d8e1b60d04de539fb35a8`). **The cross-installation portable-archive transport of this evidence (German Package 5A-2) is also binding current-`master` behavior, merged via PR #137 (merge commit `5d1d3c05bae6ab9f1c56d8c5f9a227121f432f9a`; validated PR head `2ff447e9f874d49e72fee0a549820adc1bdc3b39`)** — see the "Portable-archive boundary for derived-term evidence" subsection below for the exact current contract. See [docs/CURRENT_WORK.md](CURRENT_WORK.md) for exact current lifecycle status.

Schema 11 adds `DerivedTermEvidenceEntries` for German derived-compound review candidates (`CandidateProvenanceKind.DerivedFromCompound`). A derived candidate never receives a `WordOccurrenceEntity` — its `DerivedTermEvidenceEntity` row instead retains the whole-compound source identity, exact surface form, `SourceStartPosition`/`SourceLength` (always the complete source-compound occurrence, never a component sub-span), sentence order, and component form.

Package 5A lifecycle behavior for this evidence:

- When a derived candidate is decided `UnknownBacklog`, its owning `ReviewCandidateEntity` and `DerivedTermEvidenceEntity` row(s) may survive normal review-session completion (`TextReviewService.CompleteSession`), instead of being deleted along with every other candidate. Known/Ignored derived candidates, and every Direct candidate, continue to be cleaned up exactly as before.
- This retention exists so Preparation can still recover a real source context for the derived word despite the deliberate absence of a synthetic `WordOccurrenceEntity`.
- The `Document` and exact `SentenceSpan` that a surviving evidence row depends on must remain retained for as long as that evidence exists — generic maintenance cleanup (document/sentence-span sweeps reached from any unrelated review or preparation completion) must not delete them. Genuinely unreferenced documents/sentences with no retained derived evidence remain fully cleanup-eligible.
- When the retaining word later leaves the Unknown lifecycle through MarkKnown or Exclude, its retained `DerivedTermEvidenceEntries` row(s) and owning `ReviewCandidateEntity` are deleted at that point — retained state does not leak indefinitely.
- Every surviving `DerivedTermEvidenceEntity` row is validated on every database open (fail-closed) to reference an existing `ReviewCandidateEntity` → `ReviewSessionEntity` → `DocumentEntity`, an in-bounds source range whose substring matches the recorded surface form, exactly one matching `SentenceSpanEntity`, and a `WordEntity` for its source identity.

### Portable-archive boundary for derived-term evidence

**Merged `master` state (Package 5A, PR #135):** `DerivedTermEvidenceEntries` is never captured into any archive DTO field, in either the full/internal backup or the portable-export capture path. Because that table was never exported, a Completed session's `ReviewCandidateEntity` row that exists solely to own retained derivation evidence would otherwise export as a provenance-less item on the target side; the portable mapper excluded exactly that specific candidate row from the exported review-workflow items. A Completed session's other, legitimate candidates (including ones written back by restore/merge) were unaffected and continued to export exactly as before.

**Merged `master` state (Package 5A-2, PR #137):** merged to `master` at `5d1d3c05bae6ab9f1c56d8c5f9a227121f432f9a` (validated PR head `2ff447e9f874d49e72fee0a549820adc1bdc3b39`), this package supersedes the Package-5A export exclusion above and implements the cross-installation transport this table's own comment previously tracked as future work. It does not change the archive version, activate a required/optional feature, or introduce Schema 12.

- **Portable DTO shape:** the V2 payload gains a top-level `DerivedTermEvidence` collection of `BackupDerivedTermEvidenceV2` records, each carrying the owning archive review-item reference plus the exact transported Schema-11 fields: `SourceIdentity`, `SourceSurfaceForm`, `SourceStartPosition`, `SourceLength`, `SourceSentenceOrder`, `ComponentForm`. A corresponding `BackupRecordCountsV2.DerivedTermEvidence` count is validated for internal consistency. Registered in `BackupJsonSerializerContextV2` (source-generated; no reflection fallback). A historical V2 archive with no derived evidence remains readable (empty collection, zero count); the V1-in-memory-upgrade path supplies an empty collection.
- **ReviewCandidate export:** the Package-5A portable-export exclusion of the retained evidence-owning candidate is removed. That candidate now exports through the ordinary completed vocabulary-review-item path with its existing history/state fields preserved, exactly like any other Completed-session candidate — no synthetic `ReviewCandidate`/history vessel is created. Archive-local ids (including the owning review-item reference) remain addressing-only and are never a cross-installation semantic identity.
- **Graph validation (before any mutation):** the archive-DTO validator mirrors the binding physical `Schema11EvidenceValidator` invariants — nonblank `SourceIdentity`/`SourceSurfaceForm`/`ComponentForm`, nonnegative `SourceStartPosition`, positive `SourceLength`, resolvable owning-candidate/session/document ownership, an in-bounds UTF-16 source range whose substring matches the recorded surface form, exactly one matching `SentenceSpan` order with the range fully contained inside it, a source identity that resolves to a `Word` row in the document's language, and rejection of duplicate semantic evidence (same owning item, `SourceIdentity`, `SourceStartPosition`, `SourceLength`, `ComponentForm`). Any violation fails closed before database mutation.
- **Snapshot/export coverage:** the Schema-11 evidence-enrichment step is now applied consistently by ordinary portable export, full/internal backup, the pre-merge safety copy, and the target-state capture used by populated-target preflight/writer re-evaluation — including the merge-safety-copy capture path, which previously never applied it at all (corrected by this branch, not a separate feature).
- **Empty-target restore:** `Schema8BackupImportRepository.ImportIntoEmptySchema8Database` resolves each transported evidence row to the newly allocated local `ReviewCandidateEntity` after inserting the ordinary archive review items; multiple evidence rows can reference one archive review item without multiplying the restored candidate; no synthetic `WordOccurrenceEntity` is created.
- **Populated-target merge:** `DerivedTermEvidence` is its own merge entity kind (not folded into `VocabularyReviewItem` classification). Its semantic merge identity is installation-independent — built from the owning candidate's existing `ReviewCandidateIdentity`, the source compound's own vocabulary identity (owning-document language plus `SourceIdentity`), `SourceStartPosition`, `SourceLength`, `SourceSentenceOrder`, and `ComponentForm` — with no SQLite id and no archive-local id participating (`SourceSurfaceForm` is deliberately excluded, since the graph-validation contract already proves it is fully determined by the owning document plus the source range). Merge is additive only: exact semantic duplicates are skipped, target-only evidence is untouched, there is no overwrite or delete, and the transported evidence attaches to the one resolved final `ReviewCandidate`, whether that candidate was newly inserted in this merge or already matched by identity. Repeated or two-installation exchange of unchanged content converges without duplicating candidates or evidence (proven by focused tests; the tests confirm no duplicate physical rows are ever created, though they do not currently assert every plan-classification-level detail field-by-field).
- **Lifecycle parity for transported evidence:** imported/merged evidence participates in the same Package-5A lifecycle as natively created evidence — Preparation can recover real source-compound context from it, MarkKnown and Exclude both remove the evidence and its owning retained candidate, and generic document/sentence-span cleanup continues to protect the Document/SentenceSpan dependency for as long as retained evidence (native or transported) exists.
- **Test evidence and completed lifecycle:** an independent `REVIEW_ONLY` found no production-code defect; two MAJOR test-coverage gaps (Exclude cleanup and generic-cleanup protection for imported/merged evidence) were closed by focused characterization/hardening tests that passed immediately, and a combined focused scope of 4 tests passed 4/0. The final independent `REVIEW_ONLY` reported 0 BLOCKER / 0 MAJOR findings. Exact-head `FULL_VALIDATION` on the validated PR head: 2248 passed / 0 failed, Windows Debug/Release PASS, Android Debug/Release PASS, exit code 0 (log `artifacts/launcher-logs/ValidateAll-20260821-233217.log`). `POST_MERGE_SYNC_ONLY` completed successfully; no lifecycle steps remain pending for Package 5A-2. See [docs/CURRENT_WORK.md](CURRENT_WORK.md) and [docs/PROJECT_STATE.md](PROJECT_STATE.md) for exact current status.

---

## Schema-12 Learning-Day and Daily New-Word Budget Contract (Merged Production State)

**Historical activation status:** merged production `master` state via PR #142 (`feat: add daily new-word learning-day budget`; merge commit `34afed431711dd165b334d66b50b251a839faf02`). `DatabaseSchema.CurrentVersion` and `PRAGMA user_version` were 12 at that package's merge. The Schema-12 activation and V2 boundaries below describe that historical stage; current startup and transport follow the Schema-13 production contract. The retained learning-day tables and installation-local isolation remain applicable.

Schema 12 introduces durable logical learning-day state, frozen timezone and cutoff tracking, Bridge intervals, and daily new-word grant tracking.

### Schema-12 migration intent

1. **Durable learning-day state:** Tracks the current logical learning day ordinal, active phase (`ActiveBudgetDay` or `Bridge`), frozen day start and end timestamps in UTC, and effective timezone and cutoff configurations.
2. **Durable new-word grants:** Records distinct genuinely-new `WordId`s admitted per logical learning day ordinal with immutable `SlotOrdinal` assignments ($0 \dots N-1$).
3. **Installation-local isolation:** Schema-12 learning-day state and grants are strictly local device state. They are excluded from portable `.kfarchive` export, full backup, merge safety copy, and populated-target merge. The portable archive format remains V2.
4. **Resilience to learning-graph deletion:** `LearningDayGrants` deliberately contains no foreign key constraints to `Words` or `LearningCards`. If an admitted word is later marked Permanently Known or its learning cards are removed, its historical grant record remains intact and its slot is not reopened on the same day.

### Physical tables

#### 1. `LearningDayState` (Singleton)

A single-row table enforced by table constraint `CHECK (Id = 1)`.

| Column | Type | Nullable | Description |
| --- | --- | --- | --- |
| `Id` | `INTEGER PRIMARY KEY` | No | Fixed primary key value `1`. |
| `Phase` | `INTEGER` | No | Enum `LearningDayPhase`: `1 = ActiveBudgetDay`, `2 = Bridge`. |
| `DayOrdinal` | `INTEGER` | No | Monotonically increasing positive integer identifying the logical learning day. |
| `ActiveDayStartUtc` | `DATETIME` | No | UTC start timestamp of the active budget day. |
| `ActiveDayEndUtc` | `DATETIME` | No | UTC end timestamp of the active budget day. |
| `FrozenTimeZoneId` | `TEXT` | No | Timezone identifier frozen for the active budget day. |
| `FrozenCutoffMinutes` | `INTEGER` | No | Cutoff minute-of-day ($0 \dots 1439$) frozen for the active budget day (default 0). |
| `BridgeStartedUtc` | `DATETIME` | Yes | UTC timestamp when the Bridge phase began (null during `ActiveBudgetDay`). |
| `BridgeTargetTimeZoneId` | `TEXT` | Yes | Target timezone identifier for Bridge completion. |
| `BridgeTargetCutoffMinutes` | `INTEGER` | Yes | Target cutoff minutes for Bridge completion. |
| `BridgeTargetUtc` | `DATETIME` | Yes | UTC boundary timestamp when the Bridge phase ends. |
| `UpdatedAtUtc` | `DATETIME` | No | Last update timestamp in UTC. |

#### 2. `LearningDayGrants`

Records each genuinely-new `WordId` admitted to a logical learning day.

| Column | Type | Nullable | Description |
| --- | --- | --- | --- |
| `Id` | `INTEGER PRIMARY KEY AUTOINCREMENT` | No | Row identifier. |
| `DayOrdinal` | `INTEGER` | No | Logical learning day ordinal matching `LearningDayState.DayOrdinal`. |
| `WordId` | `INTEGER` | No | Vocabulary identity admitted. No foreign key constraint. |
| `SlotOrdinal` | `INTEGER` | No | 0-based immutable presentation order within the day's budget ($0, 1, \dots, N-1$). |
| `GrantedAtUtc` | `DATETIME` | No | UTC timestamp when the grant was issued. |

**Indexes:**
- `IX_LearningDayGrants_DayOrdinal_WordId` (UNIQUE on `DayOrdinal, WordId`): ensures a `WordId` receives at most one grant per logical day.
- `IX_LearningDayGrants_DayOrdinal_SlotOrdinal` (UNIQUE on `DayOrdinal, SlotOrdinal`): ensures slot ordinals are unique within a logical day.

### Schema-12 activation behavior

| Source version | Behavior |
| --- | --- |
| Fresh / empty database | Initializes directly to a validated schema 12. |
| 11 | Migrates to schema 12 (creates `LearningDayState` and `LearningDayGrants` tables and unique indexes, sets `PRAGMA user_version = 12`). |
| 12 (valid) | Validation only. The database is inspected and never mutated. |
| 12 (malformed) | Fails closed. Nothing is repaired and nothing is written. |
| Greater than 12 | Rejected with `DatabaseSchemaCompatibilityException`. |

### Installation-local boundary and archive compatibility

- **Archive format:** Remains V2. No V3 format is introduced.
- **Export/Import:** `LearningDayState` and `LearningDayGrants` are never serialized into portable archives or backups. Restore into empty installations or merge into populated installations initializes fresh local learning-day state upon first learning access.
- **Merge isolation:** Populated-target merge operates entirely independent of target learning-day state and grants.

---

## Schema-13 Production Contract (KF-FSRS-003)

Schema 13 is the clean production target on current `master`, delivered by merged `KF-FSRS-003`. A genuinely empty database bootstraps directly to Schema 13. Normal startup does not migrate Schema 1–12 databases: those databases, malformed current databases, future databases, and non-empty unversioned databases fail closed without reset, deletion, repair, or mutation. Startup enables `PRAGMA foreign_keys = ON` before inspecting the version and validates an already-current database before exposing services.

### FSRS factual state and replay

- `FsrsCardStates` is the authoritative FSRS-6 scheduling state for each `LearningCard`; `FsrsReviewHistoryEntries` is the append-only factual FSRS review history.
- A factual event has its implemented `StableId` identity. `SequenceNumber` is the gapless, one-based causal and replay order within its card. Equal `ReviewedAtUtc` timestamps are legal and do not collapse events.
- The persisted state must agree exactly with replay of factual history. Binary64 FSRS values are compared by their exact IEEE-754 representation; an approximate tolerance is not a validity definition.
- `FsrsReviewPersistenceCoordinator` commits the resulting FSRS state and its factual event in one transaction. A failure rolls both back, so no partial factual/projected success is reported.
- `LearningReviews` remains a separate compatibility interaction and answer-attribution stream. Its rows are not FSRS facts, and neither FSRS `StableId` nor `SequenceNumber` identifies a `LearningReview`.

### Learning controls and eligibility

- `WordLearningControls` owns word-level AlreadyKnown decisions; `SenseLearningControls` owns sense-level StopLearning decisions. The owners are distinct, and absence of the respective row means no active clean control.
- Control timestamps require canonical UTC validation. Controls govern Schema-13 runtime eligibility while preserving factual history and FSRS state.
- This persistence contract does not create or promise a user-interface reversal, stop, or resume workflow.

### Archive V3 integrity boundary

Archive format version remains 3. Schema-13 FSRS history and state must meet exact binary64 replay/state integrity, and inconsistent state fails closed. Applicable V3 exports require `learning-review-causal-order-v1`; its ordered interaction history is distinct from FSRS factual history. Unmarked or legacy interaction history whose causal order cannot be proven fails closed before Schema-13 mutation. Feature-marked populated imports preserve occurrence multiplicity and use the ordered-prefix causal merge contract in [backup-merge-v1-design.md](architecture/backup-merge-v1-design.md); divergent histories fail closed before interaction or FSRS mutation.

## Historical Schema-13 Persistence and Migration Foundation (KF-PERSIST-013-001)

This section records the pre-cutover foundation. Its statements about dormant production activation apply only to that historical package and are superseded for current `master` by the Schema-13 production contract above.

### 1. Activation and Dormancy Boundary

- **Source Foundation:** Schema-13 table definitions, shape validation, repositories, atomic persistence coordinator, and the `Schema13DormantMigration` engine are implemented in source on `feature/schema13-clean-persistence-v1`.
- **Production Schema Unchanged:** `DatabaseSchema.CurrentVersion` remains **12** on `master`. Normal application startup through `DatabaseSchema.InitializeAsync` initializes or migrates databases up to Schema 12 only and does **not** call `Schema13DormantMigration.ApplyAsync`. A healthy initialized production database reports `PRAGMA user_version = 12`.
- **Explicit Migration Scope:** `Schema13DormantMigration.ApplyAsync` is an explicit, callable migration method that migrates a specified Schema-12 SQLite connection to Schema 13 in isolation.
- **Production Activation Ownership:** Production activation (incrementing `DatabaseSchema.CurrentVersion` to 13, updating `InitializeAsync`, and wiring DI runtime services) is deferred to `KF-FSRS-003`.

### 2. Physical Target Tables, Indexes, and Constraints

Schema 13 introduces four physical tables defined in `KnownFirst.Data.Migrations.Schema13.Schema13Ddl`:

#### A. `FsrsCardStates`

One-to-one scheduling-state table for `LearningCards` representing the current factual `Fsrs6Card` state.

```sql
CREATE TABLE FsrsCardStates (
    CardId INTEGER PRIMARY KEY,
    State INTEGER NOT NULL,
    Stability REAL,
    Difficulty REAL,
    LastReviewedAtUtc TEXT,
    StepIndex INTEGER,
    DueAtUtc TEXT,
    FOREIGN KEY (CardId) REFERENCES LearningCards(Id) ON DELETE CASCADE,
    CHECK (State IN (0, 1, 2, 3)),
    CHECK (
        (Stability IS NULL OR Stability >= 0.001)
        AND (Difficulty IS NULL OR (Difficulty >= 1.0 AND Difficulty <= 10.0))
    ),
    CHECK (
        (State = 0 AND Stability IS NULL AND Difficulty IS NULL AND LastReviewedAtUtc IS NULL AND StepIndex IS NULL)
        OR (State = 1 AND Stability IS NOT NULL AND Difficulty IS NOT NULL AND LastReviewedAtUtc IS NOT NULL AND StepIndex = 0)
        OR (State = 2 AND Stability IS NOT NULL AND Difficulty IS NOT NULL AND LastReviewedAtUtc IS NOT NULL AND StepIndex IS NULL)
        OR (State = 3 AND Stability IS NOT NULL AND Difficulty IS NOT NULL AND LastReviewedAtUtc IS NOT NULL AND StepIndex = 0)
    )
);
```

- **Index:** `IX_FsrsCardStates_State_DueAtUtc` ON `FsrsCardStates (State, DueAtUtc)` for efficient due-card and state querying.

#### B. `FsrsReviewHistoryEntries`

Append-only factual review log recording every historical review event.

```sql
CREATE TABLE FsrsReviewHistoryEntries (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    StableId TEXT NOT NULL,
    CardId INTEGER NOT NULL,
    SequenceNumber INTEGER NOT NULL,
    Rating INTEGER NOT NULL,
    ReviewedAtUtc TEXT NOT NULL,
    FOREIGN KEY (CardId) REFERENCES LearningCards(Id) ON DELETE CASCADE,
    CHECK (LENGTH(TRIM(StableId)) > 0),
    CHECK (SequenceNumber > 0),
    CHECK (Rating IN (0, 1, 2, 3))
);
```

- **Indexes:**
  - `IX_FsrsReviewHistoryEntries_StableId` (UNIQUE ON `StableId`): enforces unique global event identity.
  - `IX_FsrsReviewHistoryEntries_Card_Sequence` (UNIQUE ON `CardId, SequenceNumber`): enforces deterministic, gapless 1-based causal review order per card.
  - `IX_FsrsReviewHistoryEntries_Card_Replay` (INDEX ON `CardId, ReviewedAtUtc, SequenceNumber`): optimizes chronological event replay.

#### C. `WordLearningControls`

Physical persistence for reversible Word-level learning decisions (`AlreadyKnown`).

```sql
CREATE TABLE WordLearningControls (
    WordId INTEGER PRIMARY KEY,
    DecidedAtUtc TEXT NOT NULL,
    FOREIGN KEY (WordId) REFERENCES Words(Id) ON DELETE CASCADE,
    CHECK (LENGTH(TRIM(DecidedAtUtc)) > 0)
);
```

#### D. `SenseLearningControls`

Physical persistence for reversible Sense-level learning decisions (`StopLearning`).

```sql
CREATE TABLE SenseLearningControls (
    SenseId INTEGER PRIMARY KEY,
    DecidedAtUtc TEXT NOT NULL,
    FOREIGN KEY (SenseId) REFERENCES Senses(Id) ON DELETE CASCADE,
    CHECK (LENGTH(TRIM(DecidedAtUtc)) > 0)
);
```

- **Shape Validation:** `Schema13ShapeValidator.IsValidDatabase` strictly validates the presence, column nullabilities, affinities, foreign keys, CHECK constraints, and exact index configurations before the database can be used.

### 3. Semantic Separation and Learning Control Invariants

- **`WordLearningControl` Semantics:**
  - Represents the reversible user decision that a word was already known before KnownFirst taught it.
  - Absence of a row in `WordLearningControls` corresponds to `WordLearningControl.Default` (not marked already known).
  - Setting `WordLearningControl.Default` deletes the row from `WordLearningControls`.
  - Marking a word already known persists an explicit row with a strict UTC timestamp (`DecidedAtUtc`).
  - No dual-write or synchronization with `Words.Status` occurs; `Words.Status` remains untouched.
  - No semantic graph data (`Words`, `Senses`, `Meanings`, `LearningCards`) is deleted.
- **`SenseLearningControl` Semantics:**
  - Represents the reversible user decision to halt learning on an individual Sense.
  - Absence of a row corresponds to `SenseLearningControl.Default` (learning active).
  - Setting `SenseLearningControl.Default` deletes the row from `SenseLearningControls`.
  - No dual-write to `Sense.Status` or legacy scheduler columns occurs.
  - Schema-12 forward migration creates **zero** `SenseLearningControls` because Schema 12 contains no equivalent user decision.
- **FSRS State vs. Factual History Separation:**
  - `FsrsCardStates` is the target factual scheduling state for one `Fsrs6Card` per `LearningCard`.
  - `FsrsReviewHistoryEntries` is the append-only factual log of past review events.
  - State and history remain completely separate; no fallback or synchronization to legacy `LearningCards` scheduler columns (`EaseFactor`, `IntervalDays`, `Mastered`, `Retired`, `Suspended`).
- **Identity and Ordering Roles:**
  - `StableId`: unique event identity across installations.
  - `SequenceNumber`: strictly increasing 1-based causal order per card.
  - `ReviewedAtUtc`: factual timestamp of review. Equal timestamps and equal timestamp/rating multiplicity are valid and preserved deterministically.

### 4. Atomic Runtime Persistence Foundation (`FsrsReviewPersistenceCoordinator`)

- **Atomic Resulting-State and History Write:** `FsrsReviewPersistenceCoordinator.PersistReviewAsync` persists a caller-computed resulting `Fsrs6Card` and appends the corresponding factual `Fsrs6ReviewEvent` in a single SQLite transaction.
- **Formula Isolation:** The persistence coordinator does **not** duplicate or recompute FSRS mathematical scheduling formulas.
- **Transactional Rollback:** State update and history append roll back together on any error; partial persistence is impossible. No legacy dual-write is performed.

### 5. Migration Engine (`Schema13DormantMigration`)

- **Source and Target Versions:** `SourceVersion = 12`, `TargetVersion = 13`.
- **Precondition & Version Guards:**
  - `sourceVersion > TargetVersion` (source $> 13$): fails closed with `Schema13MigrationException.FutureVersion`.
  - `sourceVersion == TargetVersion` (source $= 13$): runs `ValidateAlreadyApplied` (`Schema13ShapeValidator` and `Schema13MigrationIntegrityValidator`) without mutating data and returns `AlreadyApplied`.
  - `sourceVersion != SourceVersion` (source $\ne 12$): fails closed with `Schema13MigrationException.UnsupportedSourceVersion`.
- **Source Shape Verification:** Verifies source Schema-12 shape via `Schema12ShapeValidator` before creating target structures.
- **Partial-Target Protection:** Checks for pre-existing Schema-13 tables or indexes on a version-12 database; any pre-existing target artifact throws an `InvariantViolation` fail-closed error.
- **Single Transaction:** DDL execution, bootstrap data materialization, shape validation, integrity validation, and `PRAGMA user_version = 13` run within a single `RunInTransactionAsync` block. Any error rolls back all target changes.

### 6. Historical Transformation Semantics (`Schema13LearningBootstrap`)

- **1:1 Review History Preservation:** Every source `LearningReviewEntity` row maps 1:1 to an `FsrsReviewHistoryEntries` row.
- **Deterministic Ordering:** Source reviews are ordered deterministically by `ReviewedAtUtc` ascending with legacy `Id` ascending as tie-breaker.
- **Rating Domain:** Mapped rating is `ReviewRating` ($0 \dots 3$).
- **Deterministic Migration StableId:** `Schema13HistoricalReviewStableIdPolicy` computes a SHA-256 stable identity under domain `KnownFirst.Identity.FsrsReviewHistoryEntry.MigrationBootstrap.v1` combining Sense identifier, `CardDirection`, `ReviewedAtUtc`, `Rating`, and a 0-based multiplicity ordinal for duplicate timestamps. No random GUIDs are used in migration.
- **FSRS Target State Derivation via Replay:** Target `Fsrs6Card` state is derived exclusively by streaming the mapped review events through `Fsrs6Replayer`. Legacy `EaseFactor` and `IntervalDays` never synthesize FSRS parameters. Replay-derived `DueAtUtc` is preserved without legacy override.
- **Unreviewed & Historyless Progress Cards:**
  - Genuinely unreviewed cards (0 review history rows, New state) map to `Fsrs6Card.New`.
  - Progressed cards with missing review history fail closed with `Schema13MigrationException.InvariantViolation` instead of silently resetting to New.
- **Word Learning Controls Migration:** Words with `Status = WordStatus.Known` produce a `WordLearningControl` row with `DecidedAtUtc` taken from the legacy `Words.UpdatedAt` timestamp (or `CreatedAtUtc` fallback).
- **No Sense Learning Controls:** Schema-12 migration creates zero `SenseLearningControls`.
- **Semantic Graph Preservation:** Previously deleted semantic graph data is not recreated.

### 7. Source Preservation

- Existing Schema-12 tables (`Words`, `Senses`, `Meanings`, `LearningCards`, `LearningReviews`, `LearningSessions`, `LearningSessionCards`, `LearningDayState`, `LearningDayGrants`, `DerivedTermEvidenceEntries`, `Documents`, etc.) remain logically unchanged and fully intact after migration.
- No columns are dropped and no legacy data is destructively transformed during Schema 13 migration.

### 8. Foreign-Key Boundary

- Schema-13 DDL defines physical SQLite foreign keys referencing `LearningCards(Id)`, `Words(Id)`, and `Senses(Id)` with `ON DELETE CASCADE`.
- Schema-13 shape and integrity tests verify that target data is valid under explicit `PRAGMA foreign_keys = ON`.
- Production `KnownFirstDatabase` does **not** yet globally enable `PRAGMA foreign_keys = ON`. Global production foreign-key enablement prerequisite belongs to `KF-FSRS-003`.

### 9. Backup and Restore Boundary

- **Archive Format Remains V2:** Portable `.kfarchive` export and import remain format V2.
- **Archive V3 Prerequisite:** Schema-13 structures (`FsrsCardStates`, `FsrsReviewHistoryEntries`, `WordLearningControls`, `SenseLearningControls`) are not exported to V2 archives. Export and cross-installation transport of Schema-13 state belong to `KF-BACKUP-006` (Archive V3).
- **No Incompatible Transport:** Schema-13 data is never coerced or truncated into Schema-12/V2 archives.
# Current Schema-13 status

Schema 13 is current production source/runtime truth on `master`. Fresh empty production databases bootstrap directly to Schema 13. Existing Schema 1–12 databases fail closed in the current production startup path; this document does not authorize or describe an automatic production migration. `WordLearningControls` and `SenseLearningControls` are persisted Schema-13 data with repository, backup/restore, integrity-validation, and test coverage; user-facing workflows remain backlog work.
