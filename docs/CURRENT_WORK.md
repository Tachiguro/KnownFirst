# KnownFirst Current Work

## Last updated

2026-08-06

## Repository

- Repository: https://github.com/Tachiguro/KnownFirst.git
- Only local project folder: `C:\Dev\KnownFirst` (see [ADR-0007](decisions/ADR-0007-single-canonical-working-directory.md))
- Active rule: use the single folder; create no worktree without explicit user approval.
- Only one writing agent may operate at a time.

## Verified master baseline

- Master commit: `143c771afb0c875e2d47d802c9c2115b967ef689` (PR #59 merged)
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
- **PR #53 — D1 authoritative state and database truth reconciliation.**
- **PR #54 — D1 closure and D2 activation.**
- **PR #55 — D2 Agent Communication and Operation Governance.**
- **PR #56 — D2 closure and D3 activation.**
- **PR #57 — D3 Backup and Import Contracts.**
- **PR #58 — D3 closure and D4 activation.**
- **PR #59 — D4 Product, Workflow, and Release-Facing Documentation.**

## Active work package

**Authoritative documentation reconciliation**

- **D1:** Reconcile KnownFirst's authoritative repository state, Schema-9 database contract, active roadmap, backlog, changelog, and pull-request validation language. (Completed and merged via PR #53).
- **D2:** Agent Communication and Operation Governance. (Completed and merged via PR #55).
- **D3:** Backup and Import Contracts. (Completed and merged via PR #57).
- **D4:** Product, Workflow, and Release-Facing Documentation. (Completed and merged via PR #59).
- **D5 (Current active documentation package):** Testing, GUI Status, Historical Banners, and Markdown Hygiene. Confirmed primary target files: `docs/TESTING.md`, `docs/GUI_TEST_MATRIX.md`. Historical status banners and Markdown hygiene (tables, headings, links, fragments, whitespace, encoding, BOM, line endings) remain mandatory investigation categories for the later D5 `PLAN_ONLY`. Any additional editable D5 file requires concrete evidence in that plan and separate user approval.
- Package B implementation is not accepted and is paused until D5 is completed, merged, and locally synchronized.
- Package C remains future work.

## Current blocker or pending validation

- Package B (completed-review convergence writer) is paused.
- No pull request is currently open.
- No remote Package-B branch exists.
- No Beta 13, packaging, device, or store task is active.

## Exact next action

- Review and complete the D5 Testing, GUI Status, Historical Banners, and Markdown Hygiene package.

## Concise new-chat handoff

- Master baseline is `143c771afb0c875e2d47d802c9c2115b967ef689` (PR #59 merged); no open pull request exists.
- `DatabaseSchema.CurrentVersion` is 9 and Schema 9 is active on master.
- Beta 12 / build 12 remains the last confirmed external distribution (Google Play Internal Testing, user-tested 2026-07-30). No newer distribution has occurred.
- The current approved work is D5 Testing, GUI Status, Historical Banners, and Markdown Hygiene.
- Package A is merged and complete; Package B implementation is not accepted and is paused; Package C is not started.
- No AAB, APK, Android build, signing, publishing, or store operation is authorized by this package.
