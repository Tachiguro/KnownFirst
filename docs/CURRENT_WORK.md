# KnownFirst Current Work

## Last updated

2026-08-02

## Repository

- Repository: https://github.com/Tachiguro/KnownFirst.git
- Only local project folder: `C:\Dev\KnownFirst` (see [ADR-0007](decisions/ADR-0007-single-canonical-working-directory.md))
- Active rule: use the single folder; create no worktree without explicit user approval.
- Only one writing agent may operate at a time.

## Verified master baseline

- Master commit: `cf1b0995415dc858357f0a5c9e90e7c0aefb327c` (PR #44 merged, Slice 8 transactional Schema-8 merge writer)
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
- **Meaning Slice 7 Schema-8 MergePreflight adaptation (PR #43 merged; full suite 1551 passed, 0 failed, 0 skipped)**
- **Meaning Slice 8 transactional Schema-8 merge writer and Import routing (PR #44 merged; full suite 1593 passed, 0 failed, 0 skipped)**

## Active local implementation package

- Branch: `feature/meaning-slice9-import-preview-ui`
- Purpose: **KF-MEANING-001 Slice 9 — portable import preview UI, localized handling, and end-to-end convergence validation.**
- Current phase: implementation and validation are complete; awaiting manual PR merge to master.
- Build: 0 errors.
- Complete full-suite validation: **1626 passed, 0 failed, 0 skipped** (3 m 11 s).
  - 160 preview/UI focused tests pass.
  - 267 end-to-end convergence focused tests pass.
  - 91 LearningSession identity correction focused tests pass.

### Implemented Slice-9 behavior

- **Import preview UI** — read-only preview before confirmation distinguishes restore (empty target), merge (populated Schema-8 target), and no-change (all portable data already present) cases.
- **Preview safety** — no database mutation, safety copy, or writer invocation during preview; supports non-seekable caller streams.
- **Confirmation** — distinct labels for restore or merge; no-change presents success without a mutating action; re-validates and re-evaluates the operation independently on confirmation.
- **Import workflow** — one unified Import data operation; no separate Merge button or separate merge workflow.
- **Merge preview and results** — expose aggregate inserted, enriched, preserved-variant, and skipped counts; explain that local data is preserved and a validated private safety copy is created before mutation.
- **Disposition classification** — RestoredIntoEmpty, MergeApplied, MergeNoChange; workflow-change notifications occur only for RestoredIntoEmpty and MergeApplied; no notification for no-change.
- **Localization** — complete EN/DE/RU coverage for preview, result, and failure handling.
- **Corrected LearningSession identity** — distinct real sessions using the same card set no longer collapse; identity now includes StartedAtUtc, CompletedAtUtc, ordered queue digest, and Rating per item; planner and target-index share the same implementation; reimport converges without duplicates.
- **End-to-end convergence validation** — real automated tests exercise archive creation → validation → preview → preflight → validated safety copy → transactional writer → deterministic scheduler replay → result summary → repeated-import no-change; bidirectional divergent Schema-8 databases converge semantically.
- **Archive-v1 upgrade and convergence** — Schema-8 populated-target Import upgrades archive-v1 in memory and converges on reimport.
- **Safety-copy validation** — safety copies are reopened and validated from final paths; represent the pre-merge target state; remain available after later writer failure.
- **Rollback-after-safety-copy validation** — real injected writer failure after safety-copy success rolls back all target mutations, retains the valid safety copy, leaves no staging artifact, exposes no raw exception text.
- **Corrupt-archive fail-closed validation** — corrupt archives fail closed before safety-copy creation or mutation.

## Current blocker or pending validation

- No implementation or validation blocker remains; Slice-9 PR creation and manual merge to master are pending.
- No Beta 13, packaging, device, or store task is active.

## Exact next action

- Manual merge of the Slice-9 pull request on GitHub.
- Completion of the full portable archive export/import implementation milestone.

## Concise new-chat handoff

- Master baseline is `cf1b0995415dc858357f0a5c9e90e7c0aefb327c` (PR #44 merged); `DatabaseSchema.CurrentVersion` is 8 and Schema 8 is active on master.
- Beta 12 / build 12 was distributed via Google Play Internal Testing and user-tested.
- Slices 1–8 are merged on master. Slice 9 (portable import preview UI, localized handling, and full end-to-end convergence validation) is complete on the feature branch and awaits manual merge of PR #45.
- Import workflow: single unified operation with read-only preview distinguishing restore, merge, and no-change; confirmation revalidates independently; localized EN/DE/RU; mutations notify only on RestoredIntoEmpty or MergeApplied.
- Populated-target import validates the merge plan, creates a validated safety copy of the target, and applies the merge transactionally; stale or non-executable plans are rejected; reimport converges without duplicates.
- LearningSession identity now includes timestamps, queue order, and ratings; distinct sessions using the same card set remain separate.
- Card scheduling is replayed deterministically through the existing scheduler; replay preserves Sense, PreferredMeaning, and Direction.
- Real end-to-end tests exercise archive creation through reimport no-change; bidirectional divergent databases converge semantically; safety copies are validated.
- Complete full suite: 1626 passed, 0 failed, 0 skipped.
- Slice 9 is validated and has an open pull request awaiting manual merge.
- Exact next action: manual merge of the Slice-9 pull request.
- No Beta 13, packaging, device, or store task is active.
