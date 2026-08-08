# KnownFirst Current Work

## Last updated

2026-08-08 (PR #77 merged and synchronized; KF-BACKUP-004 complete on `master`; post-merge documentation closure active)

## Repository

- Repository: https://github.com/Tachiguro/KnownFirst.git
- Only local project folder: `C:\Dev\KnownFirst` (see [ADR-0007](decisions/ADR-0007-single-canonical-working-directory.md))
- Active rule: use the single folder; create no worktree without explicit user approval.
- Only one writing agent may operate at a time.

## Verified product-state milestone

- Most recent product-relevant milestone commit: `14138ccdab1e9b09a12ded002ff198d9b7312fcf` (Milestone 14B, PR #73 merged). This is historical milestone evidence, not a claim about the literal current `master` HEAD; discover the exact current `master` HEAD dynamically per [docs/NEW_CHAT_BOOTSTRAP.md](NEW_CHAT_BOOTSTRAP.md).
- Source-controlled application identity: `1.0.0-beta.12` (build 12)
- Confirmed distribution: `1.0.0-beta.12` / build 12 was distributed via Google Play Internal Testing and user-tested (confirmed 2026-07-30). No newer Android build, AAB, Internal Testing release, installation, or user test has occurred since.
- Active database schema on master: SQLite `PRAGMA user_version` 9
- Supported platforms: Android (Google Play Internal Testing) and Windows development/verification. iOS and Mac Catalyst remain removed.
- Solution: `KnownFirst.slnx`

## Completed packages on master since the previous baseline

- **PR #49 — Documentation governance and release-readiness rules.**
- **PR #50 — Android portable export staging:** Strict validation before destination acquisition.
- **PR #51 — Schema-9 review-session history storage activation.**
- **PR #52 — Package A (Schema-9 completed-review convergence):** Schema-9 completed-review identity, planner, target-index parity, and characterization coverage.
- **PR #53 — D1 authoritative state and database truth reconciliation.**
- **PR #54 — D1 closure and D2 activation.**
- **PR #55 — D2 Agent Communication and Operation Governance.**
- **PR #56 — D2 closure and D3 activation.**
- **PR #57 — D3 Backup and Import Contracts.**
- **PR #58 — D3 closure and D4 activation.**
- **PR #59 — D4 Product, Workflow, and Release-Facing Documentation.**
- **PR #60 — D4 closure and D5 activation.**
- **PR #61 — D5 Testing and GUI Contract Reconciliation.**
- **PR #62 — D5 Historical Banners and Routing Corrections.**
- **PR #63 — D5 Mechanical Markdown Hygiene.**
- **PR #64 — D5 closure and Package B revalidation queued.**
- **PR #65 — Package B (Schema-9 completed-review writer evidence):** genuine Schema-9 writer evidence and a narrow deterministic `BackupModelMapperV2` `ReviewSession` ordering correction; merge commit `f560a6b7ff9109bbee6c46602a002ea8b591de49`.
- **PR #68 — Package C (Schema-9 completed-review convergence):** convergence hardening, cross-installation canonical ordering, and two-installation synchronization; merge commit `db47de3bf48b49b5258ce16acc6e3e543d96143c`.
- **PR #71 — Milestone 14A (unfinished support/report control removal):** the unfinished `Support KnownFirst` and `Report a bug` controls and their shared placeholder behavior removed from the production Settings source, with a focused source-contract absence test; merge commit `39609ffffb39c69238882172d153f4bb795ddab8`.
- **PR #73 — Milestone 14B (reopenable release-note history):** Settings → Help & Support link and new `/release-notes` route exposing the complete existing release-note catalog; merge commit `14138ccdab1e9b09a12ded002ff198d9b7312fcf`. `POST_MERGE_SYNC_ONLY` completed successfully.
- **PR #74 — Milestone 14B post-merge documentation closure:** reconciled `CURRENT_WORK.md`, `PROJECT_STATE.md`, and `ROADMAP.md` with the merged Milestone 14B product state; merge commit `27ebb9aed301dfce424e4c713a9e7d8aa56bf95b`. `POST_MERGE_SYNC_ONLY` completed successfully.
- **PR #75 — Standing Delegation Governance Reconciliation:** reconciled `AGENTS.md`, [AGENT_WORKFLOW.md](AGENT_WORKFLOW.md), [NEW_CHAT_BOOTSTRAP.md](NEW_CHAT_BOOTSTRAP.md), [PROMPT_AND_TASK_ROUTING.md](PROMPT_AND_TASK_ROUTING.md), and this file with the user's standing orchestration delegation for the routine `PLAN_ONLY`-through-`PR_ONLY` lifecycle; merge commit `666aa165b071886940ac7ce1b86de9ae2e11c73a`. `POST_MERGE_SYNC_ONLY` completed successfully.
- **PR #76 — `KF-BACKUP-003` Package D (Schema-9 portable workflow canonical ordering):** made `BackupModelMapperV2`'s v2 export ordering for completed `PreparationSessions`/`PreparationCandidates`, `LearningSessions`/`LearningSessionCards`, and `LearningReviews` total over emitted content instead of falling through to installation-local SQLite row order; merge commit `17d3f1a031b9f319041ff1034a227d17b1029c4f`. `POST_MERGE_SYNC_ONLY` completed successfully.
- **PR #77 — `KF-BACKUP-004` (Schema-9 LearningReview merge integrity):** positional action lookup keys (`lr#<archiveRowIndex>`), meaning-aware review-event identity incorporating stable nullable `TargetAnswerVariant`/`MatchedAnswerVariant` identities, and scheduler replay alignment; `LearningSessionId` deliberately excluded from event identity; merge commit `bec861fb8a054beb2804f1132b450da1e45dee90`. `POST_MERGE_SYNC_ONLY` completed successfully.

## Currently active package

**Milestone 14 product/source work is complete: Milestone 14B was manually merged via PR #73** (merge commit `14138ccdab1e9b09a12ded002ff198d9b7312fcf`). The Milestone 14B post-merge documentation closure was manually merged via PR #74 (merge commit `27ebb9aed301dfce424e4c713a9e7d8aa56bf95b`). The Standing Delegation Governance Reconciliation was manually merged via PR #75 (merge commit `666aa165b071886940ac7ce1b86de9ae2e11c73a`); `POST_MERGE_SYNC_ONLY` for PR #75 completed successfully, and local `master` is fast-forwarded to that commit.

**`KF-BACKUP-003` Package D — Schema-9 Portable Workflow Canonical Ordering is complete on `master`.** It was manually merged via PR #76 (merge commit `17d3f1a031b9f319041ff1034a227d17b1029c4f`); the final PR re-review approved it with `0 BLOCKER / 0 MAJOR / 0 MINOR / 0 NIT`, and `POST_MERGE_SYNC_ONLY` completed successfully. Its evidence remains: focused RED `56 passed / 4 failed / 0 skipped / 60 total` → GREEN `60 passed / 0 failed / 0 skipped / 60 total`; broader data-safety `TEST_ONLY` `376 passed / 0 failed / 0 skipped`; `ALL_AUTOMATED` `1786 passed / 0 failed / 0 skipped`. Package D changed only `Services/DataSafety/BackupModelMapperV2.cs` plus two test files, with no archive DTO, `.kfarchive` format version, schema, migration, merge-identity, import-routing, public error/status-code, GUI, build, package, or release change. See [architecture/backup-merge-v1-design.md](architecture/backup-merge-v1-design.md) §21.

**`KF-BACKUP-004` — Schema-9 LearningReview Merge Integrity is complete on `master`.** It was manually merged via PR #77 (merge commit `bec861fb8a054beb2804f1132b450da1e45dee90`); the final PR review approved it with `0 BLOCKER / 0 MAJOR / 0 MINOR / 0 NIT`, and `POST_MERGE_SYNC_ONLY` completed successfully. Its evidence remains: focused RED `114 passed / 7 failed / 0 skipped / 121 total` → GREEN `121 passed / 0 failed / 0 skipped / 121 total`; broader data-safety `TEST_ONLY` `348 passed / 0 failed / 0 skipped`; `ALL_AUTOMATED` `1795 passed / 0 failed / 0 skipped`. Scope was three production files (`Services/DataSafety/Merge/MergePreflightPlannerV2.cs`, `Services/DataSafety/Merge/MergeWriterExecutor.cs`, `Services/DataSafety/Merge/Schema9LearningReviewMergeIdentity.cs`) plus three test files (`KnownFirst.Tests/MergePreflightPlannerTests.cs`, `KnownFirst.Tests/MergeWriterServiceTests.cs`, `KnownFirst.Tests/PortableImportEndToEndConvergenceTests.cs`). No archive DTO, format version, database schema, migration, public error/status code, import-UI, or Package D mapper-ordering change. See [architecture/backup-merge-v1-design.md](architecture/backup-merge-v1-design.md) §22 and [BACKLOG.md](BACKLOG.md) `KF-BACKUP-004`.

**The currently active package is the `KF-BACKUP-004` Post-Merge Documentation Closure** on branch `docs/kf-backup-004-post-merge-closure-v1`, reconciling `CURRENT_WORK.md`, `PROJECT_STATE.md`, `ROADMAP.md`, `BACKLOG.md`, `DATABASE_CONTRACT.md`, and `docs/architecture/backup-merge-v1-design.md` with the merged state on `master`.
- This is a bounded six-file documentation-only package.
- Live Git and GitHub state governs its routine lifecycle (`REVIEW_ONLY` → `COMMIT_ONLY` → `PUSH_ONLY` → `PR_ONLY` → final `REVIEW_ONLY` → manual owner merge → `POST_MERGE_SYNC_ONLY`).
- After this documentation closure is merged and synchronized, no new engineering package is automatically started; the next active task is chosen from the remaining Priority-15 residuals (`LegacyReviewSummaries` ordering, mid-session review-event export policy, `Learning.Cards`/Sense `StableId` cross-installation ordering, legacy v1 planner synthesized label) or explicit user direction.

Milestone 14A history is unaffected: it passed final PR review, was manually merged via PR #71 (merge commit `39609ffffb39c69238882172d153f4bb795ddab8`), and `POST_MERGE_SYNC_ONLY` completed successfully. Package A/B/C history is likewise unaffected: Package A merged via PR #52; Package B merged via PR #65 (merge commit `f560a6b7ff9109bbee6c46602a002ea8b591de49`); Package C merged via PR #68 (merge commit `db47de3bf48b49b5258ce16acc6e3e543d96143c`), with final local automated evidence `1776 passed / 0 failed / 0 skipped`.

## Current blocker or pending validation

- None for Milestone 14B, its post-merge documentation closure, or the Standing Delegation Governance Reconciliation: all completed their full lifecycle.
- Milestone 14 as a whole remains complete on `master`.
- None for `KF-BACKUP-003` Package D: completed its full lifecycle and merged via PR #76 (`17d3f1a031b9f319041ff1034a227d17b1029c4f`).
- None for `KF-BACKUP-004`: completed its full lifecycle and merged via PR #77 (`bec861fb8a054beb2804f1132b450da1e45dee90`), with `POST_MERGE_SYNC_ONLY` completed successfully.
- Active documentation closure (`docs/kf-backup-004-post-merge-closure-v1`): pending standard review, commit, push, PR, manual merge, and sync lifecycle.
- Rendered-GUI, runtime, platform, Release-build, and AAB-level behavior of Package D and of `KF-BACKUP-004` remains unproven and out of scope for this initiative; the same is true of the Milestone 14B history page and Settings entry point, which belongs to separately authorized manual/GUI verification and the future pre-AAB validation gate in [BUILD_AND_RELEASE.md](BUILD_AND_RELEASE.md).
- No Beta 13, build, packaging, signing, publishing, store, or device activity has occurred, and no such task is active.

## Exact next action

- **State-driven next action for the `docs/kf-backup-004-post-merge-closure-v1` package:** determine the first incomplete step from live Git and GitHub state. If uncommitted in working tree, independent `REVIEW_ONLY` over the six-file diff is next, followed by `COMMIT_ONLY` → `PUSH_ONLY` → `PR_ONLY` → final `REVIEW_ONLY` → manual owner PR merge on GitHub → `POST_MERGE_SYNC_ONLY`.
- If this documentation package is already merged and synchronized when this document is read, the next authorized engineering task is determined from live roadmap and repository state (see [ROADMAP.md](ROADMAP.md) priority 15 residuals and beyond) rather than from this description.
- Automated agents never merge PRs or enable auto-merge; pull requests are merged exclusively by the repository owner manually through GitHub.

## Concise new-chat handoff

- Most recent recorded product-relevant milestone on `master`: `14138ccdab1e9b09a12ded002ff198d9b7312fcf` (PR #73, Milestone 14B merged).
- Current `master` baseline: `bec861fb8a054beb2804f1132b450da1e45dee90` (PR #77 merge commit). Discover literal HEAD dynamically per [docs/NEW_CHAT_BOOTSTRAP.md](NEW_CHAT_BOOTSTRAP.md).
- `DatabaseSchema.CurrentVersion` is 9 and Schema 9 is active on master.
- Beta 12 / build 12 remains the last confirmed external distribution (Google Play Internal Testing, user-tested 2026-07-30). No newer distribution has occurred.
- D1-D5 documentation reconciliation is complete. Package A, Package B, Package C, Package D (PR #76), and KF-BACKUP-004 (PR #77) are complete and merged on master.
- `KF-BACKUP-004` (Schema-9 LearningReview merge integrity) is **complete and merged** via PR #77 (merge commit `bec861fb8a054beb2804f1132b450da1e45dee90`); final PR review approved it `0 BLOCKER / 0 MAJOR / 0 MINOR / 0 NIT` and `POST_MERGE_SYNC_ONLY` completed successfully. Its evidence: RED `114/7/0/121` → GREEN `121/0/0/121`; `TEST_ONLY` `348/0/0`; `ALL_AUTOMATED` `1795/0/0`.
- The active task is the six-file post-merge documentation closure (`docs/kf-backup-004-post-merge-closure-v1`). Discover its exact lifecycle state from live Git/GitHub state.
- After post-merge documentation closure is merged, remaining Priority 15 residuals (`LegacyReviewSummaries` ordering, mid-session review-event export policy, `Learning.Cards`/Sense `StableId` ordering, legacy v1 planner label) remain open before Priority 16 automated GUI validation.
- No AAB, APK, Android build, signing, publishing, or store operation is authorized by this package.
