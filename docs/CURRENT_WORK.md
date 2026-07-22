# KnownFirst Current Work

## Last updated

2026-07-22 16:00:00 +02:00

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

- Branch: `build/remove-apple-targets`
- Base: `9434f99b64f82718d4b0b0fa4dbaf607a4536aa6`
- Always verify the current tip with `git rev-parse HEAD`; this handoff does not embed a self-referential immutable HEAD value.

## Completed recently

- Removed the additional worktrees and redundant repository copy.
- Removed external diagnostic stores, root logs, and generated build output.
- Freed approximately 2.153 GiB while keeping Git clean.
- Kept only `C:\Dev\KnownFirst` and the Beta 8/Beta 7 AAB release evidence.

## Active task

- Apple platform removal is implemented on this branch. Build-configuration
  and icon checks passed; focused tests passed 7/7; the complete suite passed
  418/418; Windows Debug and Android Debug/Release builds passed with zero
  warnings and zero errors. Details and hashes are in the dated handoff.

## Paused work

- Backup/Restore Phase 3 is paused, not discarded, until the provider and persistence model for a Wikipedia fallback is stable.
- Backup foundations exist in `master`; no user-facing backup function is available.

## Planned sequence

1. Finish documentation and handoff structure.
2. Implement a Wikipedia fallback behind Wiktionary.
5. Decide required persistence and migration changes.
6. Re-check the backup format against the final provider model.
7. Resume Backup/Restore Phase 3.
8. Complete further data-safety phases.
9. Add statistics.
10. Add privacy-friendly bug reporting.
11. Prepare the public beta.

## Known constraints and risks

- Analysis, checking, and learning prioritization remain separate processes.
- Frequency prioritizes words but never deletes them; frequency-one words remain.
- Known words apply across texts; tests use synthetic data and temporary SQLite databases only.
- AOT and trimming remain enabled; no reflection fallback is permitted.
- Apple support is intentionally absent from the active project targets; no
  Apple build or device validation is part of this repository.
- Wikipedia fallback is not implemented; Backup/Restore is not user-available.
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

Implement the Wikipedia fallback behind Wiktionary on a separate feature branch
and audit the provider and persistence model before changing either contract.
Backup/Restore Phase 3 remains paused.

## New-chat handoff

“Lies AGENTS.md, docs/INDEX.md und docs/CURRENT_WORK.md vollständig. Halte dich
anschließend an die dort definierte Lesereihenfolge. Verifiziere Branch, HEAD,
Git-Status und registrierte Worktrees. Fahre danach ausschließlich mit der
unter ‚Next exact action‘ beschriebenen Aufgabe fort.”
