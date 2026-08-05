# KnownFirst Current Work

## Last updated

2026-08-05

## Repository

- Repository: https://github.com/Tachiguro/KnownFirst.git
- Only local project folder: `C:\Dev\KnownFirst` (see [ADR-0007](decisions/ADR-0007-single-canonical-working-directory.md))
- Active rule: use the single folder; create no worktree without explicit user approval.
- Only one writing agent may operate at a time.

## Verified master baseline

- Master commit: `06b361250d99085cba1e47ad5653a2dbe503f2da` (PR #52 merged)
- Source-controlled application identity: `1.0.0-beta.12` (build 12)
- Confirmed distribution: `1.0.0-beta.12` / build 12 was distributed via Google Play Internal Testing and user-tested (confirmed 2026-07-30). No newer Android build, AAB, Internal Testing release, installation, or user test has occurred since.
- Active database schema on master: SQLite `PRAGMA user_version` 9
- Supported platforms: Android (Google Play Internal Testing) and Windows development/verification. iOS and Mac Catalyst remain removed.
- Solution: `KnownFirst.slnx`

## Completed packages on master since the previous baseline

- **PR #49 — Documentation governance and release-readiness rules.**
- **PR #50 — Android portable export staging:** Strict validation before destination acquisition.
- **PR #51 — Schema-9 review-session history storage activation.**
- **PR #52 — Package A (Schema-9 completed-review convergence):** Schema-9 completed-review identity, planner, target-index parity, and characterization coverage.

## Active work package

**Authoritative documentation reconciliation**

- **D1 (Current package on `docs/reconcile-authoritative-state-schema9`):** Reconcile KnownFirst's authoritative repository state, Schema-9 database contract, active roadmap, backlog, changelog, and pull-request validation language. (Not merged).
- D2–D5 remain later documentation packages.
- Package B implementation is not accepted and is paused pending documentation repair.
- Package C remains future work.

## Current blocker or pending validation

- Package B (completed-review convergence writer) is paused.
- No pull request is currently open.
- No remote Package-B branch exists.
- No Beta 13, packaging, device, or store task is active.

## Exact next action

- Review and complete this documentation-governance package (branch `docs/reconcile-authoritative-state-schema9`).

## Concise new-chat handoff

- Master baseline is `06b361250d99085cba1e47ad5653a2dbe503f2da` (PR #52 merged); no open pull request exists.
- `DatabaseSchema.CurrentVersion` is 9 and Schema 9 is active on master.
- Beta 12 / build 12 remains the last confirmed external distribution (Google Play Internal Testing, user-tested 2026-07-30). No newer distribution has occurred.
- The current approved work is authoritative documentation reconciliation (D1).
- Package A is merged and complete; Package B implementation is not accepted and is paused; Package C is not started.
- No AAB, APK, Android build, signing, publishing, or store operation is authorized by this package.
