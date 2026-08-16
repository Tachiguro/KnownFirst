# KnownFirst Current Work

## Last updated

2026-08-16 (Final Beta-13 Pre-AAB documentation-evidence reconciliation; pre-correction synchronized `master` baseline at `7d2e5865ede7e46dc2e0f9fe1cee4110adde4b92` following PR #118 merge; exact-master `FULL_VALIDATION` [`ValidateAll-20260816-160649.log`] passed 1963/1963 tests, Windows/Android Debug/Release, 114/114 AOT, 0 prohibited diagnostics; prior attempted final certification was rejected due to stale Gate 6/7 documentation evidence; signed Android Release APK `KnownFirst-1.0.0-beta.13-android-release.apk` [SHA-256 `53bbcb18b62927dae0af0a63d0e6a3cda6a8420c1c9517d354c638504b9ac6b6`] produced and verified for manual Android validation; final Beta-13 Release AAB creation explicitly authorized for Google Play Internal Testing once mandatory Pre-AAB gate is certified on merged `master` [NOT YET CREATED]; subsequent Google Play Internal Testing upload/distribution separately authorized [NOT YET PERFORMED]; Closed Testing, Open Testing, Production, and public rollout remain unauthorized).

## Repository and Worktree Governance

- Repository: https://github.com/Tachiguro/KnownFirst.git
- Canonical local working directory: Exactly one canonical local checkout and one normal worktree per environment (defaulting to `C:\Dev\KnownFirst`, see [ADR-0007](decisions/ADR-0007-single-canonical-working-directory.md)).
- Single writer: Only one writing agent operates at a time in the repository.
- Single worktree: Additional worktrees or repository copies require explicit user authorization.

## Active Work Package

- **Active task / branch:** `docs/beta13-pre-aab-final-evidence-reconciliation-v1` (live Git/GitHub state remains authoritative)
- **Work package:** Final Beta-13 Pre-AAB documentation-evidence reconciliation and release-readiness preparation.
- **Previous merged packages:**
  - PR #113 (`fix: preserve git exit codes in windows packaging`): Corrected native Git exit-code capture in packaging scripts under Windows PowerShell 5.1. `POST_MERGE_SYNC_ONLY` completed.
  - PR #114 (`fix: harden windows packaging isolation and logs`): `<DefaultItemExcludes>` ordinary `bin\**;obj\**` protection against `BLAZOR102` during redirected builds and synchronous launcher output capture surviving terminating child exceptions. `POST_MERGE_SYNC_ONLY` completed.
  - PR #115 (`docs: record verified windows portable package evidence`): Reconciled durable release documentation (`PROJECT_STATE.md`, `docs/releases/1.0.0-beta.13.md`) with physical Windows Portable package evidence produced on `master`. `POST_MERGE_SYNC_ONLY` completed.
  - PR #116 (`docs: reconcile current work after PR 115`): Reconciled `docs/CURRENT_WORK.md` active work package state and verified master baseline after PR #115 merge. `POST_MERGE_SYNC_ONLY` completed.
  - PR #117 (`docs: add internal testing risk acceptance`): Added §7.1 Owner Risk Acceptance for Google Play Internal Testing in `BUILD_AND_RELEASE.md` and updated `docs/releases/1.0.0-beta.13.md` release record. `POST_MERGE_SYNC_ONLY` completed.
  - PR #118 (`docs: reconcile beta13 pre-aab release state`): Reconciled durable release state after PR #117; merged to `7d2e5865ede7e46dc2e0f9fe1cee4110adde4b92`. `POST_MERGE_SYNC_ONLY` completed.

## Verified Baseline & Release Boundaries

- **Authoritative `master` baseline:** Synchronized `master` at `7d2e5865ede7e46dc2e0f9fe1cee4110adde4b92` (PR #118 merge commit) serves as the pre-correction baseline for this package. Prior PR merge commits (through PR #118) and their `POST_MERGE_SYNC_ONLY` completions are historical facts.
- **Exact-master validation on `7d2e5865...`:** Exact-master `FULL_VALIDATION` passed all 5 stages on `7d2e5865...` (1963/1963 automated tests, Windows Debug/Release, Android Debug/Release, 114/114 AOT, 0 prohibited release diagnostics; log `ValidateAll-20260816-160649.log`). A subsequent attempted final Pre-AAB certification was rejected because Gate Items 6 and 7 contained stale durable documentation evidence. Packaging did not proceed and remains blocked until this reconciliation merges and passes exact-master validation.
- **Source identity:** `1.0.0-beta.13` (build 13) merged on `master` via PR #92. Merging is not a packaging, signing, or distribution event.
- **Confirmed external distribution:** `1.0.0-beta.12` / build 12 (Google Play Internal Testing, confirmed 2026-07-30). No newer Android external distribution has occurred.
- **Database schema:** SQLite `PRAGMA user_version` 10 on `master`.
- **Supported platforms:** Android (Google Play Internal Testing) and Windows development/verification. iOS and Mac Catalyst remain removed.
- **Windows distribution packaging:** A real self-contained Windows Portable Release ZIP (`KnownFirst-1.0.0-beta.13-build13-win-x64-9e455d0.zip`) and matching SHA-256 sidecar were produced and verified on `master` at source commit `9e455d0e03494cac8e713cd4d16c66946124f852` on 2026-08-16. Clean-PC runtime execution, MSIX packaging, and external distribution remain separate unexecuted milestones.
- **Android Release APK:** A signed Android Release APK (`KnownFirst-1.0.0-beta.13-android-release.apk`, SHA-256 `53bbcb18b62927dae0af0a63d0e6a3cda6a8420c1c9517d354c638504b9ac6b6`) was produced and verified on physical hardware for manual Android validation.
- **Mandatory Pre-AAB Release-Readiness Gate:** Final exact-HEAD Pre-AAB Release-Readiness Gate ([docs/BUILD_AND_RELEASE.md](BUILD_AND_RELEASE.md) §7) remains pending on merged `master` following this documentation reconciliation.
- **Final Release AAB:** **EXPLICITLY AUTHORIZED FOR GOOGLE PLAY INTERNAL TESTING ONCE PRE-AAB GATE IS CERTIFIED ON MERGED MASTER / NOT YET CREATED**.
- **Google Play Internal Testing Upload/Distribution:** **SEPARATELY AUTHORIZED FOLLOWING VERIFIED AAB / NOT YET PERFORMED**. Closed Testing, Open Testing, Production, and public rollout remain unauthorized.

## Exact Next Action

- Complete this bounded documentation-correction package through its governed PR lifecycle.
- Following repository-owner manual merge of the PR and `POST_MERGE_SYNC_ONLY` to `master`, run mandatory exact-master `FULL_VALIDATION` on the new `master` HEAD.
- Execute final Pre-AAB `REVIEW_ONLY` to certify all Pre-AAB gate items against [docs/BUILD_AND_RELEASE.md](BUILD_AND_RELEASE.md) §7 and §7.1 on the new merged `master` HEAD.
- Only after successful final gate certification, execute the authorized `PACKAGE_ONLY` `ANDROID_GOOGLE_PLAY_AAB` operation to create and verify the Google Play AAB.
