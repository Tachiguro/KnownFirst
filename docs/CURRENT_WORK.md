# KnownFirst Current Work

## Last updated

2026-07-22 17:33:00 +02:00

## Repository

- Repository: https://github.com/Tachiguro/KnownFirst.git
- Only local project folder: `C:\Dev\KnownFirst`
- Active rule: use the single folder; create no worktree without explicit user approval.
- Only one writing agent may operate at a time.

## Stable baseline

- Stable master baseline: `9434f99b64f82718d4b0b0fa4dbaf607a4536aa6`
- App version: `1.0.0-beta.8` (code 8)
- Database schema: SQLite `PRAGMA user_version` 7
- Supported platforms: Android (Google Play Internal Testing) and Windows development/verification. iOS and Mac Catalyst have been deliberately removed from the application targets.
- Solution: `KnownFirst.slnx`
- Google Play: Beta 8 is retained locally and the current channel is internal testing.

## Current branch

- Branch: `feature/wikipedia-json-client`
- Base: `914e48da955d1a922d8e068b7b4034233092c70b`
- Always verify the current tip with `git rev-parse HEAD`; this handoff does not embed a self-referential immutable HEAD value.

## Completed recently

- Removed the additional worktrees and redundant repository copy.
# KnownFirst Current Work

## Last updated

2026-07-22 17:33:00 +02:00

## Repository

- Repository: https://github.com/Tachiguro/KnownFirst.git
- Only local project folder: `C:\Dev\KnownFirst`
- Active rule: use the single folder; create no worktree without explicit user approval.
- Only one writing agent may operate at a time.

## Stable baseline

- Stable master baseline: `9434f99b64f82718d4b0b0fa4dbaf607a4536aa6`
- App version: `1.0.0-beta.8` (code 8)
- Database schema: SQLite `PRAGMA user_version` 7
- Supported platforms: Android (Google Play Internal Testing) and Windows development/verification. iOS and Mac Catalyst have been deliberately removed from the application targets.
- Solution: `KnownFirst.slnx`
- Google Play: Beta 8 is retained locally and the current channel is internal testing.

## Current branch

- Branch: `feature/wikipedia-json-client`
- Base: `914e48da955d1a922d8e068b7b4034233092c70b`
- Always verify the current tip with `git rev-parse HEAD`; this handoff does not embed a self-referential immutable HEAD value.

## Completed recently

- Removed the additional worktrees and redundant repository copy.
- Removed external diagnostic stores, root logs, and generated build output.
- Freed approximately 2.153 GiB while keeping Git clean.
- Kept only `C:\Dev\KnownFirst` and the Beta 8/Beta 7 AAB release evidence.

## Active task

- Implementiert einen AOT-sicheren, source-generierten Wikipedia JSON API Client.
- Vollständig lokale synthetische JSON-Fixtures und deterministische Clienttests (450 gesamt).
- Windows Debug, Android Debug und Android Release (mit AOT und Trimming) erfolgreich gebaut.
- Backup/Restore Phase 3 ist weiterhin pausiert.

## Paused work

- Backup/Restore Phase 3 is paused, not discarded, until the provider and persistence model for a Wikipedia fallback is stable.
- Backup foundations exist in `master`; no user-facing backup function is available.

## Planned sequence

1. Review and merge the current `feature/wikipedia-json-client` branch into `master`.
2. Fast-forward synchronize the local `master` branch.
3. Create a new branch for the WikipediaLookupProvider.
4. Abbilden des getesteten WikipediaApiClient in LexicalResult.
5. Noch keinen Wiktionary-Fallback und keine UI implementieren.
6. Schema-Version 7 beibehalten.
7. Re-check the backup format against the final provider model.
8. Resume Backup/Restore Phase 3.
9. Complete further data-safety phases.
10. Add statistics.
11. Add privacy-friendly bug reporting.
12. Prepare the public beta.

## Known constraints and risks

- Analysis, checking, and learning prioritization remain separate processes.
- Frequency prioritizes words but never deletes them; frequency-one words remain.
- Known words apply across texts; tests use synthetic data and temporary SQLite databases only.
- AOT and trimming remain enabled; no reflection fallback is permitted.
- Apple support is intentionally absent from the active project targets; no
  Apple build or device validation is part of this repository.
- Wikipedia fallback is not implemented; Backup/Restore is not user-available.
- Physical Android device testing is deferred to feature milestones, Beta releases, device-specific bugs, or explicit user requests, and is always a separate work package.
- Normal development, unit tests, and standard validation builds (Windows Debug, Android Debug, Android Release with AOT/Trimming) do not require a connected smartphone or routine device deployment/ADB execution.
- No `pm clear`, app uninstallation, or user data reset is permitted without explicit user authorization.
- `CURRENT_WORK.md` must be updated after every work package.
- Release AABs are local ignored evidence and are not Git-versioned.
- Linux is a later feasibility/alternative-host check; Microsoft Store and public Google Play release remain planned.

## Relevant files

- `AGENTS.md`
- `docs/INDEX.md`
- `docs/PROJECT_STATE.md`
- `docs/ROADMAP.md`
- `CHANGELOG.md`
- `docs/DATABASE_CONTRACT.md`
- `docs/architecture/backup-format-v1.md`
- `docs/plans/backup-restore-v1-implementation-plan.md`
- `docs/handoffs/2026-07-22-beta-8-release.md`
- `KnownFirst.slnx`
- `KnownFirst.csproj`

## Next exact action

1. Review and merge the current `feature/wikipedia-json-client` branch into `master`.
2. Fast-forward synchronize the local `master` after PR merge.
3. Create a new branch for `WikipediaLookupProvider`.
4. Abbilden des getesteten `WikipediaApiClient` in `LexicalResult`.
5. Noch keinen Wiktionary-Fallback und keine UI implementieren.
6. Schema-Version 7 beibehalten, sofern der Provider-Audit nichts anderes beweist.

## New-chat handoff

“Read AGENTS.md, docs/INDEX.md and docs/CURRENT_WORK.md completely. Follow the reading order defined there. Verify the branch, HEAD, Git status, and registered worktrees. Then proceed exclusively with the task described under 'Next exact action'.”
