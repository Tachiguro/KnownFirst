# KnownFirst Current Work

## Last updated

2026-08-13 (PR #92 verified merged to `master`, making `1.0.0-beta.13`/build 13 the current source identity; its `POST_MERGE_SYNC_ONLY` completed exactly once and must not be repeated; this bounded `DOCUMENT_ONLY` package reconciles the resulting post-merge documentation; P16-A remains merged, binding source-foundation capability; Priority 15 remains complete on `master`)

## Repository

- Repository: https://github.com/Tachiguro/KnownFirst.git
- Only local project folder: `C:\Dev\KnownFirst` (see [ADR-0007](decisions/ADR-0007-single-canonical-working-directory.md))
- Active rule: use the single folder; create no worktree without explicit user approval.
- Only one writing agent may operate at a time.

## Verified product-state milestone

- Current verified `master` baseline: `a5a9e734af0db0639a38471433748e334ca34d65` (PR #92 merge commit). This is immutable historical merge evidence, not a permanent claim about the literal current `master` HEAD and not a frozen future Pre-AAB candidate; discover the exact current `master` HEAD dynamically per [docs/NEW_CHAT_BOOTSTRAP.md](NEW_CHAT_BOOTSTRAP.md).
- Source-controlled application identity on `master`: `1.0.0-beta.13` (build 13). PR #92 (final head `774b2245f64a986fe004f4ebd3288747642bdb0f`) merged `release/1.0.0-beta.13-candidate-v1` into `master`; `1.0.0-beta.13` is now binding `master` identity — see "Beta 13 merged source identity" below. Merging is not a build, package, device-validation, or distribution event.
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
- **PR #83 — `KF-BACKUP-005C` (populated-target Active learning-workflow convergence and conflict safety):** makes the established bounded populated-target Schema-10/V2 Active-workflow convergence contract binding `master` behavior. Merge commit `bed54d01624e80ca6dd5adf8af097e64fe33e588` (feature head `bc30e9ee9a3689cc4d8b7d108ac83dc037a1b962`); `POST_MERGE_SYNC_ONLY` completed successfully. Focused `Schema10ActiveArchive`: **8 passed / 0 failed / 0 skipped**; controlled affected/regression scope: **254 passed / 0 failed / 0 skipped** with Workers=1 and **254 passed / 0 failed / 0 skipped** with Workers=8; final relevant reviews: **0 BLOCKER / 0 MAJOR / 0 MINOR**.
- **PR #85 — `LegacyReviewSummaries` canonical ordering correction:** makes V2 `LegacyReviewSummaries` export ordering canonical across installations by replacing the former composite string key with typed ordering over `ReviewCount`, `ForgotCount`, `PartialCount`, `KnownCount`, nullable `LastReviewedAt` presence (null first), and normalized UTC ticks for a present timestamp. Null is explicitly distinguished from present UTC `DateTime.MinValue`; local `ReviewStateEntity.Id` is not ordering material; multiplicity is preserved. Feature head `baf5fcda0a017c1492a08dac730d683c1554784d`; merge commit `8eeaea58d87f9cfeb28cc4fc2520e5b277bb2526`; `POST_MERGE_SYNC_ONLY` completed successfully.
- **PR #87 — `Learning.Cards` canonical ordering correction:** makes V2 valid/resolved-card ordering semantic-first through existing `FutureCardIdentity`, preferred-Meaning `ExactMeaningVariantIdentity`, and typed emitted Card state, with Sense `StableId` only as late/final non-local ordering material; malformed/unresolved-reference snapshots retain deterministic mapper fallback behavior including Direction. Feature head `2cab8042887bed1004e7c26573a52fd59cc3b380`; merge commit `e97c83ac0cf7decf2915162e0e3a4abf24ee30d8`; `POST_MERGE_SYNC_ONLY` completed successfully. Final bounded affected/regression `TEST_ONLY`: **119 passed / 0 failed / 0 skipped**; independent review: **0 BLOCKER / 0 MAJOR / 0 MINOR / 0 NIT**.
- **PR #88 — PR #87 documentation closure:** merged feature head `4fc8b15f0861ab264ef7518ef66e810b0cf3c15c` through merge commit `133d34366204979d2905c665370531547a7a0b98`; `POST_MERGE_SYNC_ONLY` completed successfully.
- **PR #89 — Occurrence action-key correction:** merged feature head `d45a7e8fad533ddda5dda425356bf2095e8bafb6` (implementation commit `edbb49a87ff3f37337c413111a60f6cfa6805b88`) through merge commit `49d25cb8d7d113d1f0b1826369d9105a37d9207b`; `POST_MERGE_SYNC_ONLY` completed successfully. Independent review found **0 BLOCKER / 0 MAJOR / 0 MINOR / 0 NIT**.

## Priority-15 completion

**Priority 15 — Portable merge integrity hardening** is complete and binding `master` behavior. PR #89 merged the Occurrence action-key correction at `49d25cb8d7d113d1f0b1826369d9105a37d9207b` after implementation, bounded `TEST_ONLY`, independent review, owner merge, and `POST_MERGE_SYNC_ONLY`.

### Occurrence action-key correction — merged binding master behavior

- The proven production-representable defect is `MergeEntityKind.Occurrence`: both planners and the writer formerly used `SentenceId:VocabularyId` as an action lookup key. Distinct physical occurrences of one vocabulary in one sentence could therefore receive different classifications but collide in the writer's last-wins action map.
- The binding correction uses the shared lookup-only key `SourceMaterialArchiveId:Occurrence.Order`, with invariant-culture decimal formatting, in `MergePreflightPlanner`, `MergePreflightPlannerV2`, and `MergeWriterExecutor`. Valid V1/V2 graphs require `Order` to be unique within its source material, and archive IDs cannot contain `:`. Semantic `ComputeOccurrenceIdentity`, classifications, reason codes, preview counts, multiplicity, V1 compatibility, archive V2, DTOs, Schema 10, migrations, LearningReview contracts, scheduler, mapper ordering, UI, transport, synchronization, persistence, and public status/error-code contracts are unchanged.
- Focused TDD: genuine RED **0 passed / 2 failed / 0 skipped**; identical GREEN **2 passed / 0 failed / 0 skipped**. The initial fixture compilation error was corrected before the genuine RED and is not RED evidence. Bounded affected/regression `TEST_ONLY`: **257 passed / 0 failed / 0 skipped**; both occurrence regressions were included and green; pre/post `git diff --check` passed.
- Evidence is automated component/integration/contract evidence using isolated synthetic SQLite only. It is not ALL_AUTOMATED, ValidateAll, GitHub CI, Windows/Android build validation, rendered GUI/runtime/device evidence, or package/sign/publish/distribution evidence. Independent review found **0 BLOCKER / 0 MAJOR / 0 MINOR / 0 NIT**.

### Learning.Cards canonical ordering — merged master correction

The Priority-15 V2 `Learning.Cards` correction is merged and binding `master` behavior via PR #87 (feature head `2cab8042887bed1004e7c26573a52fd59cc3b380`, merge commit `e97c83ac0cf7decf2915162e0e3a4abf24ee30d8`); `POST_MERGE_SYNC_ONLY` completed successfully.

- Proven defect: `(resolved Sense StableId, Direction)` is unique for valid exportable databases, but Sense StableIds are independently generated installation-random GUID material. Equivalent installations can therefore assign the same positional archive-local `c-*` id to different semantic cards; downstream `ReviewEvents`, `AnswerVariantProgress`, and learning-workflow queue references consume those bindings. This is canonical archive-emission/local-reference instability, not demonstrated corruption, semantic merge divergence, or data loss.
- Merged correction: valid/resolved cards order semantic-first by existing `FutureCardIdentity`, preferred-Meaning `ExactMeaningVariantIdentity`, and typed emitted Card state; Sense StableId is only late/final non-local ordering material. Malformed or unresolved-reference snapshots retain mapper non-validation behavior and use an explicit deterministic fallback over available vocabulary/card/meaning content, including Direction, without local numeric ids or enumeration order. Multiplicity is preserved; no grouping or deduplication is introduced.
- Focused TDD and bounded regression evidence: initial RED **1 failed / 0 passed / 0 skipped** and GREEN **1 passed / 0 failed / 0 skipped**; the first bounded regression run returned **118 passed / 1 failed / 0 skipped** and exposed the tied-learning-session-sort-key regression; correction reproduction **1 passed / 1 failed / 0 skipped**; correction GREEN **2 passed / 0 failed / 0 skipped**; final affected/regression scope (`BackupCreationTests`, `BackupArchiveV2Tests`, `BackupModelContractTests`, `PortableImportEndToEndConvergenceTests`) **119 passed / 0 failed / 0 skipped**. Pre/post `git diff --check` passed.
- Database Schema 10, outer archive V2, DTO shape, migrations/validators, V1 mapper/reader/writer and v1-to-v2 compatibility, merge identities/planner classifications/writer semantics, scheduler semantics, UI, transport, synchronization, public status/error codes, and persistence contracts are unchanged.
- Independent review: **0 BLOCKER / 0 MAJOR / 0 MINOR / 0 NIT**. Evidence remains bounded automated unit/integration/contract evidence only: not ALL_AUTOMATED, ValidateAll, GitHub CI, platform/runtime builds, rendered GUI/device, packaging, signing, publishing, or distribution evidence. It does not claim universal whole-archive byte equality; unrelated installation-random StableIds remain.

The legacy `LearningReview` label (`CardId@ReviewedAtUtc`) can collide in a direct legacy plan but is not production-writer reachable: V1 input is upgraded to V2, production preflight and stale-plan validation use `MergePreflightPlannerV2`, and V2 LearningReview already uses its positional action key. It is not a current production-writer defect. Priority 16 is now active through P16-A; the previous not-started wording is historical only.

### KF-BACKUP-005C merged master capability

- A populated, quiescent Schema-10 target can additively accept a Schema-10/V2 archive with an Active learning workflow. Existing writer machinery preserves workflow and queue-row `StableId` values, remaps committed `LearningReview` rows to the new target-local integer session ID, and performs scheduler replay.
- For the same Active workflow `StableId`, exact durable workflow, queue, and multiplicity-aware LearningReview equivalence converges to `NoChanges`; no safety copy, writer invocation, or scheduler replay occurs.
- Any non-exact same-`StableId` Active state requires a deterministic merge decision and is non-executable with zero target mutation. This includes scalar, queue, review-event, and review-multiplicity mismatches, plus archive-Active/target-Completed state.
- Archive-Completed/target-Active remains blocked by the existing `BlockedByActiveWorkflow` / `ActiveWorkflowUnsupported` boundary. Source schema ≤9 Active learning workflows, Active `VocabularyReview`, and Active `PreparationBatch` remain unsupported.
- The Active-aware Schema-10 capture is read-only and preflight-only. Existing safety-copy capture remains fail-closed for an Active target; executable additive merges still require the existing validated safety copy and writer stale-plan safeguards.

**Final bounded evidence:** focused `Schema10ActiveArchive` **8/0/0**; controlled bounded affected/regression scope **254/0/0** with MSTest Workers=1 and again **254/0/0** with normal Workers=8; independent implementation re-review and historical-failure risk review each found **0 BLOCKER / 0 MAJOR / 0 MINOR**. Two earlier safety-copy-count observations (H1 idempotency 1→2; H2 semantic mismatch 1→0) remain documented as unexplained, non-reproducible historical transient failures, not passing runs and not a concrete correction target. This is automated unit/integration/persistence/contract evidence only; no ALL_AUTOMATED, ValidateAll, GitHub CI, platform/runtime, Release-build, rendered GUI, device/emulator, APK/AAB, signing, publishing, or distribution evidence exists for 005C.

### LegacyReviewSummaries canonical ordering — merged master correction

`LegacyReviewSummaries` canonical ordering is binding `master` behavior through PR #85 (feature head `baf5fcda0a017c1492a08dac730d683c1554784d`, merge commit `8eeaea58d87f9cfeb28cc4fc2520e5b277bb2526`); `POST_MERGE_SYNC_ONLY` completed successfully.

- `BackupModelMapperV2` now orders each vocabulary item's legacy summaries by typed emitted content: `ReviewCount`, `ForgotCount`, `PartialCount`, `KnownCount`, nullable `LastReviewedAt` presence (null first), then normalized UTC ticks for a present timestamp. This distinguishes null from a present UTC `DateTime.MinValue`; local `ReviewStateEntity.Id` is not ordering material.
- Multiplicity is preserved. Exact duplicate emitted summaries may tie only because their permutation is byte- and semantics-equivalent. No `Distinct`, grouping, or `HashSet` behavior was introduced.
- Genuine focused TDD recorded **1 failed / 0 passed** before the correction and **1 passed / 0 failed / 0 skipped** after it. Independent implementation review found **0 BLOCKER / 0 MAJOR / 0 MINOR / 0 NIT**. The bounded affected/regression `TEST_ONLY` scope (`BackupCreationTests`, `BackupArchiveV2Tests`, `BackupModelContractTests`) returned **110 passed / 0 failed / 0 skipped**; `git diff --check` passed.
- Database Schema 10, outer archive V2, V1 mapper/writer and reader behavior, v1-to-v2 upgrade, DTO shape, migration behavior, and merge policies remain unchanged.
- This evidence is bounded automated unit/integration/contract evidence only. It is not ALL_AUTOMATED, ValidateAll, GitHub CI, platform/runtime, rendered-GUI, device/emulator, package, signing, publishing, or distribution evidence.

The `Learning.Cards`/Sense `StableId` correction is the merged PR #87 package described above. The Occurrence action-key correction is the merged PR #89 completion of Priority 15.

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
- The historical KF-BACKUP-005B populated-target guard is superseded on current `master` by the bounded KF-BACKUP-005C convergence contract above; its historical empty-target package boundary remains accurate.

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
- **KF-BACKUP-005C:** merged via PR #83 (merge commit `bed54d01624e80ca6dd5adf8af097e64fe33e588`); `POST_MERGE_SYNC_ONLY` completed successfully and its populated-target convergence contract is current `master` behavior.
- Priority 15 is complete: PR #89 merged the Occurrence action-key correction; the mid-session review-event export policy is on `master` through 005B, PR #87 merged the `Learning.Cards`/Sense `StableId` ordering correction, and PR #88 completed its documentation closure.

## P16-A — merged Priority-16 foundation

- P16-A is **merged and binding `master` behavior** via PR #91, merge commit `810a7bcd0979c2f482135c07b56f834204b53118`. Its final independent remote review found **0 BLOCKER / 0 MAJOR / 0 MINOR / 0 NIT**. `POST_MERGE_SYNC_ONLY` completed successfully.
- History: the first independent review found **0 BLOCKER / 2 MAJOR / 3 MINOR / 0 NIT**; the resulting initial hardening correction `9719f3089ea758a6a65b3c5378834c1367227116` is historical and superseded. A renewed independent static review found **0 BLOCKER / 2 MAJOR / 0 MINOR / 0 NIT** (late SDK-style `BaseIntermediateOutputPath` assignment risk; screenshot summary SHA-256/persisted-artifact mismatch); both are corrected in `4545fcc927ffdd787c548b447cbd21d087460e25`. Final bounded source-level validation was MSTest **160 passed / 0 failed / 0 skipped** and Node **7 passed / 0 failed / 0 cancelled / 0 skipped / 0 todo**, with pre/post `git diff --check` passed and all seven correction-file SHA-256 hashes unchanged.
- **Current evidence boundary (unchanged by the merge):** P16-A is merged Android GUI-automation *source* infrastructure only. It has **not** been runtime-executed — no Android platform build, package, installation, Appium/UiAutomator2/Chromedriver runtime, rendered interaction, real screenshot evidence, ADB/device/emulator behavior, matrix-row automation, or CI evidence exists for it. `MANUAL_ANDROID_GUI` remains a distinct scope. P16-B and P16-C remain not started; no row of the GUI test matrix is automated.

Milestone 14A, 14B, KF-BACKUP-003 Package D, KF-BACKUP-004, KF-BACKUP-005A/005B/005C, and P16-A are all complete and merged on `master`; their history is unaffected.

## Beta 13 merged source identity

- The `1.0.0-beta.13` / build `13` release candidate, originally committed and pushed on `release/1.0.0-beta.13-candidate-v1` (original reviewed candidate/PR-opening commit `576865f9ed17f94c5f68aaa466f9bbf0244edb84`), was manually merged to `master` via PR #92 (final PR head `774b2245f64a986fe004f4ebd3288747642bdb0f`, merge commit `a5a9e734af0db0639a38471433748e334ca34d65`). Local `POST_MERGE_SYNC_ONLY` for PR #92 completed exactly once and must not be repeated. `1.0.0-beta.13` is now the current source identity on `master`; the merge commit is immutable historical evidence, not a claim that it remains the future Pre-AAB candidate commit.
- Implementation evidence: focused genuine RED **7 failed / 36 passed / 0 skipped / 43 total** → identical focused GREEN **43 passed / 0 failed / 0 skipped / 43 total**; bounded affected/regression `TEST_ONLY` **138 passed / 0 failed / 0 skipped / 138 total**, with pre/post SHA-256 hashes of all eight implementation files unchanged and `git diff --check` passed.
- Local independent pre-commit review passed **0 BLOCKER / 0 MAJOR / 0 MINOR / 1 NIT**. The first independent remote review of PR #92 found **0 BLOCKER / 1 MAJOR / 1 MINOR / 0 NIT**, limited to stale lifecycle documentation and no product-code defect. Candidate evidence is recorded in [docs/releases/1.0.0-beta.13.md](releases/1.0.0-beta.13.md).
- Merging is not a build, package, signing, device-validation, or distribution event. Beta 13 has not been finally built, fully validated, packaged, signed/package-verified, runtime/device validated, uploaded, distributed, or declared release-ready. `ALL_AUTOMATED`, final `UI_CONTRACT_AUTOMATED` evidence, `FULL_VALIDATION`, and the mandatory exact-candidate Pre-AAB Release-Readiness Gate ([docs/BUILD_AND_RELEASE.md](BUILD_AND_RELEASE.md) §7) remain pending and are not executed by this documentation package.
- The last confirmed external distribution remains `1.0.0-beta.12` / build 12 (2026-07-30); that evidence predates current Schema-10 populated-target merge and Active-workflow portability and does not validate them. No Beta-13 manual Android validation has yet been completed.

## Current blocker or pending validation

- None for Milestone 14B, its post-merge documentation closure, the Standing Delegation Governance Reconciliation, KF-BACKUP-003 Package D, KF-BACKUP-004, the KF-BACKUP-004 post-merge documentation closure (PR #78), KF-BACKUP-005A (PR #79), `LegacyReviewSummaries` canonical ordering (PR #85), P16-A (PR #91), or the Beta-13 source merge (PR #92): all completed their full lifecycle on `master`, including PR #92's `POST_MERGE_SYNC_ONLY` (completed exactly once; not to be repeated).
- Active package: this bounded post-merge documentation reconciliation (branch `docs/beta13-post-merge-reconciliation-v1`), correcting the eight documents that still described PR #92 as open/unmerged or Beta 13 as branch-local only. This package does not execute the Mandatory Pre-AAB Release-Readiness Gate.
- KF-BACKUP-005C, `LegacyReviewSummaries`, `Learning.Cards`, and the Occurrence action-key correction are merged and binding on `master`; PR #89 merged feature head `d45a7e8fad533ddda5dda425356bf2095e8bafb6` at `49d25cb8d7d113d1f0b1826369d9105a37d9207b` after independent review **0 BLOCKER / 0 MAJOR / 0 MINOR / 0 NIT** and `POST_MERGE_SYNC_ONLY`.
- Rendered-GUI, runtime, platform-build, Release-build, device/emulator, and AAB-level behavior remains unproven for every merged package, including P16-A and the KF-BACKUP-005 series.
- No Beta-13 external distribution, APK/AAB packaging, signing, publishing, or device/emulator activity has occurred. Windows and Android compile validation occurred for the earlier KF-BACKUP-005A candidate only; it is not current-candidate evidence.

## Exact next action

- **Next lifecycle action:** PR #92 is merged and closed; its lifecycle (review, manual merge, verification, `POST_MERGE_SYNC_ONLY`) is complete and must never be repeated. The active package is this bounded post-merge documentation reconciliation on `docs/beta13-post-merge-reconciliation-v1`. Its own remaining lifecycle phases — determined live from Git/GitHub state per [docs/NEW_CHAT_BOOTSTRAP.md](NEW_CHAT_BOOTSTRAP.md), never assumed from this file's prose — are: `REVIEW_ONLY`, `COMMIT_ONLY`, `PUSH_ONLY`, `PR_ONLY`, then the user's separate manual merge on GitHub, direct verification of that merge, and exactly one new, distinct `POST_MERGE_SYNC_ONLY` for that merge. Only after that new `POST_MERGE_SYNC_ONLY` completes does the next major activity become the mandatory exact-candidate Pre-AAB Release-Readiness Gate ([docs/BUILD_AND_RELEASE.md](BUILD_AND_RELEASE.md) §7), executed against the then-current exact `master` HEAD discovered live at that time — not against `a5a9e734af0db0639a38471433748e334ca34d65`, which is historical PR #92 merge evidence only. This documentation package does not execute the gate. P16-B and P16-C remain not started.

## Concise new-chat handoff

- Most recent recorded product-relevant milestone on `master`: `14138ccdab1e9b09a12ded002ff198d9b7312fcf` (PR #73, Milestone 14B merged).
- Current verified `master` baseline: `a5a9e734af0db0639a38471433748e334ca34d65` (PR #92 merge commit). This is immutable historical merge evidence, not a frozen future Pre-AAB candidate; discover future literal HEAD dynamically per [docs/NEW_CHAT_BOOTSTRAP.md](NEW_CHAT_BOOTSTRAP.md).
- `DatabaseSchema.CurrentVersion` is **10** and Schema 10 is active on `master`.
- Beta 12 / build 12 remains the last confirmed external distribution (Google Play Internal Testing, user-tested 2026-07-30). No newer external distribution has occurred.
- D1-D5 documentation reconciliation is complete. Package A, Package B, Package C, Package D (PR #76), KF-BACKUP-004 (PR #77), KF-BACKUP-004 post-merge closure (PR #78), and KF-BACKUP-005A (PR #79) are complete and merged on `master`.
- **KF-BACKUP-005B:** merged master capability via PR #81; it implements Schema-10 Active workflow export and empty-target restore from durable state, with focused final `TEST_ONLY` green.
- **KF-BACKUP-005C:** populated-target Active convergence is binding `master` behavior through PR #83 (merge commit `bed54d01624e80ca6dd5adf8af097e64fe33e588`); `POST_MERGE_SYNC_ONLY` completed successfully.
- **PR #85 — `LegacyReviewSummaries` canonical ordering:** merged master behavior at `8eeaea58d87f9cfeb28cc4fc2520e5b277bb2526` (feature head `baf5fcda0a017c1492a08dac730d683c1554784d`); `POST_MERGE_SYNC_ONLY` completed successfully.
- **PR #87 / PR #88:** PR #87 remains merged product behavior; PR #88 completed its documentation closure at `133d34366204979d2905c665370531547a7a0b98` from feature head `4fc8b15f0861ab264ef7518ef66e810b0cf3c15c`, followed by `POST_MERGE_SYNC_ONLY`.
- **PR #89 / PR #90:** Priority 15 is Committed. PR #90 merged the Priority-15 post-merge documentation closure at `9a3480678168414e4b8060d0673ec41c8f74767a` and local `POST_MERGE_SYNC_ONLY` completed.
- **PR #91:** P16-A is merged and binding `master` behavior at `810a7bcd0979c2f482135c07b56f834204b53118`; final independent remote review **0 BLOCKER / 0 MAJOR / 0 MINOR / 0 NIT**; `POST_MERGE_SYNC_ONLY` completed successfully. It is merged source infrastructure only — no Android runtime/device/platform/package, rendered screenshot, matrix-row, or CI evidence exists. P16-B and P16-C remain not started.
- **Beta-13 merged source identity:** originally committed and pushed on `release/1.0.0-beta.13-candidate-v1` from base `810a7bcd0979c2f482135c07b56f834204b53118`; original reviewed candidate/PR-opening commit `576865f9ed17f94c5f68aaa466f9bbf0244edb84`; manually merged to `master` via PR #92 (final PR head `774b2245f64a986fe004f4ebd3288747642bdb0f`, merge commit `a5a9e734af0db0639a38471433748e334ca34d65`); local `POST_MERGE_SYNC_ONLY` completed exactly once and must not be repeated. `1.0.0-beta.13` is now the current source identity on `master`. The local pre-commit review passed **0/0/0/1**; the first remote review found lifecycle-documentation-only **0/1/1/0**. Focused RED→GREEN and bounded `TEST_ONLY` (138/0/0) evidence is recorded in [docs/releases/1.0.0-beta.13.md](releases/1.0.0-beta.13.md); the mandatory exact-candidate Pre-AAB Release-Readiness Gate, build, package, and distribution remain pending and are addressed by future packages, not this documentation reconciliation.
- **This package (post-merge documentation reconciliation):** corrects the eight documents (`PROJECT_STATE.md`, `CURRENT_WORK.md`, `ROADMAP.md`, `README.md`, `CHANGELOG.md`, `docs/releases/1.0.0-beta.13.md`, `docs/BETA_TESTING.md`, `docs/GUI_TEST_MATRIX.md`) that still described PR #92 as open/unmerged or Beta 13 as a branch-local-only candidate. `docs/VERSIONING.md` was reviewed and found already current; no edit was made. This package is `DOCUMENT_ONLY`: no code, test, schema, or version/build-number changes; the Mandatory Pre-AAB Release-Readiness Gate is not executed by it.
- No Beta-13 external distribution, APK/AAB packaging, signing, publishing, or device/emulator activity has occurred. Windows and Android compile validation occurred for KF-BACKUP-005A only, not for 005B or the current candidate.
