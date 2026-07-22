# KnownFirst Current Work

## Last updated

2026-07-23 00:00:00 +02:00

## Repository

- Repository: https://github.com/Tachiguro/KnownFirst.git
- Only local project folder: C:\Dev\KnownFirst
- Active rule: use the single folder; create no worktree without explicit user approval.
- Only one writing agent may operate at a time.

## Stable baseline

- Stable master baseline: 2f0d41aeab7e2fc4e50a668ebfcfc410270a56b6
- App version: 1.0.0-beta.8 (code 8)
- Database schema: SQLite PRAGMA user_version 7
- Supported platforms: Android (Google Play Internal Testing) and Windows development/verification. iOS and Mac Catalyst have been deliberately removed from the application targets.
- Solution: KnownFirst.slnx
- Google Play: Beta 8 is retained locally and the current channel is internal testing.

## Current branch

- Branch: feature/wikipedia-lookup-provider
- Base: 2f0d41aeab7e2fc4e50a668ebfcfc410270a56b6
- Always verify the current tip with git rev-parse HEAD; this handoff does not embed a self-referential immutable HEAD value.

## Active task

- Review and merge Pull Request #8 after final validation.

## Completed recently

- Implemented the WikipediaLookupProvider which maps WikipediaApiClient results to LexicalResult.
- Registered WikipediaLookupProvider and WikipediaApiClient via Dependency Injection.
- Handled disambiguation, rate-limits, operation cancellations, and not-found mappings.
- Validated integration with LexicalLookupProviderResolver.
- Verified test suite and AOT/Trimming Android Release bounds.

## Validation

- Focused tests (Wikipedia, Resolver, Enrichment): [pending] passed, [pending] failed, [pending] skipped.
- Full test suite: [pending] passed, [pending] failed, [pending] skipped, duration [pending] s.
- Windows Debug build: [pending] warnings, [pending] errors.
- Android Debug build: [pending] warnings, [pending] errors.
- Android Release build: [pending] warnings, [pending] errors; AOT and trimming executed successfully (single-threaded MSBuild).
- No live Wikipedia request, device action, ADB, APK installation, publish, database migration, cache integration, or backup change was performed.

## Paused work

- Backup/Restore Phase 3 is paused, not discarded, until the provider and persistence model for a Wikipedia fallback is stable.
- Backup foundations exist in master; no user-facing backup function is available.

## Planned sequence

1. Review and merge Pull Request #8.
2. Fast-forward local master after merge.
3. Create a separate documentation-only branch for structured vocabulary/PDF import and sense-level learning.
4. In that documentation package, separate: decided requirements, open design questions, deferred ideas, data-model impact, milestones, acceptance criteria.
5. The planned document path is: docs/plans/structured-vocabulary-import-and-sense-learning.md.
6. After that documentation PR is merged, continue with the separate Wikipedia fallback orchestration branch.
7. Do not combine fallback orchestration and UI.
8. Keep schema version 7 until a later explicit data-model decision.

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

1. Review and merge Pull Request #8.
2. Fast-forward local master after merge.
3. Create a separate documentation-only branch for structured vocabulary/PDF import and sense-level learning.
4. Separate requirements, design, impact, and milestones in the new plan.
5. The planned document path is: docs/plans/structured-vocabulary-import-and-sense-learning.md.
6. Do not implement Wikipedia fallback orchestration yet.

## New-chat handoff

"Read AGENTS.md, docs/INDEX.md and docs/CURRENT_WORK.md completely. Follow the reading order defined there. Verify the branch, HEAD, Git status, and registered worktrees. Then proceed exclusively with the task described under 'Next exact action'."
