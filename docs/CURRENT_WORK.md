# KnownFirst Current Work

## Last updated

2026-07-23 01:55:20 +02:00

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

- Branch: docs/structured-vocabulary-sense-learning-plan
- Base: 387c8d730829e76b47ed8aa6d672758e9e1611b3
- Always verify the current tip with git rev-parse HEAD; this handoff does not embed a self-referential immutable HEAD value.

## Active task

- Review and merge the structured vocabulary and sense-learning planning PR.

## Completed recently

- PR #9 merged (`Maintenance: finalize Wikipedia provider handoff`).
- Local `master` fast-forwarded to `387c8d730829e76b47ed8aa6d672758e9e1611b3`.
- Created binding architecture and initiative plan `docs/plans/structured-vocabulary-import-and-sense-learning.md`.
- No implementation, DB migration, or dependencies added; schema version remains 7.

## Validation

- Documentation-only change.
- No tests or builds executed by design.
- `git diff --check` clean.
- All relative Markdown links validated.
- No code, project, database, UI, backup, or localization changes.
- No live Wikipedia request, device action, ADB, APK installation, publish, database migration, cache integration, or backup change was performed.

## Paused work

- Backup/Restore Phase 3 is paused, not discarded, until the provider and persistence model for a Wikipedia fallback is stable.
- Backup foundations exist in master; no user-facing backup function is available.

## Planned sequence

1. Review and merge Pull Request #9.
2. Fast-forward local master after merge.
3. Create a documentation-only branch for structured vocabulary/PDF import and sense-level learning.
4. Create: docs/plans/structured-vocabulary-import-and-sense-learning.md
5. Separate decided requirements, open design questions, deferred ideas, data-model impact, milestones, and acceptance criteria.
6. After that documentation PR is merged, create a separate branch for Wikipedia fallback orchestration.
7. Keep fallback orchestration and UI separate.
8. Keep schema version 7 until an explicit data-model decision.

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

1. Review and merge Pull Request #10.
2. Fast-forward local master after merging this documentation PR.
3. Review and decide the provisional data-model recommendation (Option B) before any schema change.
4. Resume the separate Wikipedia fallback orchestration branch.
5. Automatic Wikipedia fallback may occur only after deterministic Wiktionary NotFound.
6. Do not fallback after timeout, rate-limit, network error, ParseFailure, PermanentFailure, or cancellation.
7. Keep manual provider switching as a later UI package.
8. Keep Backup/Restore Phase 3 paused until the lexical persistence decision is confirmed.
9. Keep schema version 7 until an explicit migration work package is approved.

## New-chat handoff

"Read AGENTS.md, docs/INDEX.md and docs/CURRENT_WORK.md completely. Follow the reading order defined there. Verify the branch, HEAD, Git status, and registered worktrees. Then proceed exclusively with the task described under 'Next exact action'."
