# KnownFirst Current Work

## Last updated

2026-07-29

## Repository

- Repository: https://github.com/Tachiguro/KnownFirst.git
- Only local project folder: `C:\Dev\KnownFirst` (see [ADR-0007](decisions/ADR-0007-single-canonical-working-directory.md))
- Active rule: use the single folder; create no worktree without explicit user approval.
- Only one writing agent may operate at a time.

## Verified master baseline

- Master commit: `cdbb681ba1d878208cadb640b87d5f06299abe32` (PR #34 merge commit)
- Source-controlled application identity: `1.0.0-beta.12` (build 12)
- Active database schema: SQLite `PRAGMA user_version` 7 (unchanged)
- Schema 8 status: dormant (schema 8 migration engine and preparation foundations merged, but not activated for normal database initialization)
- Supported platforms: Android (Google Play Internal Testing) and Windows development/verification. iOS and Mac Catalyst remain removed.
- Solution: `KnownFirst.slnx`

## Active local engineering package

- Branch: `feature/windows-gui-test-launcher-v1`
- Implementation commit: `7704118dde53cd69743b2c0c14ac5a5a497f3dbc` (before documentation update)
- Status: local verified branch; implementation complete.
- Summary: GUI test launcher contracts, profile isolation, PowerShell scenario runners (`scripts/knownfirst.ps1`, `run-scenario-startup-smoke.ps1`, `scenarios.json`), and UI contract tests (`UiWorkflowContractTests.cs`).

## Completed validation (StartupSmoke)

- Real Windows PowerShell 5.1 `StartupSmoke` GUI test executed and passed (`PASS`).
- Verified test outcomes:
  - Window observed = `True`
  - Startup event observed = `True`
  - KnownFirst process remained alive for 12 seconds
  - GUI Test Result: `Passed`
- Evidence log: `artifacts/launcher-logs/GuiTest-StartupSmoke-20260729-185521.log`
- Run directory: `artifacts/gui-tests/windows/runs/20260729T185521Z-StartupSmoke`
- Interference: No keyboard, mouse, or window focus interference occurred.

## Current blocker or pending validation

- No implementation or validation blocker remains.

## Exact next action

- Create single commit for this documentation update on `feature/windows-gui-test-launcher-v1`.
- Push `feature/windows-gui-test-launcher-v1` to `origin`.
- Create Pull Request against `master`.
- Await human review and explicit merge approval.

## Concise new-chat handoff

- Master baseline is `cdbb681ba1d878208cadb640b87d5f06299abe32` (PR #34 merged).
- Active branch `feature/windows-gui-test-launcher-v1` (`7704118`) verified via real `StartupSmoke` run (`PASS`).
- Next steps: commit documentation update, push branch to origin, open PR, and request human review.
