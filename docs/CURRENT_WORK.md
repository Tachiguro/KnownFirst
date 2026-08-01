# KnownFirst Current Work

## Last updated

2026-08-01

## Repository

- Repository: https://github.com/Tachiguro/KnownFirst.git
- Only local project folder: `C:\Dev\KnownFirst` (see [ADR-0007](decisions/ADR-0007-single-canonical-working-directory.md))
- Active rule: use the single folder; create no worktree without explicit user approval.
- Only one writing agent may operate at a time.

## Verified master baseline

- Master commit: `60d8f073fc4d07cdfdc83a8d404cd606c458e321` (PR #40 merged)
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
- **Meaning Slice 4 answer assignments and progress replay (PR #40 merged; full suite 1347 passed, 0 failed, 0 skipped)**

## Active local implementation package

- Branch: `feat/meaning-slice5-sense-queue`
- Purpose: Complete dormant Schema-8 Sense-addressed learning cards, frozen queue targets, continuation, and permanent-known cleanup while preserving Schema 7 behavior.
- Current phase: Meaning Slice 5 Packages 1 and 2 implementation and validation complete on the feature branch.
- Complete Slice-5 validation: 1358 passed, 0 failed, 0 skipped.
- Note: PR review, manual merge, and post-merge synchronization remain pending.

## Current blocker or pending validation

- No implementation or validation blocker remains.
- Populated-target archive import is not implemented; current import refuses populated installations.
- No current packaging or release task is active.

## Exact next action

- Review the Slice-5 pull request after creation.
- After user review: manual merge and separately authorized post-merge synchronization for **KF-MEANING-001 Slice 5**.
- The merge writer must not be implemented before Meaning Slices 4 and 5, Schema-8 activation, and MergePreflight adaptation are complete.

## Concise new-chat handoff

- Master baseline is `60d8f073fc4d07cdfdc83a8d404cd606c458e321` (PR #40 merged).
- Beta 12 / build 12 was distributed via Google Play Internal Testing and user-tested.
- Active database schema is 7; Schema 8 is dormant.
- Populated-target archive merge is not implemented.
- Meaning Slice 4 is merged and verified (1347 passed, 0 failed, 0 skipped).
- Active branch: `feat/meaning-slice5-sense-queue`; Packages 1 and 2 are implemented and fully validated (1358 passed, 0 failed, 0 skipped).
- Exact next action: PR review; manual merge and post-merge synchronization remain pending.
- No Beta 13 or active packaging task is in progress.
