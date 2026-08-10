# KnownFirst Current Work

## Last updated

2026-08-10 (KF-BACKUP-005B complete on `master` via PR #81; KF-BACKUP-005C is next and has not started)

## Repository

- Repository: https://github.com/Tachiguro/KnownFirst.git
- Only local project folder: `C:\Dev\KnownFirst` (see [ADR-0007](decisions/ADR-0007-single-canonical-working-directory.md))
- Active rule: use the single folder; create no worktree without explicit user approval.
- Only one writing agent may operate at a time.

## Verified product-state milestone

- Most recent product-relevant milestone commit: `14138ccdab1e9b09a12ded002ff198d9b7312fcf` (Milestone 14B, PR #73 merged). This is historical milestone evidence, not a claim about the literal current `master` HEAD; discover the exact current `master` HEAD dynamically per [docs/NEW_CHAT_BOOTSTRAP.md](NEW_CHAT_BOOTSTRAP.md).
- Source-controlled application identity: `1.0.0-beta.12` (build 12)
- Confirmed distribution: `1.0.0-beta.12` / build 12 was distributed via Google Play Internal Testing and user-tested (confirmed 2026-07-30). No newer Android external distribution, AAB/APK package, Internal Testing release, installation, or user test has occurred since. Android compile builds have occurred as validation only.
- Active database schema on master: SQLite `PRAGMA user_version` 10
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
- **PR #78 — `KF-BACKUP-004` Post-Merge Documentation Closure:** reconciled `CURRENT_WORK.md`, `PROJECT_STATE.md`, `ROADMAP.md`, `BACKLOG.md`, `DATABASE_CONTRACT.md`, and `docs/architecture/backup-merge-v1-design.md` with the merged `KF-BACKUP-004` state on `master`; merge commit `e3511ba6e7466c2fa63c4c46fd37f4e427f2a931`. `POST_MERGE_SYNC_ONLY` completed successfully.
- **PR #79 — `KF-BACKUP-005A` (Schema-10 stable learning-workflow identity foundation):** `DatabaseSchema.CurrentVersion` advances to **10** on `master`; immutable `StableId` columns added to `LearningSessions` and `LearningSessionCards`; deterministic SHA-256 64-character Completed bootstrap; one-time GUID 32-character Active bootstrap; archive V2 DTO evolution with trailing nullable StableIds; source ≤9 Completed compatibility, source ≤9 Active rejection, source ≥10 StableId validation; `LearningSessionId` excluded from `LearningReview` merge identity; Active workflow portable continuation excluded (deferred to 005B/005C); merge commit `e56b8bfa27dfe1d630fbacfed24e6d56ea876026`. `POST_MERGE_SYNC_ONLY` completed successfully.
- **PR #81 — `KF-BACKUP-005B` (portable Active learning-workflow restore into an empty target):** Schema-10 ordinary portable export now carries an Active `LearningSession`, its persisted queue, committed `LearningReview` history, and workflow/queue `StableId` values; empty-target restore resumes through normal `LearningService` behavior from the last durably committed state. Final focused `TEST_ONLY`: **135 passed / 0 failed / 0 skipped**; final independent PR review: **0 BLOCKER / 0 MAJOR / 0 MINOR**; no GitHub CI evidence existed for the PR head. Merge commit `dc56e8412966ac32531c4b0358526582702d6d24` (feature commit `e8236bba3d23e942014e6979b661e0c77a2a3bdd`); `POST_MERGE_SYNC_ONLY` completed successfully.

## Currently active package

**`KF-BACKUP-005B` — Portable Active Learning-Workflow Restore Into Empty Target** is merged and binding `master` behavior via PR #81 (feature commit `e8236bba3d23e942014e6979b661e0c77a2a3bdd`, merge commit `dc56e8412966ac32531c4b0358526582702d6d24`). `POST_MERGE_SYNC_ONLY` completed successfully.

### KF-BACKUP-005B capability on master

- `DatabaseSchema.CurrentVersion` remains **10**; no Schema 11 was introduced.
- The `.kfarchive` outer format remains **V2**; no archive V3 was introduced.
- Ordinary portable export from a Schema-10 source includes an Active `LearningSession`, its persisted queue, and committed `LearningReview` rows belonging to that workflow.
- Restore into an **empty** Schema-10 installation recreates the Active workflow and resumes it through the normal production `LearningService` path from the last durably committed application/database state.
- The restored workflow remains Active. No fake `Completed` state is fabricated, and the user does not need to finish the session before portable export.
- Durable state includes completed queue items and their ratings/completion state, committed mid-session review history, remaining incomplete items, queue ordering, review-to-session remapping to the new local integer session ID, and preservation of transported workflow/queue `StableId` values.
- Transient or uncommitted UI state is not claimed to be portable.
- Existing Completed Schema-10 portability remains supported: Completed status, non-null completion timestamp, queue/history, workflow `StableId`, and queue-item `StableId` values survive restore.
- Schema-8 and Schema-9 ordinary portable export remain Completed-only. Source schema ≤9 Active learning workflows remain unsupported/rejected. Active `VocabularyReview` and Active `PreparationBatch` remain unsupported.
- Source ≥10 workflow and queue `StableId` values remain mandatory, canonical, unique, and transported unchanged.
- A populated target still fails closed for a valid Schema-10 archive containing an Active learning workflow: preview and actual import return `BackupErrorCodes.ActiveWorkflowUnsupported`, the target is not mutated, and executable merge/writer behavior does not run. This is the KF-BACKUP-005C boundary.

**Final exact-tree focused `TEST_ONLY` evidence:**

- Scope: `BackupArchiveV2Tests`, `BackupModelContractTests`, `Schema8BackupRestoreTests`, `BackupServiceImportRoutingTests`, and `MergePreflightServiceTests`.
- Result: **135 passed / 0 failed / 0 skipped**, with normal process completion, 0 build warnings, and 0 build errors.
- Pre- and post-run `git diff --check`: passed.

**Supplementary broader evidence:** an earlier test-project run returned **1820 passed / 0 failed / 0 skipped** against the same unchanged 005B production implementation. It occurred before the final acceptance-test additions and is therefore supplementary production-regression evidence, not exact-final-test-tree evidence.

**Not validated for KF-BACKUP-005B:** `ValidateAll`; Windows platform build; Android platform build; rendered GUI; physical device/emulator behavior; Release-build behavior; APK/AAB; signing; publishing; or Google Play distribution. The earlier KF-BACKUP-005A `ValidateAll` result does not validate the 005B executable tree.

**Lifecycle:** implementation → focused final `TEST_ONLY` green → final independent PR review approved (**0 BLOCKER / 0 MAJOR / 0 MINOR**) → PR #81 manually merged → `POST_MERGE_SYNC_ONLY` complete. No GitHub CI evidence existed for the 005B head; that absence is not passing CI evidence.

### Merged Schema-10 capabilities on master

- `DatabaseSchema.CurrentVersion` is **10** on `master`.
- Schema 10 introduces `StableId` columns on `LearningSessions` and `LearningSessionCards`.
- Legacy Completed learning sessions receive deterministic SHA-256 64-character StableIds on migration.
- Legacy Active learning sessions receive fresh GUID 32-character StableIds once on migration.
- Physical DDL adds nullable `TEXT` columns; shape validation and unique indexes enforce non-null canonical `StableId` values on all valid rows.
- Archive/source compatibility: source ≤9 Completed portable workflows may receive bootstrap StableIds; source ≤9 Active portable workflows remain unsupported/rejected; source ≥10 workflows require valid StableIds.
- `LearningSessionId` exclusion from `LearningReview` merge identity (established by KF-BACKUP-004) is preserved and unchanged.
- **Portability boundary:** Active learning-workflow portable continuation is explicitly excluded from 005A scope.

**Validation evidence (KF-BACKUP-005A candidate checkpoint `551399df22131e0214e87b43a3eeaea9ae40ddf9`):**

- Final `ALL_AUTOMATED`: **1812 passed / 0 failed / 0 skipped / 1812 total** (`dotnet test ./KnownFirst.Tests/KnownFirst.Tests.csproj -c Debug`; duration: 9m 18s).
- Focused five-class correction scope: 215 passed / 0 failed / 0 skipped / 215 total.
- Original Stage-1 scope: 845 passed / 0 failed / 0 skipped / 845 total.
- Wikipedia architecture sentinel: 7 passed / 0 failed / 0 skipped / 7 total.
- Canonical candidate `ValidateAll` (`.\scripts\knownfirst.ps1 -Action ValidateAll -Force` on latest fully validated executable/test-tree checkpoint `551399df22131e0214e87b43a3eeaea9ae40ddf9`): **FULL GREEN** (ALL_AUTOMATED 1812/1812 passed; Windows Debug/Release builds passed; Android Debug/Release builds passed; 0 build errors; 0 AOT/trimming/source-gen warnings; 8 non-blocking Android Release XML-documentation warnings).
  - Note: this validation confirmed both the KF-BACKUP-005A Schema-10 package and the inherited baseline `CS0542` compile fix in `Components/Pages/ReleaseNotes.razor`.
  - Later commits after `551399df...` were documentation-only with no executable/test/build-input changes.
- **Not validated / Out of scope:** rendered GUI behavior, physical device / emulator execution, APK/AAB creation, signing, publishing, and Active portable workflow resume (deferred to KF-BACKUP-005B).

**Succession:**

- **KF-BACKUP-005B:** complete and merged on `master` via PR #81.
- **KF-BACKUP-005C:** the next bounded Priority-15 development package after the 005B post-merge documentation lifecycle completes; populated-target Active workflow convergence and conflict safety remain unimplemented.
- Remaining Priority-15 residuals (`LegacyReviewSummaries` ordering, `Learning.Cards`/Sense `StableId` ordering, legacy v1 planner label) continue before Priority 16 (automated GUI validation). The mid-session review-event export policy is now on `master` through 005B.

Milestone 14A, 14B, KF-BACKUP-003 Package D, KF-BACKUP-004, and KF-BACKUP-005A are all complete and merged on `master`; their history is unaffected.

## Current blocker or pending validation

- None for Milestone 14B, its post-merge documentation closure, the Standing Delegation Governance Reconciliation, KF-BACKUP-003 Package D, KF-BACKUP-004, the KF-BACKUP-004 post-merge documentation closure (PR #78), or KF-BACKUP-005A (PR #79): all completed their full lifecycle on `master`.
- No KF-BACKUP-005B implementation, focused-`TEST_ONLY`, review, commit, push, PR, merge, or synchronization lifecycle action remains. KF-BACKUP-005C is the next bounded Priority-15 package and has not started.
- Rendered-GUI, runtime, platform-build, Release-build, device/emulator, and AAB-level behavior remains unproven and out of scope for 005B.
- No Beta 13 external distribution, APK/AAB packaging, signing, publishing, or device/emulator activity has occurred. Windows and Android compile validation occurred for the earlier KF-BACKUP-005A candidate only; it is not 005B evidence.

## Exact next action

- **Next development package:** KF-BACKUP-005C, through its own separately authorized isolated planning lifecycle. It has not started.
- Automated agents never merge PRs or enable auto-merge; pull requests are merged exclusively by the repository owner manually through GitHub.

## Concise new-chat handoff

- Most recent recorded product-relevant milestone on `master`: `14138ccdab1e9b09a12ded002ff198d9b7312fcf` (PR #73, Milestone 14B merged).
- Current verified `master` baseline at the beginning of this documentation lifecycle: `dc56e8412966ac32531c4b0358526582702d6d24` (PR #81 merge commit). Discover future literal HEAD dynamically per [docs/NEW_CHAT_BOOTSTRAP.md](NEW_CHAT_BOOTSTRAP.md).
- `DatabaseSchema.CurrentVersion` is **10** and Schema 10 is active on `master`.
- Beta 12 / build 12 remains the last confirmed external distribution (Google Play Internal Testing, user-tested 2026-07-30). No newer external distribution has occurred.
- D1-D5 documentation reconciliation is complete. Package A, Package B, Package C, Package D (PR #76), KF-BACKUP-004 (PR #77), KF-BACKUP-004 post-merge closure (PR #78), and KF-BACKUP-005A (PR #79) are complete and merged on `master`.
- **KF-BACKUP-005B:** merged master capability via PR #81; it implements Schema-10 Active workflow export and empty-target restore from durable state, with focused final `TEST_ONLY` green.
- **Next implementation package:** `KF-BACKUP-005C` populated-target Active convergence, followed by the remaining Priority-15 residuals; it has not started and requires its own isolated planning lifecycle.
- No Beta 13 external distribution, APK/AAB packaging, signing, publishing, or device/emulator activity has occurred. Windows and Android compile validation occurred for KF-BACKUP-005A only, not for 005B.
