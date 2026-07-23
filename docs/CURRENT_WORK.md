# KnownFirst Current Work

## Last updated

2026-07-23 15:48:00 +02:00

## Repository

- Repository: https://github.com/Tachiguro/KnownFirst.git
- Only local project folder: C:\Dev\KnownFirst
- Active rule: use the single folder; create no worktree without explicit user approval.
- Only one writing agent may operate at a time.

## Stable baseline

- Stable master baseline: 387c8d730829e76b47ed8aa6d672758e9e1611b3
- App version: 1.0.0-beta.8 (code 8)
- Database schema: SQLite PRAGMA user_version 7
- Supported platforms: Android (Google Play Internal Testing) and Windows development/verification. iOS and Mac Catalyst have been deliberately removed from the application targets.
- Solution: KnownFirst.slnx
- Google Play: Beta 8 is retained locally and the current channel is internal testing.

## Current branch

- Branch: feature/wikipedia-fallback-orchestration
- Base: 639618ade38f3a252705085433c1cf6d36598806
- Always verify the current tip with git rev-parse HEAD; this handoff does not embed a self-referential immutable HEAD value.

## Active task

- Finalize Wikipedia fallback orchestration pull request.

## Completed recently

- Created binding architecture and initiative plan `docs/plans/structured-vocabulary-import-and-sense-learning.md`.
- Implemented schema-neutral Wikipedia fallback orchestration in `LexicalEnrichmentService`.
- Added tracking provider and `WikipediaFallbackPolicy` to orchestrate fallback gracefully.
- Added comprehensive regression tests for routing and preservation of relations.

## Validation

- Tested with `dotnet test .\KnownFirst.Tests\KnownFirst.Tests.csproj -c Debug --nologo` yielding 552 passed tests.
- Re-verified fallback correctly surfaces relational surface forms and redirect depth.

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
- WikipediaLookupProvider maps low-level API objects to domain objects but doesn't persist data yet.
- Wiktionary fallback is implemented gracefully in logic.
- UI integration is not implemented.
- Cache integration and database persistence for Wikipedia are verified up to caching logic, but might not be completely surfaced until UI validation.
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
- Services/Lexical/LexicalEnrichmentService.cs
- KnownFirst.Tests/LexicalEnrichmentRoutingTests.cs
- KnownFirst.Tests/StudyWorkflowServiceTests.cs

## Next exact action

1. Fast-forward local master after PR #11 merge.

## New-chat handoff

"Read AGENTS.md, docs/INDEX.md and docs/CURRENT_WORK.md completely. Follow the reading order defined there. Verify the branch, HEAD, Git status, and registered worktrees. Then proceed exclusively with the task described under 'Next exact action'."
