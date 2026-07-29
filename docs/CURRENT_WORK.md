# KnownFirst Current Work

## Last updated

2026-07-29

## Repository

- Repository: https://github.com/Tachiguro/KnownFirst.git
- Only local project folder: `C:\Dev\KnownFirst` (see [ADR-0007](decisions/ADR-0007-single-canonical-working-directory.md))
- Active rule: use the single folder; create no worktree without explicit user approval.
- Only one writing agent may operate at a time.

## Verified master baseline

- Master commit: `caf4221b88d25b7fa00ba01bada3b5f6e441fe2e` (PR #35 merge commit)
- Source-controlled application identity: `1.0.0-beta.12` (build 12)
- Active database schema: SQLite `PRAGMA user_version` 7 (unchanged)
- Schema 8 status: dormant (schema 8 migration engine and preparation foundations merged, but not activated for normal database initialization)
- Supported platforms: Android (Google Play Internal Testing) and Windows development/verification. iOS and Mac Catalyst remain removed.
- Solution: `KnownFirst.slnx`

## Completed packages on master

- **Windows GUI StartupSmoke launcher package (PR #35 merged)**: added `-Action GuiTest` launcher entry point, `StartupSmoke` scenario runner (`scripts/gui-tests/windows/run-scenario-startup-smoke.ps1`), scenario configuration (`scenarios.json`), disposable profile isolation under `artifacts/`, contract tests (`UiWorkflowContractTests.cs`), and testing documentation (`docs/TESTING.md`).
- **Verified StartupSmoke test run**:
  - Window observed = `True`
  - Startup event observed = `True`
  - KnownFirst process remained alive for 12 seconds
  - GUI Test Result: `Passed`
  - Evidence log: `artifacts/launcher-logs/GuiTest-StartupSmoke-20260729-185521.log`
  - Run directory: `artifacts/gui-tests/windows/runs/20260729T185521Z-StartupSmoke`
  - No keyboard, mouse, or foreground-window interference occurred.

## Active local engineering package

- Branch: `docs/new-chat-bootstrap-protocol`
- Purpose: permanent dynamic new-chat bootstrap protocol (`docs/NEW_CHAT_BOOTSTRAP.md`) and governance updates (`AGENTS.md`, `docs/PROMPT_AND_TASK_ROUTING.md`, `docs/INDEX.md`).
- Current phase: documentation implementation completed locally; uncommitted and unpushed.

## Current blocker or pending validation

- No implementation or validation blocker remains.

## Exact next action

- Perform `REVIEW_ONLY` review of uncommitted documentation changes on `docs/new-chat-bootstrap-protocol`.
- Commit documentation update on `docs/new-chat-bootstrap-protocol`.
- Push `docs/new-chat-bootstrap-protocol` to `origin`.
- Open Pull Request against `master`.
- Perform final review and await manual user merge on GitHub.

## Concise new-chat handoff

- Master baseline is `caf4221b88d25b7fa00ba01bada3b5f6e441fe2e` (PR #35 merged).
- Active branch `docs/new-chat-bootstrap-protocol` contains the new dynamic bootstrap protocol (`docs/NEW_CHAT_BOOTSTRAP.md`).
- Next steps: `REVIEW_ONLY`, commit, push to origin, open PR, review, and request manual user merge on GitHub.
