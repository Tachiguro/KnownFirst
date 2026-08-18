# KnownFirst Current Work

## Last updated

2026-08-18 (Documentation/governance reconciliation through PR #131; canonical `master` synchronized to `00adbe02c2cf10c6fa3ddd7db59cc748c3bc8faa`; PR #131 candidate `FULL_VALIDATION` [`ValidateAll-20260818-191908.log`] passed 2039/2039 tests, Windows/Android Debug/Release, 0 failures, exit code 0; final Beta-13 Release AAB remains NOT CREATED; subsequent Google Play Internal Testing upload/distribution NOT PERFORMED; Closed Testing, Open Testing, Production, and public rollout remain unauthorized).

## Repository and Worktree Governance

- Repository: https://github.com/Tachiguro/KnownFirst.git
- Canonical local working directory: Exactly one canonical local checkout and one normal worktree per environment (defaulting to `C:\Dev\KnownFirst`, see [ADR-0007](decisions/ADR-0007-single-canonical-working-directory.md)).
- Single writer: Only one writing agent operates at a time in the repository.
- Single worktree: Additional worktrees or repository copies require explicit user authorization.

## Mandatory Documentation Governance

Every repository-writing package now requires a mandatory `DOCUMENT_ONLY` reconciliation phase before `COMMIT_ONLY`. The normal code/behavior package lifecycle is: `PLAN_ONLY` → `IMPLEMENT` → focused verification → `REVIEW_ONLY` → mandatory `DOCUMENT_ONLY` → `COMMIT_ONLY` → exact-candidate-HEAD `FULL_VALIDATION` → `PUSH_ONLY` → `PR_ONLY` → manual user merge → `POST_MERGE_SYNC_ONLY`. Documentation-only packages omit `IMPLEMENT`. See [docs/PROMPT_AND_TASK_ROUTING.md](PROMPT_AND_TASK_ROUTING.md) §I and [docs/AGENT_WORKFLOW.md](AGENT_WORKFLOW.md) for the binding rules.

## Active Work Package

- **Active work package:** Beta-13 Pre-AAB release-readiness and packaging completion.
- **Package provenance & live state:** Authoritative live checkout/branch, worktree state, and operational task positions are discovered directly from Git/GitHub, with `master` as the canonical branch.
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
  - PR #122 (`docs: reconcile beta13 pre-aab state after pr 121`): Reconciled release documentation following PR #121; merged to `e23c40335a387b71057a43789b1abd960a0f1176`. `POST_MERGE_SYNC_ONLY` completed.
  - PR #123 (`docs: normalize beta13 pre-aab state after pr 122`): Normalized release records and Gate 6/7 durable documentation; merged to `38814434bbb2f4ba17e493503f6483e41543602a`. `POST_MERGE_SYNC_ONLY` completed.
  - PR #124 (`fix: clean transient android publish directory before pre-publish check`): Corrected canonical GooglePlayBundle publisher to remove transient `publish/` output between `dotnet clean` and pre-candidate check with contract test coverage, resolving pre-publish failure from surviving signed AABs; merged to `c38f16a025d471a40087fbed768a05e965aabd2d`. `POST_MERGE_SYNC_ONLY` completed.
  - PR #125 (`docs: reconcile beta13 pre-aab state after pr 124`): Reconciled durable release documentation following PR #124 merge; merged to `be4deb8f1de279aa95b7859fbc4e0bf33a997148`. `POST_MERGE_SYNC_ONLY` completed.
  - PR #126 (`fix: scope post-publish aab candidate discovery to publish directory`): Hardened post-publish AAB candidate discovery to look only in the `publish/` directory, eliminating false candidates from build output locations (infrastructure fix, no visible UI change); merged to `19e0323d5674bb0524cdda15c8bd43e035cc0981`. `POST_MERGE_SYNC_ONLY` completed.
  - PR #127 (`fix: harden aab signature verification policy`): Tightened AAB signature verification policy in packaging scripts (infrastructure fix, no visible UI change); merged to `28674c5a1bd7aad91a1f35f103edd96810a0f8e5`. `POST_MERGE_SYNC_ONLY` completed.
  - PR #128 (`test: harden text analyzer characterization coverage`): Added characterization test coverage for the German text analyzer to protect subsequent text-analysis refactoring targets (test-only, no production behavior change); merged to `8f65bc50528e6b451342b284098e4a7335042b1f`. `POST_MERGE_SYNC_ONLY` completed.
  - PR #129 (`feat: add enhanced term recognition setting`): Added persisted application-level `EnhancedTermRecognitionEnabled` foundation, default OFF; internal seam only, no visible Settings UI control; merged to `51a089811e09e3fd2194e54949f40cadb3ec110a`. `POST_MERGE_SYNC_ONLY` completed.
  - PR #130 (`feat: add German term provenance and lexicon seam`): Added `IGermanLexicon` Core seam and German term provenance support; deterministic/offline lexical evidence foundation; production lexicon wiring and visible Settings UI remain deferred; merged to `1f9697ce15745c5630d5184e706bcf2282057306`. `POST_MERGE_SYNC_ONLY` completed.
  - PR #131 (`feat: add conservative German compound decomposition`): Added conservative two-component German compound decomposition through the `IGermanLexicon` opt-in seam; whole source compound remains Direct; derived components require exactly one unambiguous fully lexicon-backed split; ambiguous/unsupported cases fail closed; production `TextReviewService` wiring and visible Settings UI remain deferred; merged to `00adbe02c2cf10c6fa3ddd7db59cc748c3bc8faa`. `POST_MERGE_SYNC_ONLY` completed.

## Verified Baseline & Release Boundaries

- **Canonical `master` baseline:** Synchronized `master` at `00adbe02c2cf10c6fa3ddd7db59cc748c3bc8faa` (PR #131 merge commit). Prior PR merge commits and their `POST_MERGE_SYNC_ONLY` completions are historical facts; live current master identity is always discovered dynamically from Git/GitHub.
- **Historical exact-master validation evidence:**
  - `ValidateAll-20260816-170329.log` on `f387f8a8...`: PASSED (1963/1963 automated tests, Windows Debug/Release, Android Debug/Release, 114/114 AOT, 0 prohibited release diagnostics).
  - `ValidateAll-20260816-194413.log` on `eb4a2302...`: PASSED (1963/1963 automated tests, Windows Debug/Release, Android Debug/Release, 114/114 AOT, 0 prohibited release diagnostics, 73/73 UI contract tests, 10/10 diagnostic gate tests).
  - `ValidateAll-20260816-234539.log` on `c9fc3fec...`: PASSED (1964/1964 automated tests, Windows Debug/Release, Android Debug/Release, 114/114 Native AOT, 0 prohibited release diagnostics, strict compiler warning parity active with 0 warnings / 0 errors).
  - `ValidateAll-20260817-005330.log` on `e23c403...`: PASSED (1964/1964 automated tests, Windows Debug/Release, Android Debug/Release, 114/114 Native AOT, 0 prohibited release diagnostics, strict compiler warning parity active with 0 warnings / 0 errors).
  - `ValidateAll-20260817-015455.log` on `38814434...`: PASSED (1964/1964 automated tests, Windows Debug/Release, Android Debug/Release, 114/114 Native AOT, 0 prohibited release diagnostics, strict compiler warning parity active with 0 warnings / 0 errors).
  - `ValidateAll-20260817-025247.log` on candidate `e4cd038...` (PR #124): PASSED (1966/1966 automated tests, Windows Debug/Release, Android Debug/Release, 114/114 Native AOT, 0 prohibited release diagnostics, strict compiler warning parity active with 0 warnings / 0 errors, 20/20 Android publishing contract tests).
  - `ValidateAll-20260818-191908.log` on candidate `6b4ae35b3f57029dc799102e7c4e56741d60be82` (PR #131): PASSED (2039/2039 automated tests, Windows Debug/Release, Android Debug/Release, 0 failures, 0 skipped, exit code 0).
- **Historical packaging attempts:**
  - **Initial attempt on `eb4a2302...` (2026-08-16):** Canonical `GooglePlayBundle` execution failed (`GooglePlayBundle-20260816-195953.log`, exit code 1) because the publisher's `-warnaserror` promoted 8 XML documentation compiler warnings to errors. Resolved in PR #121.
  - **Second attempt on `38814434...` (2026-08-17):** Following Gate-17 certification on `38814434...`, canonical `GooglePlayBundle` execution failed (`GooglePlayBundle-20260817-021326.log`, exit code 1) before `dotnet publish` because stale `*-Signed.aab` output survived internal `dotnet clean` under `publish/`. Classified as `CANONICAL_PACKAGING_WORKFLOW_DEFECT` and resolved in PR #124.
- **Pre-AAB Gate Status:** The earlier affirmative Gate-17 certifications and reports on `eb4a2302...`, `c9fc3fec...`, `e23c403...`, and `38814434...` are historical, superseded, or non-actionable due to contradictory packaging evidence or post-gate source changes. Gate 17 remains **NOT CERTIFIED** on the post-PR#131 lineage until documentation reconciliation completes, fresh exact-master validation passes on the live synchronized master, and a new final Pre-AAB `REVIEW_ONLY` certifies all gate items.
- **Source identity:** `1.0.0-beta.13` (build 13) merged on `master` via PR #92. Merging is not a packaging, signing, or distribution event.
- **Confirmed external distribution:** `1.0.0-beta.12` / build 12 (Google Play Internal Testing, confirmed 2026-07-30). No newer Android external distribution has occurred.
- **Database schema:** SQLite `PRAGMA user_version` 10 on `master`.
- **Supported platforms:** Android (Google Play Internal Testing) and Windows development/verification. iOS and Mac Catalyst remain removed.
- **Windows distribution packaging:** A real self-contained Windows Portable Release ZIP (`KnownFirst-1.0.0-beta.13-build13-win-x64-9e455d0.zip`) and matching SHA-256 sidecar were produced and verified on `master` at source commit `9e455d0e03494cac8e713cd4d16c66946124f852` on 2026-08-16. Clean-PC runtime execution, MSIX packaging, and external distribution remain separate unexecuted milestones.
- **Android Release APK:** A signed Android Release APK (`KnownFirst-1.0.0-beta.13-android-release.apk`, SHA-256 `53bbcb18b62927dae0af0a63d0e6a3cda6a8420c1c9517d354c638504b9ac6b6`) was produced and verified on physical hardware for manual Android validation.
- **Final Release AAB:** **BLOCKED PENDING EXACT-MASTER GATE CERTIFICATION / NOT YET CREATED**.
- **Google Play Internal Testing Upload/Distribution:** **SEPARATELY AUTHORIZED FOLLOWING VERIFIED AAB / NOT YET PERFORMED**. Closed Testing, Open Testing, Production, and public rollout remain unauthorized.

## Governed Operational Sequence & Next Actions

From the live synchronized canonical `master` containing the completed durable release reconciliation:
1. Run mandatory exact-master `FULL_VALIDATION` (`ValidateAll -Force`) on the live canonical `master`.
2. Execute final Pre-AAB `REVIEW_ONLY` to evaluate and certify all Pre-AAB gate items against [docs/BUILD_AND_RELEASE.md](BUILD_AND_RELEASE.md) §7 and §7.1 on that exact `master` HEAD.
3. Upon affirmative Gate-17 certification, execute the authorized `PACKAGE_ONLY` `ANDROID_GOOGLE_PLAY_AAB` operation to create and verify the Google Play AAB.
4. Following verified AAB creation, proceed to the separately authorized Google Play Internal Testing upload.
