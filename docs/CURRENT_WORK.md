# KnownFirst Current Work

## Last updated

2026-07-30

## Repository

- Repository: https://github.com/Tachiguro/KnownFirst.git
- Only local project folder: `C:\Dev\KnownFirst` (see [ADR-0007](decisions/ADR-0007-single-canonical-working-directory.md))
- Active rule: use the single folder; create no worktree without explicit user approval.
- Only one writing agent may operate at a time.

## Verified master baseline

- Master commit: `ad6f14567c823acc5e18a1024794c0ea07916002` (PR #37 merge commit)
- Source-controlled application identity: `1.0.0-beta.12` (build 12)
- Confirmed distribution: `1.0.0-beta.12` / build 12 was distributed via Google Play Internal Testing and user-tested (installed app displayed `cfbaee6a` / `DIRTY`; exact source commit unverified).
- Active database schema: SQLite `PRAGMA user_version` 7 (unchanged)
- Schema 8 status: dormant (schema 8 migration engine, archive v2 DTOs, and preparation foundations merged via PRs #31, #32, #33, but not activated for normal database initialization)
- Supported platforms: Android (Google Play Internal Testing) and Windows development/verification. iOS and Mac Catalyst remain removed.
- Solution: `KnownFirst.slnx`

## Completed packages on master

- **Windows GUI StartupSmoke launcher package (PR #35 merged)**: added `-Action GuiTest` launcher entry point and profile isolation under `artifacts/`.
- **New-Chat Bootstrap Protocol package (PR #36 merged)**: permanent dynamic new-chat bootstrap governance in `docs/NEW_CHAT_BOOTSTRAP.md`.
- **Google Play Packaging Safeguards package (PR #37 merged)**: hardened `scripts/publish-google-play-bundle.ps1` with cross-process lock, warning escalation, candidate ownership, and sidecar verification.

## Active local documentation package

- Branch: `docs/reconcile-beta12-tested-next-merge-work`
- Purpose: reconcile durable project state, changelog, backlog, roadmap, beta testing docs, and release records with confirmed Beta 12 distribution and current master baseline.
- Current phase: documentation implementation in progress.

## Current blocker or pending validation

- No implementation or validation blocker remains.
- Populated-target archive import is not implemented; current import refuses populated installations.
- The abandoned later AAB attempt is deferred to `KF-RELEASE-001` (to be revalidated before the next genuine release); no current packaging task is active.

## Exact next action

- Complete review, commit, push, PR, and manual user merge of `docs/reconcile-beta12-tested-next-merge-work`.
- After synchronization, begin `PLAN_ONLY` phase for **KF-MEANING-001 Slice 4** (direction-specific answer assignments and progress replay).
- The merge writer must not be implemented before Meaning Slices 4 and 5, Schema-8 activation, and MergePreflight adaptation are complete.

## Concise new-chat handoff

- Master baseline is `ad6f14567c823acc5e18a1024794c0ea07916002` (PR #37 merged).
- Beta 12 / build 12 was distributed via Google Play Internal Testing and user-tested.
- Active database schema is 7; Schema 8 is dormant.
- Populated-target archive merge is not implemented.
- Next task after this documentation package is merged and synchronized: **KF-MEANING-001 Slice 4 PLAN_ONLY**.
- No Beta 13 or active packaging task is in progress.
