# KnownFirst Current Work

## Last updated

2026-07-30

## Repository

- Repository: https://github.com/Tachiguro/KnownFirst.git
- Only local project folder: `C:\Dev\KnownFirst` (see [ADR-0007](decisions/ADR-0007-single-canonical-working-directory.md))
- Active rule: use the single folder; create no worktree without explicit user approval.
- Only one writing agent may operate at a time.

## Verified master baseline

- Master commit: `1fca1f90880be0aa326013e6a45009dd0473d33e` (PR #38 merged)
- Source-controlled application identity: `1.0.0-beta.12` (build 12)
- Confirmed distribution: `1.0.0-beta.12` / build 12 was distributed via Google Play Internal Testing and user-tested.
- Active database schema: SQLite `PRAGMA user_version` 7 (unchanged)
- Schema 8 status: dormant (no Schema-8 activation has occurred)
- Supported platforms: Android (Google Play Internal Testing) and Windows development/verification. iOS and Mac Catalyst remain removed.
- Solution: `KnownFirst.slnx`

## Completed packages on master

- **Windows GUI StartupSmoke launcher package (PR #35 merged)**
- **New-Chat Bootstrap Protocol package (PR #36 merged)**
- **Google Play Packaging Safeguards package (PR #37 merged)**
- **Reconcile Beta 12 and Next Merge Work (PR #38 merged)**

## Active local documentation package

- Branch: `docs/meaning-slice4-required-primary-contract`
- Purpose: Document the user decision: one deterministic primary answer Required per normal card direction; aliases and alternatives AcceptedOnly.
- Current phase: `DOCUMENT_ONLY` architecture correction.
- Note: No Slice-4 implementation has started.

## Current blocker or pending validation

- No implementation or validation blocker remains.
- Populated-target archive import is not implemented; current import refuses populated installations.
- No current packaging or release task is active.

## Exact next action

- Review this documentation package (`docs/meaning-slice4-required-primary-contract`).
- After documentation review, commit, push, PR, manual merge, and post-merge synchronization: rerun `PLAN_ONLY` for **KF-MEANING-001 Slice 4** against the corrected binding architecture.
- The merge writer must not be implemented before Meaning Slices 4 and 5, Schema-8 activation, and MergePreflight adaptation are complete.

## Concise new-chat handoff

- Master baseline is `1fca1f90880be0aa326013e6a45009dd0473d33e` (PR #38 merged).
- Beta 12 / build 12 was distributed via Google Play Internal Testing and user-tested.
- Active database schema is 7; Schema 8 is dormant.
- Populated-target archive merge is not implemented.
- Active branch: `docs/meaning-slice4-required-primary-contract` (DOCUMENT_ONLY architecture correction).
- Exact next action: review this documentation package.
- Following synchronization: rerun `PLAN_ONLY` for KF-MEANING-001 Slice 4.
- No Beta 13 or active packaging task is in progress.
