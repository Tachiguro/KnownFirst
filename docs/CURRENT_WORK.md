# KnownFirst Current Work

## Last updated

2026-07-25

## Repository

- Repository: https://github.com/Tachiguro/KnownFirst.git
- Only local project folder: C:\Dev\KnownFirst (see [ADR-0007](decisions/ADR-0007-single-canonical-working-directory.md))
- Active rule: use the single folder; create no worktree without explicit user approval.
- Only one writing agent may operate at a time.

## Stable baseline

- Stable master baseline: `2f3f89daabcced8fbe7133ec782808d1aa5f4038` (PR #18 merge commit)
- App version: 1.0.0-beta.10 (build 10)
- Database schema: SQLite `PRAGMA user_version` 7 (unchanged)
- Supported platforms: Android (Google Play Internal Testing) and Windows development/verification. iOS and Mac Catalyst remain removed.
- Solution: KnownFirst.slnx

## Current branch

- Branch: `docs/post-beta10-project-handoff` (documentation-only, uncommitted)
- Base: `2f3f89daabcced8fbe7133ec782808d1aa5f4038`

## Completed recently

- Portable `.kfarchive` export (native Save dialog, Windows and Android).
- Portable recovery import into an empty installation only (native Open dialog); populated targets are refused, not merged.
- One-time localized What's New notice, reopenable in a future Settings control (not yet implemented — see risks).
- Local build-launcher refinement and selective Android test-package scripting.
- Pull Request #18 merged to `master` (`2f3f89d`).

## Current known risks

- A learner with only a few available words may see what looks like the same word and question twice in one learning session. Likely cause under investigation: the legitimate one-time "Again" requeue is not visually distinguished from a first-time card. Not yet fixed.
- "Support KnownFirst" and "Report a bug" in Settings are placeholders (identical no-op handler); not gated out of Release builds.
- Deterministic GUI automation (Appium/UiAutomator2 or equivalent) is not yet implemented; GUI verification remains manual per [GUI_TEST_MATRIX.md](GUI_TEST_MATRIX.md).
- Public Google Play release is intentionally blocked; current distribution is Internal Testing only.

## Active planned sequence

1. Focused, read-only investigation and deterministic test plan for the reported duplicate-looking learning question (no fix authorized yet).
2. Functional "Support KnownFirst" and "Report a bug" controls (or explicit removal/gating decision).
3. Reopenable release notes / release-note history access from Settings.
4. Deterministic Android GUI automation feasibility and first implementation.
5. Public-release legal, privacy, and support readiness review.

## Explicit exclusions

- No merge-import or overwrite semantics for portable recovery.
- No `ReplaceAll`-style restore into a populated installation.
- No additional learning languages beyond English/German source-target scope yet.
- No public Google Play promotion yet.

## Relevant files for the next task

- [architecture/backup-format-v1.md](architecture/backup-format-v1.md)
- `Services/Study/LearningService.cs`
- `Components/Pages/Learn.razor`
- `Models/LearningModels.cs`
- `KnownFirst.Tests/StudyWorkflowServiceTests.cs`

## Next exact action

Perform a read-only, focused investigation and deterministic reproduction/test plan for the reported duplicate-looking learning question (small vocabulary pool: same word and same card direction appearing twice in one session). Trace card creation, queue building, and the "Again" requeue path; identify whether the cause is a genuine duplicate or an undistinguished legitimate repeat; propose the minimum test matrix. Do not implement a fix in this step.

## New-chat handoff

"Read AGENTS.md, docs/PROMPT_AND_TASK_ROUTING.md, docs/CURRENT_WORK.md, and docs/INDEX.md completely. Verify branch, HEAD, Git status, and registered worktrees. Follow the task-specific reading route from docs/INDEX.md. Then perform only the task under 'Next exact action'."
