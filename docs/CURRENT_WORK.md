# KnownFirst Current Work

## Last updated

2026-07-23

## Repository

- Repository: https://github.com/Tachiguro/KnownFirst.git
- Only local project folder: C:\Dev\KnownFirst
- Active rule: use the single folder; create no worktree without explicit user approval.
- Only one writing agent may operate at a time.

## Stable baseline

- Stable master baseline: 766f3c1e02097c058d445d881dd3d6aa0bddb881
- App version: 1.0.0-beta.9 (code 9)
- Database schema: SQLite PRAGMA user_version 7
- Supported platforms: Android (Google Play Internal Testing) and Windows development/verification. iOS and Mac Catalyst have been deliberately removed from the application targets.
- Solution: KnownFirst.slnx
- Google Play: Provisional Beta 9 AAB generated locally; Beta 8 local retained artifact is currently missing and must be reconstructed from tag after merge.

## Current branch

- Branch: build/versioning-policy-beta-9
- Base: 766f3c1e02097c058d445d881dd3d6aa0bddb881

## Ongoing task

Establish coherent version identity and governance rules across KnownFirst for Beta 9.

## Current state

- PR #14 (Wikipedia fallback user-readiness) is merged to master (`766f3c1e02097c058d445d881dd3d6aa0bddb881`). Wikipedia fallback manual testing succeeded.
- KnownFirst.csproj updated to authoritative `<KnownFirstProductVersion>1.0.0-beta.9</KnownFirstProductVersion>` and `<KnownFirstBuildNumber>9</KnownFirstBuildNumber>`.
- Cross-platform build identity formatting corrected (`KnownFirst · 1.0.0-beta.9 · <Config> · Build 9 · Commit <short-sha>`).
- Binding versioning policy document created at `docs/VERSIONING.md`.
- In-app release-notes UI requirement documented for Beta 10 (explicitly deferred from Beta 9).
- Provisional Beta 9 AAB retained locally; must not be uploaded to Google Play until post-merge build task creates final AABs from synchronized master.
- Beta 8 local retained artifact is missing and must be reconstructed post-merge.

## Completed recently

- PR #14 (Wikipedia fallback user-readiness) merged to master (`766f3c1`).
- Created branch `build/versioning-policy-beta-9`.

## Process notes

- Lean delivery workflow from AGENTS.md followed: explicit file staging used, focused tests run during implementation.

## Exact implementation boundaries

- Schema version 7, no migration.
- In-app release notes UI is NOT implemented in Beta 9; documented for Beta 10.
- No AAB creation, signing, store upload, device use, emulator use, ADB, installation, or deployment occurred in this package.
- Provisional Beta 9 AAB remained untouched.

## Validation

- Complete automated test suite: 602 passed, 0 failed, 0 skipped.
- Windows Debug build: 0 warnings, 0 errors.
- Windows Release build: 0 warnings, 0 errors.
- Android Debug build (-m:1): 0 warnings, 0 errors.
- Android Release build (-m:1): 0 warnings, 0 errors, 0 AOT warnings, 0 trimming warnings, 0 source-generation warnings.

## Paused work

- Backup/Restore Phase 3 is paused until provider and persistence models are finalized.

## Planned sequence

1. Open pull request for `build/versioning-policy-beta-9`.
2. External review and manual merge decision for versioning policy package.
3. Synchronize master to merged PR HEAD.
4. Execute authorized post-merge build package: build all four configurations from synchronized master, build final Beta 9 AAB, reconstruct Beta 8 AAB from tag, retain both.
5. In-app release notes UI package targeting Beta 10.

## Known constraints and risks

- Analysis, checking, and learning prioritization remain separate processes.
- Frequency prioritizes words but never deletes them; frequency-one words remain.
- Known words apply across texts; tests use synthetic data and temporary SQLite databases only.
- AOT and trimming remain enabled; no reflection fallback is permitted.
- Apple support is intentionally absent from active project targets.
- Provisional Beta 9 AAB must not be uploaded until post-merge build package completes.

## Relevant files

- AGENTS.md
- KnownFirst.csproj
- Services/Diagnostics/BuildIdentityService.cs
- Services/Diagnostics/BuildIdentity.cs
- KnownFirst.Tests/Services/Diagnostics/BuildIdentityServiceTests.cs
- KnownFirst.Tests/UiWorkflowContractTests.cs
- docs/VERSIONING.md
- docs/BETA_TESTING.md
- docs/INDEX.md
- docs/CURRENT_WORK.md
- CHANGELOG.md

## Next exact action

- Review pull request for `build/versioning-policy-beta-9` and decide on manual merge.

## New-chat handoff

"Read AGENTS.md, docs/INDEX.md and docs/CURRENT_WORK.md completely. Follow the reading order defined there. Verify the branch, HEAD, Git status, and registered worktrees. Then proceed exclusively with the task described under 'Next exact action'."
