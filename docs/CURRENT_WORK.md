# KnownFirst Current Work

## Last updated

2026-07-23

## Repository

- Repository: https://github.com/Tachiguro/KnownFirst.git
- Only local project folder: C:\Dev\KnownFirst
- Active rule: use the single folder; create no worktree without explicit user approval.
- Only one writing agent may operate at a time.

## Stable baseline

- Stable master baseline: d33cd80633f1ad1c25f76567136c642c419a23af
- App version: 1.0.0-beta.8 (code 8)
- Database schema: SQLite PRAGMA user_version 7
- Supported platforms: Android (Google Play Internal Testing) and Windows development/verification. iOS and Mac Catalyst have been deliberately removed from the application targets.
- Solution: KnownFirst.slnx
- Google Play: Beta 8 is retained locally and the current channel is internal testing.

## Current branch

- Branch: docs/wikipedia-fallback-post-merge-audit
- Base: d33cd80633f1ad1c25f76567136c642c419a23af
- Local master has been fast-forwarded to origin/master.

## Ongoing task

Completing the post-merge synchronization, canonical-state correction, and user-flow audit for the Wikipedia fallback feature.

## Current state

- The existing preparation UI already renders usable Wikipedia fallback definitions through the merged orchestration.
- User-ready completion remains blocked by attribution/link, license-reference, disclosure wording, and visual/manual validation gaps identified by the audit.
- Pull Request [#12](https://github.com/Tachiguro/KnownFirst/pull/12) is open and unmerged on branch `docs/wikipedia-fallback-post-merge-audit`.
- GitHub has no independent repository checks for this head, so validation evidence is local.

## Completed recently

- PR #11 (Wikipedia fallback orchestration) is merged.
- Merged feature head is 9aa4ef7bb02166bfbafdf475f4c6fa7731ce1201.
- Merge commit is d33cd80633f1ad1c25f76567136c642c419a23af.
- Feature and merge commit trees are identical.
- Created `docs/audits/2026-07-23-wikipedia-fallback-user-flow-audit.md`.

## Process notes

- A process note: `git add .` was mistakenly used in earlier passes and must not be repeated. Stage every file explicitly.

## Exact implementation boundaries

- Schema version 7, no migration.
- No production code or tests were modified in this audit package.
- No live Wikimedia requests occurred.
- No device or visual execution occurred.

## Validation

- Merged-master validation: Complete test suite (552 passed, 0 failed, 0 skipped).
- Windows Debug build: 0 warnings, 0 errors.
- Android Debug build: 0 warnings, 0 errors.
- Android Release build: 0 warnings, 0 errors.
- AOT, trimming, and source-generation warnings: 0.

## Paused work

- Backup/Restore Phase 3 is paused, not discarded, until the provider and persistence model for a Wikipedia fallback is stable.
- Backup foundations exist in master; no user-facing backup function is available.

## Planned sequence

1. Review the documentation-only pull request containing the user-flow audit.
2. Choose the bounded implementation package identified by the audit.

## Known constraints and risks

- Analysis, checking, and learning prioritization remain separate processes.
- Frequency prioritizes words but never deletes them; frequency-one words remain.
- Known words apply across texts; tests use synthetic data and temporary SQLite databases only.
- AOT and trimming remain enabled; no reflection fallback is permitted.
- Apple support is intentionally absent from the active project targets; no Apple build or device validation is part of this repository.
- Provider output is stored in the existing lexical cache; no new durable sense-level/provider-specific application data model or UI persistence was introduced.
- The existing preparation UI already renders usable Wikipedia fallback definitions through the merged orchestration. User-ready completion remains blocked by attribution/link, license-reference, disclosure wording, and visual/manual validation gaps.
- Backup/Restore is not user-available.
- Physical-device and visual validation are unverified.
- Physical Android device testing is deferred to feature milestones, Beta releases, device-specific bugs, or explicit user requests, and is always a separate work package.
- Normal development, unit tests, and standard validation builds (Windows Debug, Android Debug, Android Release with AOT/Trimming) do not require a connected smartphone or routine device deployment/ADB execution.
- No pm clear, app uninstallation, or user data reset is permitted without explicit user authorization.
- CURRENT_WORK.md must be updated after every work package.
- Release AABs are local ignored evidence and are not Git-versioned.
- Linux is a later feasibility/alternative-host check; Microsoft Store and public Google Play release remain planned.

## Relevant files

- AGENTS.md
- docs/INDEX.md
- docs/CURRENT_WORK.md
- docs/PROJECT_STATE.md
- docs/ROADMAP.md
- CHANGELOG.md
- docs/audits/2026-07-23-wikipedia-fallback-user-flow-audit.md

## Next exact action

- Review the audit PR and choose the bounded implementation package identified by the evidence.

## New-chat handoff

"Read AGENTS.md, docs/INDEX.md and docs/CURRENT_WORK.md completely. Follow the reading order defined there. Verify the branch, HEAD, Git status, and registered worktrees. Then proceed exclusively with the task described under 'Next exact action'."
