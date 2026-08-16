# KnownFirst Current Work

## Last updated

2026-08-16 (Beta-13 Pre-AAB Documentation Reconciliation; canonical `master` synchronized following PR #121 merge at `c9fc3fec5d8725754d517e17d1eb83343c5481f2`; exact-master `FULL_VALIDATION` [`ValidateAll-20260816-234539.log`] passed 1964/1964 tests, Windows/Android Debug/Release, 114/114 AOT, 0 prohibited diagnostics, strict Android Release compiler warning parity active with 0 warnings / 0 errors; prior initial `GooglePlayBundle` packaging attempt on `eb4a2302...` failed and its certification was superseded; subsequent Pre-AAB review on `c9fc3fec...` found non-actionable due to stale Gate-6/7 durable release documentation; active documentation package `docs/beta13-pre-aab-reconciliation-v1` reconciles durable release records to restore Gate-6/7 compliance; signed Android Release APK `KnownFirst-1.0.0-beta.13-android-release.apk` [SHA-256 `53bbcb18b62927dae0af0a63d0e6a3cda6a8420c1c9517d354c638504b9ac6b6`] remains verified; final Beta-13 Release AAB remains NOT CREATED; subsequent Google Play Internal Testing upload/distribution NOT PERFORMED; Closed Testing, Open Testing, Production, and public rollout remain unauthorized).

## Repository and Worktree Governance

- Repository: https://github.com/Tachiguro/KnownFirst.git
- Canonical local working directory: Exactly one canonical local checkout and one normal worktree per environment (defaulting to `C:\Dev\KnownFirst`, see [ADR-0007](decisions/ADR-0007-single-canonical-working-directory.md)).
- Single writer: Only one writing agent operates at a time in the repository.
- Single worktree: Additional worktrees or repository copies require explicit user authorization.

## Active Work Package

- **Active work package:** Beta-13 Pre-AAB documentation reconciliation and release record alignment.
- **Package branch provenance:** `docs/beta13-pre-aab-reconciliation-v1` (task branch for this documentation reconciliation package; authoritative live checkout/branch and worktree state are discovered directly from Git/GitHub, with `master` as canonical branch).
- **Previous merged packages:**
  - PR #113 (`fix: preserve git exit codes in windows packaging`): Corrected native Git exit-code capture in packaging scripts under Windows PowerShell 5.1. `POST_MERGE_SYNC_ONLY` completed.
  - PR #114 (`fix: harden windows packaging isolation and logs`): `<DefaultItemExcludes>` ordinary `bin\**;obj\**` protection against `BLAZOR102` during redirected builds and synchronous launcher output capture surviving terminating child exceptions. `POST_MERGE_SYNC_ONLY` completed.
  - PR #115 (`docs: record verified windows portable package evidence`): Reconciled durable release documentation (`PROJECT_STATE.md`, `docs/releases/1.0.0-beta.13.md`) with physical Windows Portable package evidence produced on `master`. `POST_MERGE_SYNC_ONLY` completed.
  - PR #116 (`docs: reconcile current work after PR 115`): Reconciled `docs/CURRENT_WORK.md` active work package state and verified master baseline after PR #115 merge. `POST_MERGE_SYNC_ONLY` completed.
  - PR #117 (`docs: add internal testing risk acceptance`): Added §7.1 Owner Risk Acceptance for Google Play Internal Testing in `BUILD_AND_RELEASE.md` and updated `docs/releases/1.0.0-beta.13.md` release record. `POST_MERGE_SYNC_ONLY` completed.
  - PR #118 (`docs: reconcile beta13 pre-aab release state`): Reconciled durable release state after PR #117; merged to `7d2e5865ede7e46dc2e0f9fe1cee4110adde4b92`. `POST_MERGE_SYNC_ONLY` completed.
  - PR #119 (`docs: reconcile final beta13 pre-aab evidence`): Reconciled pre-reconciliation master baseline to `7d2e5865ede7e46dc2e0f9fe1cee4110adde4b92` and exact-master validation evidence; merged to `f387f8a8838def3ec657266066be44dd39b134e9`. `POST_MERGE_SYNC_ONLY` completed.
  - PR #120 (`docs: normalize final beta13 pre-aab evidence`): Normalized historical foundation baselines, separated technical gate rows, and hardened Gate 16/17 lifecycle-neutral evaluation; merged to `eb4a2302e7a0be20f22d38ba9e5ec7af3feb36c6`. `POST_MERGE_SYNC_ONLY` completed.
  - PR #121 (`fix: harden pre-aab warning gate parity`): Hardened `scripts/knownfirst.ps1` ValidateAll Android Release step with `-warnaserror`, `-p:ILLinkTreatWarningsAsErrors=true`, and `--no-incremental` without creating packages, added contract test coverage, and resolved 8 XML documentation diagnostics across 7 source files without warning suppression; merged to `c9fc3fec5d8725754d517e17d1eb83343c5481f2`. `POST_MERGE_SYNC_ONLY` completed.

## Verified Baseline & Release Boundaries

- **Canonical `master` baseline:** Synchronized `master` at `c9fc3fec5d8725754d517e17d1eb83343c5481f2` (PR #121 merge commit) serves as the foundation baseline for this reconciliation package. Prior PR merge commits (through PR #121) and their `POST_MERGE_SYNC_ONLY` completions are historical facts; live current master identity is always discovered dynamically from Git/GitHub.
- **Historical exact-master validation evidence:**
  - `ValidateAll-20260816-170329.log` on `f387f8a8...`: PASSED (1963/1963 automated tests, Windows Debug/Release, Android Debug/Release, 114/114 AOT, 0 prohibited release diagnostics).
  - `ValidateAll-20260816-194413.log` on `eb4a2302...`: PASSED (1963/1963 automated tests, Windows Debug/Release, Android Debug/Release, 114/114 AOT, 0 prohibited release diagnostics, 73/73 UI contract tests, 10/10 diagnostic gate tests).
  - `ValidateAll-20260816-234539.log` on `c9fc3fec...`: PASSED (1964/1964 automated tests, Windows Debug/Release, Android Debug/Release, 114/114 Native AOT, 0 prohibited release diagnostics, strict compiler warning parity active with 0 warnings / 0 errors).
- **Historical packaging attempt on `eb4a2302...` (2026-08-16):** Canonical `GooglePlayBundle` execution failed (`GooglePlayBundle-20260816-195953.log`, exit code 1) because the publisher's `-warnaserror` promoted 8 XML documentation compiler warnings to errors. Prior Gate-17 certification was superseded for packaging purposes. Final Beta-13 AAB was not created; no upload occurred; packaging was not retried.
- **Correction status:** PR #121 merged into `master`; all 8 XML documentation diagnostics resolved; `scripts/knownfirst.ps1` ValidateAll Android Release step hardened with `-warnaserror`, `-p:ILLinkTreatWarningsAsErrors=true`, and `--no-incremental`; contract tests passed; strict exact-master compilation verified with 0 warnings / 0 errors and 114/114 Native AOT.
- **Pre-AAB Gate Status:** The earlier affirmative Gate-17 certification on `eb4a2302...` is historical and superseded; the subsequent affirmative review report on `c9fc3fec...` is non-actionable because Gate Items 6 and 7 were not truly satisfied by durable release documentation at review time. Gate 17 remains **NOT CERTIFIED** until this documentation package completes its full PR lifecycle, is merged and synchronized, fresh exact-master validation passes on the new merge commit, and a new final Pre-AAB `REVIEW_ONLY` certifies all gate items.
- **Source identity:** `1.0.0-beta.13` (build 13) merged on `master` via PR #92. Merging is not a packaging, signing, or distribution event.
- **Confirmed external distribution:** `1.0.0-beta.12` / build 12 (Google Play Internal Testing, confirmed 2026-07-30). No newer Android external distribution has occurred.
- **Database schema:** SQLite `PRAGMA user_version` 10 on `master`.
- **Supported platforms:** Android (Google Play Internal Testing) and Windows development/verification. iOS and Mac Catalyst remain removed.
- **Windows distribution packaging:** A real self-contained Windows Portable Release ZIP (`KnownFirst-1.0.0-beta.13-build13-win-x64-9e455d0.zip`) and matching SHA-256 sidecar were produced and verified on `master` at source commit `9e455d0e03494cac8e713cd4d16c66946124f852` on 2026-08-16. Clean-PC runtime execution, MSIX packaging, and external distribution remain separate unexecuted milestones.
- **Android Release APK:** A signed Android Release APK (`KnownFirst-1.0.0-beta.13-android-release.apk`, SHA-256 `53bbcb18b62927dae0af0a63d0e6a3cda6a8420c1c9517d354c638504b9ac6b6`) was produced and verified on physical hardware for manual Android validation.
- **Final Release AAB:** **BLOCKED PENDING POST-MERGE GATE CERTIFICATION / NOT YET CREATED**.
- **Google Play Internal Testing Upload/Distribution:** **SEPARATELY AUTHORIZED FOLLOWING VERIFIED AAB / NOT YET PERFORMED**. Closed Testing, Open Testing, Production, and public rollout remain unauthorized.

## Governed Operational Sequence & Next Actions

Continue from the first unfinished step of the governed Pre-AAB lifecycle:
1. Complete this active documentation reconciliation package through its governed PR lifecycle (`REVIEW_ONLY` → `COMMIT_ONLY` → `TEST_ONLY` → `PUSH_ONLY` → `PR_ONLY` → remote `REVIEW_ONLY` → owner manual merge).
2. Following repository-owner manual merge of the documentation PR to `master`, perform `POST_MERGE_SYNC_ONLY` exactly once.
3. On the resulting live canonical `master`, run mandatory exact-master `FULL_VALIDATION` (`ValidateAll -Force`).
4. Execute final Pre-AAB `REVIEW_ONLY` to certify all Pre-AAB gate items against [docs/BUILD_AND_RELEASE.md](BUILD_AND_RELEASE.md) §7 and §7.1 on that exact `master` HEAD.
5. Upon successful affirmative gate certification, execute the authorized `PACKAGE_ONLY` `ANDROID_GOOGLE_PLAY_AAB` operation to create and verify the Google Play AAB.
6. Following verified AAB creation, proceed to the separately authorized Google Play Internal Testing upload.
