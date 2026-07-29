# KnownFirst Current Work

## Last updated

2026-07-29

## Repository

- Repository: https://github.com/Tachiguro/KnownFirst.git
- Only local project folder: `C:\Dev\KnownFirst` (see [ADR-0007](decisions/ADR-0007-single-canonical-working-directory.md))
- Active rule: use the single folder; create no worktree without explicit user approval.
- Only one writing agent may operate at a time.

## Verified master baseline

- Master commit: `b405aa49001538ac60f45dc9697d9308e48e9eb2` (PR #33 merge commit)
- Source-controlled application identity: `1.0.0-beta.12` (build 12)
- Active database schema: SQLite `PRAGMA user_version` 7 (unchanged)
- Schema 8 status: dormant (schema 8 migration engine and preparation foundations merged, but not activated for normal database initialization)
- Supported platforms: Android (Google Play Internal Testing) and Windows development/verification. iOS and Mac Catalyst remain removed.
- Solution: `KnownFirst.slnx`

## Pending local engineering package

- Branch: `feature/windows-gui-test-launcher-v1`
- Commit: `7704118dde53cd69743b2c0c14ac5a5a497f3dbc`
- Status: local unmerged branch; no push or pull request completed.
- Summary: GUI test launcher contracts, profile isolation, and PowerShell script updates locally committed.

## Current blocker or pending validation

- Decisive real Windows PowerShell 5.1 `StartupSmoke` execution remains pending.

## Exact next action

- Under a separately authorized prompt, switch safely to `feature/windows-gui-test-launcher-v1`.
- Execute exactly one real `StartupSmoke` validation.
- Do not automatically fix, commit, push, or create a pull request as part of that validation step.

## Concise new-chat handoff

- Stable master baseline is `b405aa49001538ac60f45dc9697d9308e48e9eb2` (PR #33 merged).
- Active database schema is 7 (Schema 8 remains dormant).
- Local branch `feature/windows-gui-test-launcher-v1` (`7704118`) awaits real PowerShell `StartupSmoke` validation.
