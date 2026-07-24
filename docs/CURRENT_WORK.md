# KnownFirst Current Work

## Last updated

2026-07-24

## Repository

- Repository: https://github.com/Tachiguro/KnownFirst.git
- Only local project folder: C:\Dev\KnownFirst (see [ADR-0007](decisions/ADR-0007-single-canonical-working-directory.md))
- Active rule: use the single folder; create no worktree without explicit user approval.
- Only one writing agent may operate at a time.

## Stable baseline

- Stable master baseline: 28f8a74637b2b81d55ed39411922d2ac1decda75 (PR #15 merge commit)
- App version: 1.0.0-beta.9 (code 9)
- Database schema: SQLite PRAGMA user_version 7
- Supported platforms: Android (Google Play Internal Testing) and Windows development/verification. iOS and Mac Catalyst have been deliberately removed from the application targets.
- Solution: KnownFirst.slnx

## Current branch

- Branch: docs/task-based-documentation-routing (Pull Request #16 open)
- Base: 28f8a74637b2b81d55ed39411922d2ac1decda75

## Ongoing task

Restructure and rationalize KnownFirst documentation so coding agents read only task-relevant documentation.

## Current state

- PR #15 (Versioning policy for Beta 9) is merged to master (`28f8a74637b2b81d55ed39411922d2ac1decda75`).
- Active work is on branch `docs/task-based-documentation-routing` under open Pull Request #16.
- Universal repository instructions in [AGENTS.md](../AGENTS.md) streamlined.
- Task-based documentation router rebuilt in [docs/INDEX.md](INDEX.md) using only existing document paths.
- Delivery and validation workflow updated in [docs/AGENT_WORKFLOW.md](AGENT_WORKFLOW.md) with corrected conventional commit prefixes (`feat:`, `fix:`, `docs:`, etc.).
- Dedicated build and release guide created in [docs/BUILD_AND_RELEASE.md](BUILD_AND_RELEASE.md).
- Single canonical working directory adopted in [ADR-0007](decisions/ADR-0007-single-canonical-working-directory.md); ADR-0006 marked Superseded.

## Completed recently

- PR #15 (build/versioning-policy-beta-9) merged to master (`28f8a74`).
- Documentation refactoring and task-based routing package created and submitted as PR #16.

## Process notes

- Followed single worktree rule (`C:\Dev\KnownFirst`).
- Documentation-only package; no production code, test, project, or schema changes made.

## Exact implementation boundaries

- Schema version 7, no migration.
- In-app release notes UI is NOT implemented in Beta 9; documented for Beta 10.
- No build, test execution, packaging, signing, store upload, ADB, or device operations were performed.

## Validation

- Clean working tree verification (`git status --short`).
- Repository-wide inline relative Markdown link audit completed clean (0 broken links across tracked `.md` files).
- `git diff --check` clean.

## Paused work

- Backup/Restore Phase 3 is paused until provider and persistence models are finalized.

## Planned sequence

1. Review Pull Request #16 for `docs/task-based-documentation-routing` and decide on manual merge.
2. Synchronize local master to merged PR HEAD.
3. Post-merge release outputs (Windows/Android builds, Beta 9 AAB) occur only upon a separate explicit user request.
4. Future milestones: Sense-level data-model decision, Backup/Restore Phase 3 continuation, In-app release notes UI (Beta 10).

## Known constraints and risks

- Analysis, checking, and learning prioritization remain separate processes.
- Frequency prioritizes words but never deletes them; frequency-one words remain.
- Known words apply across texts; tests use synthetic data and temporary SQLite databases only.
- AOT and trimming remain enabled; no reflection fallback is permitted.
- Apple support is intentionally absent from active project targets.

## Relevant files

- AGENTS.md
- docs/INDEX.md
- docs/AGENT_WORKFLOW.md
- docs/BUILD_AND_RELEASE.md
- docs/CURRENT_WORK.md
- docs/PROJECT_STATE.md
- docs/ROADMAP.md
- docs/decisions/ADR-0007-single-canonical-working-directory.md
- docs/decisions/ADR-0006-git-worktrees-for-isolated-development.md
- docs/decisions/README.md

## Next exact action

- Review Pull Request #16 for `docs/task-based-documentation-routing` and decide on manual merge.

## New-chat handoff

"Read AGENTS.md, docs/INDEX.md and docs/CURRENT_WORK.md completely. Follow the reading order defined there. Verify the branch, HEAD, Git status, and registered worktrees. Then proceed exclusively with the task described under 'Next exact action'."
