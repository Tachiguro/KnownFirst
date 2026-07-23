# KnownFirst Current Work

## Last updated

2026-07-23

## Repository

- Repository: https://github.com/Tachiguro/KnownFirst.git
- Only local project folder: C:\Dev\KnownFirst
- Active rule: use the single folder; create no worktree without explicit user approval.
- Only one writing agent may operate at a time.

## Stable baseline

- Stable master baseline: 639618ade38f3a252705085433c1cf6d36598806
- App version: 1.0.0-beta.8 (code 8)
- Database schema: SQLite PRAGMA user_version 7
- Supported platforms: Android (Google Play Internal Testing) and Windows development/verification. iOS and Mac Catalyst have been deliberately removed from the application targets.
- Solution: KnownFirst.slnx
- Google Play: Beta 8 is retained locally and the current channel is internal testing.

## Current branch

- Branch: feature/wikipedia-fallback-orchestration
- Base: 639618ade38f3a252705085433c1cf6d36598806
- Current branch head is intentionally not embedded because editing this document changes the commit SHA. Verify the current tip with `git rev-parse HEAD` and the PR head on GitHub.
- Pull Request #11 URL: https://github.com/Tachiguro/KnownFirst/pull/11
- PR state: open and unmerged
- Confirmation: PR #11 is not part of master.
- GitHub has no repository status checks for this head, so evidence is strictly local.

## Active task

- Finalize Wikipedia fallback orchestration pull request.

## Completed recently

- Created binding architecture and initiative plan `docs/plans/structured-vocabulary-import-and-sense-learning.md`.
- Wikipedia fallback behind Wiktionary is implemented on PR #11.
- Added comprehensive regression tests for routing and preservation of relations.
- Strengthened cache isolation tests for fallback orchestration.

## Process notes

- A process note: `git add .` was mistakenly used in earlier passes and must not be repeated. Stage every file explicitly.

## Exact implementation boundaries

- Wikipedia fallback behind primary Wiktionary
- Cache isolation strictly implemented
- Schema version 7, no migration
- No provider-selection UI
- No Backup/Restore continuation
- No PDF/list import
- No synchronization
- No live Wikimedia tests
- No device, emulator, ADB, or logcat work
- No APK/AAB creation
- No publish, signing, deployment, or store work

## Validation

- Focused tests (Fallback, EnrichAsync, Cache): 35 passed, 0 failed, 0 skipped.
- Complete test suite: 552 passed, 0 failed, 0 skipped.
- Windows Debug warnings and errors: 0 warnings, 0 errors.
- Android Debug warnings and errors: 0 warnings, 0 errors.
- Android Release warnings and errors: 0 warnings, 0 errors.
- AOT warnings: 0
- Trimming warnings: 0
- Source-generation warnings: 0
- git diff --check result: 0 whitespace errors
- Markdown-link validation: passed

## Paused work

- Backup/Restore Phase 3 is paused, not discarded, until the provider and persistence model for a Wikipedia fallback is stable.
- Backup foundations exist in master; no user-facing backup function is available.

## Planned sequence

1. Finalize PR #11 for Wikipedia fallback orchestration.
2. Fast-forward local master after merge.
3. Keep fallback orchestration and UI separate.
4. Keep schema version 7 until an explicit data-model decision.

## Known constraints and risks

- Analysis, checking, and learning prioritization remain separate processes.
- Frequency prioritizes words but never deletes them; frequency-one words remain.
- Known words apply across texts; tests use synthetic data and temporary SQLite databases only.
- AOT and trimming remain enabled; no reflection fallback is permitted.
- Apple support is intentionally absent from the active project targets; no Apple build or device validation is part of this repository.
- Provider output is stored in the existing lexical cache; no new durable sense-level/provider-specific application data model or UI persistence was introduced.
- UI integration is not implemented.
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
- docs/DATABASE_CONTRACT.md
- docs/architecture/wikipedia-json-client.md
- docs/architecture/wikipedia-lookup-provider.md
- docs/architecture/backup-format-v1.md
- docs/plans/backup-restore-v1-implementation-plan.md
- docs/handoffs/2026-07-22-beta-8-release.md
- docs/handoffs/2026-07-23-wikipedia-fallback-orchestration.md
- KnownFirst.slnx
- KnownFirst.csproj
- Services/Lexical/Wikipedia/WikipediaLookupProvider.cs
- Services/Lexical/LexicalEnrichmentService.cs
- KnownFirst.Tests/LexicalEnrichmentRoutingTests.cs
- KnownFirst.Tests/StudyWorkflowServiceTests.cs

## Next exact action

1. Review Pull Request #11.
2. Merge it manually only after explicit user approval.
3. After merge, fast-forward local master in a separate controlled step.

## New-chat handoff

"Read AGENTS.md, docs/INDEX.md and docs/CURRENT_WORK.md completely. Follow the reading order defined there. Verify the branch, HEAD, Git status, and registered worktrees. Then proceed exclusively with the task described under 'Next exact action'."
