# KnownFirst Current Work

## Last updated

2026-08-02

## Repository

- Repository: https://github.com/Tachiguro/KnownFirst.git
- Only local project folder: `C:\Dev\KnownFirst` (see [ADR-0007](decisions/ADR-0007-single-canonical-working-directory.md))
- Active rule: use the single folder; create no worktree without explicit user approval.
- Only one writing agent may operate at a time.

## Verified master baseline

- Master commit: `d53ffe3d92e249e8bc2f191d1b5cc8b9e81681dc` (PR #43 merged, Slice 7 Schema-8 MergePreflight)
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

## Active local implementation package

- Branch: `feature/meaning-slice8-core-merge-writer`
- Purpose: **KF-MEANING-001 Slice 8 — transactional Schema-8 populated-target merge writer and Import routing.**
- Current phase: implementation and validation are complete; awaiting documentation and PR creation.
- Build: 0 errors.
- Complete Slice-8 validation: **1593 passed, 0 failed, 0 skipped** (3 m 5 s).
  - 108 writer-focused tests pass.
  - 42 scheduler-replay tests pass.
  - 177 Import-integration tests pass.

### Implemented Slice-8 behavior

- `PortableMergeWriter` — transactional Schema-8 populated-target merge writer that validates the incoming plan against the current target state, rejects stale or non-executable plans, and atomically commits the merge or rolls back completely on any error.
- Stable identity resolution using explicit source-local-ID-to-target-ID maps; source integer IDs are never target identities.
- Existing domain entities are reused; missing entities and preserved variants are inserted; defined enrichment policies are applied.
- Sense-addressed meanings, contexts, answer variants, assignments, progress, cards, reviews, sessions, queues, and review/preparation workflows are merged and preserved.
- Card scheduling is replayed through the existing scheduler in deterministic order (ReviewedAtUtc, then review fingerprint ordinally); replay changes only derived scheduling fields and does not repoint Sense, PreferredMeaning, or Direction.
- Multiple Senses for one Word remain independent; each Sense is merged separately.
- Failure and cancellation roll back the complete merge.
- Reimport converges without duplicate domain entities or deduplicated history; merged review history becomes authoritative.
- Import routing: empty targets use restore-into-empty; populated Schema-8 targets use validation → preflight → validated safety copy → transactional writer.
- Archive-v1 upgrades in memory for Schema 8; archive-v2 is supported natively; archive-v2 into Schema 7 remains rejected.
- Fully duplicate imports return successful no-change without safety copy or writer invocation.
- Non-seekable source streams are supported.
- Stable errors are preserved; `PortableImportResult` exposes backward-compatible disposition and aggregate summary.

## Current blocker or pending validation

- No implementation or validation blocker remains; Slice-8 PR creation and documentation finalization are pending.
- Slice 9 (Import UI, localized preview/result handling, and final end-to-end release-readiness validation) remains unimplemented and out of scope for this slice.
- No Beta 13, packaging, device, or store task is active.

## Exact next action

- Manual merge of the Slice-8 pull request on GitHub.
- **KF-MEANING-001 Slice 9 — Import UI, localized preview/result handling, and final end-to-end release-readiness validation.**

## Concise new-chat handoff

- Master baseline is `d53ffe3d92e249e8bc2f191d1b5cc8b9e81681dc` (PR #43 merged); `DatabaseSchema.CurrentVersion` is 8 and Schema 8 is active on master.
- Beta 12 / build 12 was distributed via Google Play Internal Testing and user-tested.
- Meaning Slices 1–7 are merged on master. Slice 8 (transactional Schema-8 populated-target merge writer) is implemented and validated on `feature/meaning-slice8-core-merge-writer`.
- Populated-target import now validates the merge plan, creates a validated safety copy of the target, and applies the merge transactionally; stale or non-executable plans are rejected; reimport converges without duplicates.
- Card scheduling is replayed deterministically through the existing scheduler; replay preserves Sense, PreferredMeaning, and Direction.
- Slice 8 is validated with 1593 passed, 0 failed, 0 skipped and has an open pull request awaiting manual merge.
- Slice 9 (Import UI, localized handling, release validation) remains unimplemented and out of scope.
- Exact next action: manual merge of the Slice-8 pull request.
- No Beta 13, packaging, device, or store task is active.
