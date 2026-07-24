# KnownFirst Current Work

## Last updated

2026-07-24

## Repository

- Repository: https://github.com/Tachiguro/KnownFirst.git
- Only local project folder: C:\Dev\KnownFirst (see [ADR-0007](decisions/ADR-0007-single-canonical-working-directory.md))
- Active rule: use the single folder; create no worktree without explicit user approval.
- Only one writing agent may operate at a time.

## Stable baseline

- Stable master baseline: 2211d3fb8f21b7207eb09e0699617dcfb925cc75 (PR #16 merge commit)
- App version: 1.0.0-beta.9 (code 9)
- Database schema: SQLite PRAGMA user_version 7
- Supported platforms: Android (Google Play Internal Testing) and Windows development/verification. iOS and Mac Catalyst have been deliberately removed from the application targets.
- Solution: KnownFirst.slnx

## Current branch

- Branch: docs/strict-task-execution-workflow (uncommitted diff)
- Base: 2211d3fb8f21b7207eb09e0699617dcfb925cc75

## Ongoing task

Implement the approved KnownFirst workflow-governance plan establishing strict isolation between planning, implementation, testing, documentation, builds, packaging, Git, and PR operations.

## Current state

- PR #16 (task-based documentation routing) was merged into master (`2211d3fb8f21b7207eb09e0699617dcfb925cc75`).
- Active branch is `docs/strict-task-execution-workflow` created from synchronized `master`.
- Created [docs/PROMPT_AND_TASK_ROUTING.md](PROMPT_AND_TASK_ROUTING.md) as the canonical prompt-authoring and task-isolation guide.
- Created [docs/TESTING.md](TESTING.md) as the canonical test scope guide and failure policy.
- Updated [AGENTS.md](../AGENTS.md) with universal operation mode rules and scope boundaries.
- Updated [docs/AGENT_WORKFLOW.md](AGENT_WORKFLOW.md) with the 13-phase explicit state sequence.
- Updated [docs/INDEX.md](INDEX.md) with prompt creation routing, test routing, and baseline routers.
- Updated [docs/BUILD_AND_RELEASE.md](BUILD_AND_RELEASE.md) with isolated build/package subsections and AOT scoping.
- Updated [docs/VERSIONING.md](VERSIONING.md) with the cumulative unread release notes specification and display trigger.

## Completed recently

- PR #16 (task-based documentation routing) merged to master (`2211d3f`).
- Synchronized local master to `2211d3f`.

## Process notes

- Followed single worktree rule (`C:\Dev\KnownFirst`).
- Documentation-only package; no production code, test code, project files, scripts, or localization resources were modified.
- Created exactly two new Markdown files: `docs/PROMPT_AND_TASK_ROUTING.md` and `docs/TESTING.md`.

## Exact implementation boundaries

- Schema version 7, no migration.
- In-app release notes UI is NOT implemented in code.
- No builds, test runs, packaging, signing, store uploads, ADB, emulator, or device operations were performed.
- No commit, push, PR creation, merge, or staging occurred.

## Validation

- Clean working tree verification (`git status --short`).
- Repository-wide inline relative Markdown link audit across 46 committed HEAD `.md` files and 2 untracked `.md` files (48 total in working-tree review scope; 0 broken links).
- `git diff --check` clean.

## Paused work

- Backup/Restore Phase 3 is paused until provider and persistence models are finalized.

## Planned sequence

1. User review of the uncommitted documentation diff on `docs/strict-task-execution-workflow`.
2. Explicit `COMMIT_ONLY` prompt.
3. Explicit `PUSH_ONLY` prompt.
4. Explicit `PR_ONLY` prompt.
5. Review and explicit user merge decision.
6. `SYNC_ONLY` after merge.

## Relevant files

- AGENTS.md
- docs/PROMPT_AND_TASK_ROUTING.md
- docs/TESTING.md
- docs/INDEX.md
- docs/AGENT_WORKFLOW.md
- docs/BUILD_AND_RELEASE.md
- docs/VERSIONING.md
- docs/CURRENT_WORK.md

## Next exact action

- User review of the uncommitted documentation diff on branch `docs/strict-task-execution-workflow`.

## New-chat handoff

"Read AGENTS.md, docs/PROMPT_AND_TASK_ROUTING.md, and docs/CURRENT_WORK.md completely. Follow the reading order defined there. Verify the branch, HEAD, Git status, and registered worktrees. Then proceed exclusively with the task described under 'Next exact action'."
