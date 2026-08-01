# KnownFirst Current Work

## Last updated

2026-08-01

## Repository

- Repository: https://github.com/Tachiguro/KnownFirst.git
- Only local project folder: `C:\Dev\KnownFirst` (see [ADR-0007](decisions/ADR-0007-single-canonical-working-directory.md))
- Active rule: use the single folder; create no worktree without explicit user approval.
- Only one writing agent may operate at a time.

## Verified master baseline

- Master commit: `e1724651dd7d4d3ed427b84a96da3d909d0c72ed` (PR #41 merged)
- Source-controlled application identity: `1.0.0-beta.12` (build 12)
- Confirmed distribution: `1.0.0-beta.12` / build 12 was distributed via Google Play Internal Testing and user-tested.
- Active database schema on master: SQLite `PRAGMA user_version` 7
- Schema 8 status on master: dormant (Slices 1–5 merged as dual-schema foundations)
- Supported platforms: Android (Google Play Internal Testing) and Windows development/verification. iOS and Mac Catalyst remain removed.
- Solution: `KnownFirst.slnx`

## Completed packages on master

- **Windows GUI StartupSmoke launcher package (PR #35 merged)**
- **New-Chat Bootstrap Protocol package (PR #36 merged)**
- **Google Play Packaging Safeguards package (PR #37 merged)**
- **Reconcile Beta 12 and Next Merge Work (PR #38 merged)**
- **Meaning Slice 4 answer assignments and progress replay (PR #40 merged; full suite 1347 passed, 0 failed, 0 skipped)**
- **Meaning Slice 5 Sense-addressed cards and queue behavior (PR #41 merged; full suite 1364 passed, 0 failed, 0 skipped)**

## Active local implementation package

- Branch: `feature/meaning-slice6-schema8-activation`
- Purpose: **KF-MEANING-001 Slice 6 — Schema-8 activation and the first real user-data migration.**
- Current phase: implementation, the review corrections, and full validation are complete on the feature branch; the pull request is open and awaits manual merge.
- Focused Slice-6 validation: 466 passed, 0 failed, 0 skipped.
- Complete Slice-6 validation: **1542 passed, 0 failed, 0 skipped** (3 m 15 s).

### Implemented Slice-6 behavior

- `DatabaseSchema.CurrentVersion` is **8**; Schema 8 is active for real application databases.
- A fresh database initializes directly to a validated Schema 8.
- Supported versions 0–6 first reach the Schema-7 baseline boundary and are then migrated to Schema 8 in the same initialization.
- A version-7 database is migrated to Schema 8 on its next initialization.
- A valid version-8 database is validation-only on reopen: it is inspected and never mutated.
- A malformed Schema-8 database and any database whose version is greater than 8 fail closed; nothing is repaired and nothing is written.
- The migration runs inside one real SQLite transaction and is rollback-safe, cancellation-safe, and retryable: a failed attempt leaves a byte-for-byte unchanged Schema-7 database that can be retried.
- Structural validation covers required tables, required columns, declared column nullability and primary-key semantics, absence of legacy artifacts, index definitions (columns, order, uniqueness, partial predicates), enum domains, ownership relationships, queue/review answer-variant targets, and persisted relationships.
- Legacy enum backfills bring pre-Schema-7 rows to deterministic supported values before activation.
- `DashboardService` and `TextReviewService` use Schema-8 semantics through validated schema-capability resolution.
- Schema-8 archive export and merge safety copies use archive format **v2**.
- Archive format **v1** remains readable and can still restore into an empty Schema-8 target.
- Import into a populated target remains refused.
- `MergePreflightService` intentionally fails closed on a Schema-8 target with `merge-preflight-schema8-adaptation-required`, pending the Slice-7 adaptation.

### Test-infrastructure corrections made during Slice-6 validation

- **Future-version exception contract.** `TemporaryKnownFirstDatabase.InitializeAsync` builds its Schema-7 fixture once but re-applies the production future-version gate (`DatabaseSchema.CurrentVersion` + `DatabaseSchemaCompatibilityException`) on every gated operation, so a database written to a future version is still refused before any backup capture.
- **Process-wide SQLite pool drains removed.** `SQLiteAsyncConnection.ResetPool()` was removed from all 16 ordinary fixture lifecycle sites across seven test files. Under `[assembly: Parallelize(Scope = ExecutionScope.MethodLevel)]` a global drain closes native handles that concurrently running tests still hold, faulting the test host inside `sqlite3_changes` instead of failing cleanly. Teardown is now scoped: `CloseAsync` releases exactly the owning connection's pooled entry, then the fixture's own database, `-wal`, and `-shm` files are deleted through the shared `TemporaryDatabaseFiles` helper. Method-level parallelization is retained.
- **Document-backed ContextSnapshot fixtures.** `LearningServiceSchema8ViewAndContinuationTests` now seeds the exact source `Documents` row every ContextSnapshot references, so Schema-8 ownership validation holds without weakening the fixture.
- **Capability gate ordering.** `Schema8AnswerAssignmentServiceTests.Validation_DuplicatePreferred_FailsClosed` constructs its duplicate by dropping a required unique index, which is itself a physical-shape violation; the test now pins `LearningSchemaCapabilityException` as the outer fail-closed gate and proves the duplicate exists underneath it with zero mutation.
- **Legacy cache purge.** `WiktionaryProviderTests.Cache_InitializationInvalidatesLegacyIncompleteKeys` now runs real `DatabaseSchema.InitializeAsync` so the production startup cleanup executes, and asserts the legacy key is gone while a valid `v2|` key survives.

## Current blocker or pending validation

- No implementation or validation blocker remains; manual merge of the Slice-6 pull request is pending.
- Populated-target archive import is not implemented; current import refuses populated installations.
- The populated-target merge writer and Import routing are not implemented.
- No current packaging or release task is active.

## Exact next action

- User-manual merge of the Slice-6 pull request on GitHub, then a separately authorized post-merge synchronization.
- After merge: **KF-MEANING-001 Slice 7 — Schema-8 MergePreflight adaptation.**
- The merge writer (Slice 8) must not be implemented before Slice 7 is complete.

## Concise new-chat handoff

- Master baseline is `e1724651dd7d4d3ed427b84a96da3d909d0c72ed` (PR #41 merged).
- Beta 12 / build 12 was distributed via Google Play Internal Testing and user-tested.
- Meaning Slices 1–5 are merged; Slice 6 activates Schema 8 on the feature branch `feature/meaning-slice6-schema8-activation`.
- On the Slice-6 branch `DatabaseSchema.CurrentVersion` is 8 and Schema 8 is active; on master it is still 7.
- MergePreflight rejects Schema-8 targets until Slice 7; populated-target archive merge remains unimplemented.
- Slice 6 is validated with 1542 passed, 0 failed, 0 skipped and has an open pull request awaiting manual merge.
- Exact next action: manual merge of the Slice-6 pull request, then Slice 7.
- No Beta 13 or active packaging task is in progress.
