# KnownFirst Current Work

## Last updated

2026-07-23 01:55:20 +02:00

## Repository

- Repository: https://github.com/Tachiguro/KnownFirst.git
- Only local project folder: C:\Dev\KnownFirst
- Active rule: use the single folder; create no worktree without explicit user approval.
- Only one writing agent may operate at a time.

## Stable baseline

- Stable master baseline: 573c2ece4e0b0520821f1812e89f03ee3760a568
- App version: 1.0.0-beta.8 (code 8)
- Database schema: SQLite PRAGMA user_version 7
- Supported platforms: Android (Google Play Internal Testing) and Windows development/verification. iOS and Mac Catalyst have been deliberately removed from the application targets.
- Solution: KnownFirst.slnx
- Google Play: Beta 8 is retained locally and the current channel is internal testing.

## Current branch

- Branch: master
- Base: 573c2ece4e0b0520821f1812e89f03ee3760a568
- Always verify the current tip with git rev-parse HEAD; this handoff does not embed a self-referential immutable HEAD value.

## Active task

- Create a separate documentation-only branch for structured vocabulary/PDF import and sense-level learning.

## Completed recently

- Implemented the WikipediaLookupProvider which maps WikipediaApiClient results to LexicalResult.
- Registered WikipediaLookupProvider and WikipediaApiClient via Dependency Injection.
- Handled disambiguation, rate-limits, operation cancellations, and not-found mappings.
- Validated integration with LexicalLookupProviderResolver.
- Verified test suite and AOT/Trimming Android Release bounds.
- Resolved all MSTest analyzers warnings (MSTEST0032, MSTEST0037).
- Merged Pull Request #8 via `gh` and fast-forwarded local `master`.

## Validation

- Focused tests (Wikipedia, Resolver, Enrichment): [passed] passed, [0] failed, [0] skipped.
- Full test suite: [534] passed, [0] failed, [0] skipped, duration [28] s.
- Windows Debug build: [0] warnings, [0] errors.
- Android Debug build: [0] warnings, [0] errors.
- Android Release build: [0] warnings, [0] errors; AOT and trimming executed successfully (single-threaded MSBuild).
- No live Wikipedia request, device action, ADB, APK installation, publish, database migration, cache integration, or backup change was performed.

## Paused work

- Backup/Restore Phase 3 is paused, not discarded, until the provider and persistence model for a Wikipedia fallback is stable.
- Backup foundations exist in master; no user-facing backup function is available.

## Planned sequence

1. Create a separate documentation-only branch for structured vocabulary/PDF import and sense-level learning.
2. In that documentation package, separate: decided requirements, open design questions, deferred ideas, data-model impact, milestones, acceptance criteria.
3. The planned document path is: docs/plans/structured-vocabulary-import-and-sense-learning.md.
4. After that documentation PR is merged, continue with the separate Wikipedia fallback orchestration branch.
5. Do not combine fallback orchestration and UI.
6. Keep schema version 7 until a later explicit data-model decision.

## Known constraints and risks

- Analysis, checking, and learning prioritization remain separate processes.
- Frequency prioritizes words but never deletes them; frequency-one words remain.
- Known words apply across texts; tests use synthetic data and temporary SQLite databases only.
- AOT and trimming remain enabled; no reflection fallback is permitted.
- Apple support is intentionally absent from the active project targets; no Apple build or device validation is part of this repository.
- WikipediaLookupProvider maps low-level API objects to domain objects but doesn't persist data yet.
- Wiktionary fallback is not implemented.
- UI integration is not implemented.
- Cache integration and database persistence for Wikipedia are not implemented.
- Backup/Restore is not user-available.
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
- KnownFirst.slnx
- KnownFirst.csproj
- Services/Lexical/Wikipedia/WikipediaLookupProvider.cs
- KnownFirst.Tests/Services/Lexical/Wikipedia/WikipediaLookupProviderTests.cs

## Next exact action

1. Create a separate documentation-only branch for structured vocabulary/PDF import and sense-level learning.
2. Separate requirements, design, impact, and milestones in the new plan.
3. The planned document path is: docs/plans/structured-vocabulary-import-and-sense-learning.md.
4. Do not implement Wikipedia fallback orchestration yet.

## New-chat handoff

"Read AGENTS.md, docs/INDEX.md and docs/CURRENT_WORK.md completely. Follow the reading order defined there. Verify the branch, HEAD, Git status, and registered worktrees. Then proceed exclusively with the task described under 'Next exact action'."
