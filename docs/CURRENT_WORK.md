# KnownFirst Current Work

## Last updated

2026-08-02

## Repository

- Repository: https://github.com/Tachiguro/KnownFirst.git
- Only local project folder: `C:\Dev\KnownFirst` (see [ADR-0007](decisions/ADR-0007-single-canonical-working-directory.md))
- Active rule: use the single folder; create no worktree without explicit user approval.
- Only one writing agent may operate at a time.

## Verified master baseline

- Master commit: `3debd7a1b1d300ea08586b1b6d8570db72cf6138` (PR #42 merged, Slice 6 Schema-8 activation)
- Source-controlled application identity: `1.0.0-beta.12` (build 12)
- Confirmed distribution: `1.0.0-beta.12` / build 12 was distributed via Google Play Internal Testing and user-tested.
- Active database schema on master: SQLite `PRAGMA user_version` 8 (Slice 6 activation merged)
- Schema 8 status on master: active
- Supported platforms: Android (Google Play Internal Testing) and Windows development/verification. iOS and Mac Catalyst remain removed.
- Solution: `KnownFirst.slnx`

## Completed packages on master

- **Windows GUI StartupSmoke launcher package (PR #35 merged)**
- **New-Chat Bootstrap Protocol package (PR #36 merged)**
- **Google Play Packaging Safeguards package (PR #37 merged)**
- **Reconcile Beta 12 and Next Merge Work (PR #38 merged)**
- **Meaning Slice 4 answer assignments and progress replay (PR #40 merged; full suite 1347 passed, 0 failed, 0 skipped)**
- **Meaning Slice 5 Sense-addressed cards and queue behavior (PR #41 merged; full suite 1364 passed, 0 failed, 0 skipped)**
- **Meaning Slice 6 Schema-8 activation and first real user-data migration (PR #42 merged; full suite 1542 passed, 0 failed, 0 skipped)**

## Active local implementation package

- Branch: `feature/meaning-slice7-schema8-merge-preflight`
- Purpose: **KF-MEANING-001 Slice 7 — Schema-8 MergePreflight adaptation.**
- Current phase: implementation and validation are complete; checkpoint commit `bea01a75ae6da2e6f7a7ea269dae0e1c7cbe3675` is pushed and a pull request is open awaiting manual merge.
- Build: 0 errors.
- Focused Slice-7 validation (MergePreflightServiceTests + safety suite): 135 passed, 0 failed, 0 skipped.
- Complete Slice-7 validation: **1551 passed, 0 failed, 0 skipped** (2 m 53 s).

### Implemented Slice-7 behavior

- `MergePreflightService`/`MergePreflightPlannerV2` plan merges for active Schema-8 target databases against archive-format-v2 (Schema-8) sources.
- Archive-format-v1 sources remain supported into a Schema-8 target through the existing in-memory upgrade path (`BackupArchiveV1UpgradePolicy`).
- Preflight is deterministic and strictly read-only: no target mutation, no safety copy, no writer invocation, no persistent import artifact.
- Multiple Senses of the same Word are planned independently (identity now includes the persisted `Sense.TopicOrDomain` field).
- Sense-addressed meanings, answer variants, `SenseAnswerVariantAssignment`s, `AnswerVariantProgress`, learning cards, reviews, queue items, and vocabulary/preparation workflows are all covered by the plan.
- Import into a populated target and the merge writer remain unimplemented; this slice is preflight planning only.

### Test-infrastructure corrections made during Slice-6 validation

- **Future-version exception contract.** `TemporaryKnownFirstDatabase.InitializeAsync` builds its Schema-7 fixture once but re-applies the production future-version gate (`DatabaseSchema.CurrentVersion` + `DatabaseSchemaCompatibilityException`) on every gated operation, so a database written to a future version is still refused before any backup capture.
- **Process-wide SQLite pool drains removed.** `SQLiteAsyncConnection.ResetPool()` was removed from all 16 ordinary fixture lifecycle sites across seven test files. Under `[assembly: Parallelize(Scope = ExecutionScope.MethodLevel)]` a global drain closes native handles that concurrently running tests still hold, faulting the test host inside `sqlite3_changes` instead of failing cleanly. Teardown is now scoped: `CloseAsync` releases exactly the owning connection's pooled entry, then the fixture's own database, `-wal`, and `-shm` files are deleted through the shared `TemporaryDatabaseFiles` helper. Method-level parallelization is retained.
- **Document-backed ContextSnapshot fixtures.** `LearningServiceSchema8ViewAndContinuationTests` now seeds the exact source `Documents` row every ContextSnapshot references, so Schema-8 ownership validation holds without weakening the fixture.
- **Capability gate ordering.** `Schema8AnswerAssignmentServiceTests.Validation_DuplicatePreferred_FailsClosed` constructs its duplicate by dropping a required unique index, which is itself a physical-shape violation; the test now pins `LearningSchemaCapabilityException` as the outer fail-closed gate and proves the duplicate exists underneath it with zero mutation.
- **Legacy cache purge.** `WiktionaryProviderTests.Cache_InitializationInvalidatesLegacyIncompleteKeys` now runs real `DatabaseSchema.InitializeAsync` so the production startup cleanup executes, and asserts the legacy key is gone while a valid `v2|` key survives.

## Current blocker or pending validation

- No implementation or validation blocker remains; manual merge of the Slice-7 pull request is pending.
- Populated-target archive import is not implemented; current import refuses populated installations.
- The populated-target merge writer (Slice 8) and Import routing are not implemented.
- No current packaging or release task is active.

## Exact next action

- User-manual merge of the Slice-7 pull request on GitHub, then a separately authorized post-merge synchronization.
- After merge: **KF-MEANING-001 Slice 8 — populated-target merge writer and Import routing.**
- Slice 9 (Import UI and end-to-end convergence validation) follows Slice 8.

## Concise new-chat handoff

- Master baseline is `3debd7a1b1d300ea08586b1b6d8570db72cf6138` (PR #42 merged); `DatabaseSchema.CurrentVersion` is 8 and Schema 8 is active on master.
- Beta 12 / build 12 was distributed via Google Play Internal Testing and user-tested.
- Meaning Slices 1–6 are merged. Slice 7 (Schema-8 MergePreflight adaptation) is implemented and validated on `feature/meaning-slice7-schema8-merge-preflight`, checkpoint commit `bea01a75ae6da2e6f7a7ea269dae0e1c7cbe3675`.
- MergePreflight now supports Schema-8 targets against archive-v2 and archive-v1 (upgraded) sources, read-only and deterministic; populated-target archive merge (the writer) remains unimplemented.
- Slice 7 is validated with 1551 passed, 0 failed, 0 skipped and has an open pull request awaiting manual merge.
- Exact next action: manual merge of the Slice-7 pull request, then Slice 8.
- No Beta 13 or active packaging task is in progress.
