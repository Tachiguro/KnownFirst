# KnownFirst Current Work

## Last updated

2026-08-08 (Milestone 14A post-merge closure)

## Repository

- Repository: https://github.com/Tachiguro/KnownFirst.git
- Only local project folder: `C:\Dev\KnownFirst` (see [ADR-0007](decisions/ADR-0007-single-canonical-working-directory.md))
- Active rule: use the single folder; create no worktree without explicit user approval.
- Only one writing agent may operate at a time.

## Verified product-state milestone

- Most recent product-relevant milestone commit: `39609ffffb39c69238882172d153f4bb795ddab8` (Milestone 14A, PR #71 merged). This is historical milestone evidence, not a claim about the literal current `master` HEAD; discover the exact current `master` HEAD dynamically per [docs/NEW_CHAT_BOOTSTRAP.md](NEW_CHAT_BOOTSTRAP.md).
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
- **PR #60 — D4 closure and D5 activation.**
- **PR #61 — D5 Testing and GUI Contract Reconciliation.**
- **PR #62 — D5 Historical Banners and Routing Corrections.**
- **PR #63 — D5 Mechanical Markdown Hygiene.**
- **PR #64 — D5 closure and Package B revalidation queued.**
- **PR #65 — Package B (Schema-9 completed-review writer evidence):** genuine Schema-9 writer evidence and a narrow deterministic `BackupModelMapperV2` `ReviewSession` ordering correction; merge commit `f560a6b7ff9109bbee6c46602a002ea8b591de49`.
- **PR #68 — Package C (Schema-9 completed-review convergence):** convergence hardening, cross-installation canonical ordering, and two-installation synchronization; merge commit `db47de3bf48b49b5258ce16acc6e3e543d96143c`.
- **PR #71 — Milestone 14A (unfinished support/report control removal):** the unfinished `Support KnownFirst` and `Report a bug` controls and their shared placeholder behavior removed from the production Settings source, with a focused source-contract absence test; merge commit `39609ffffb39c69238882172d153f4bb795ddab8`.

## Currently active package

No implementation package is currently active. Milestone 14A is complete and merged to `master` (PR #71); discover live branch and pull-request lifecycle state dynamically per [NEW_CHAT_BOOTSTRAP.md](NEW_CHAT_BOOTSTRAP.md).

- Milestone 14A passed final PR review, was manually merged via PR #71 (merge commit `39609ffffb39c69238882172d153f4bb795ddab8`), and `POST_MERGE_SYNC_ONLY` completed successfully. No Milestone 14A lifecycle step remains outstanding.
- The unfinished `Support KnownFirst` and `Report a bug` production controls, their `Common_FeatureComingSoon` placeholder UI, and the shared placeholder state and handlers were removed from `Components/Pages/Settings.razor`. The dead placeholder styling was removed from `Components/Pages/Settings.razor.css`.
- `Settings_HelpAndSupport` and the build-identity display were intentionally retained; all unrelated Settings behavior is unchanged.
- The localization keys `Settings_SupportKnownFirst`, `Settings_ReportBug`, and `Common_FeatureComingSoon` are intentionally retained as resources. They are no longer referenced by any production control.
- A focused absence contract was added to `KnownFirst.Tests/UiWorkflowContractTests.cs` and completed a genuine focused TDD red/green cycle during `IMPLEMENT`.
- Authorized `UI_CONTRACT_AUTOMATED` evidence: `70 passed / 0 failed / 0 skipped`.
- **This evidence is source/markup/Razor/CSS contract evidence only.** It proves absence in the production component source, not runtime rendering.
- No rendered-GUI, Windows runtime, Android/device, AOT/trimming, APK/AAB, packaging, signing, publishing, store, release, or newer external-distribution evidence was produced by Milestone 14A.
- No SQLite schema version/migration, archive DTO/format version, public merge error/status contract, or release identity was changed by Milestone 14A.

Package C history is unaffected: it passed final PR review, was manually merged via PR #68 (merge commit `db47de3bf48b49b5258ce16acc6e3e543d96143c`), `POST_MERGE_SYNC_ONLY` completed successfully, and its final local automated evidence remains `1776 passed / 0 failed / 0 skipped`.

## Current blocker or pending validation

- No active implementation blocker or Milestone 14A lifecycle step remains outstanding; discover live branch and pull-request lifecycle state dynamically.
- Milestone 14 as a whole is **not** complete: reopenable release-note history (Milestone 14B) remains outstanding and has not started.
- Rendered-Release and AAB-level absence of the removed controls remains unproven and belongs to the future pre-AAB validation gate in [BUILD_AND_RELEASE.md](BUILD_AND_RELEASE.md).
- No Beta 13, build, packaging, signing, publishing, store, or device activity has occurred, and no such task is active.

## Exact next action

- No Milestone 14A lifecycle step remains.
- No repository-writing next phase is automatically authorized.
- The next planned product work is **Milestone 14B — reopenable release-note history**, the remaining half of ROADMAP milestone 14. It has not started and requires its own separately authorized `PLAN_ONLY` before implementation.

## Concise new-chat handoff

- Most recent recorded product-relevant milestone on `master`: `39609ffffb39c69238882172d153f4bb795ddab8` (PR #71, Milestone 14A merged). The exact current `master` HEAD is a live GitHub/Git fact, not this value — discover it dynamically per [docs/NEW_CHAT_BOOTSTRAP.md](NEW_CHAT_BOOTSTRAP.md).
- `DatabaseSchema.CurrentVersion` is 9 and Schema 9 is active on master.
- Beta 12 / build 12 remains the last confirmed external distribution (Google Play Internal Testing, user-tested 2026-07-30). No newer distribution has occurred.
- D1-D5 documentation reconciliation is complete. Package A, Package B, and Package C are merged and complete on master.
- Package C was implemented, MINOR-1 corrected, independently reviewed, `TEST_ONLY`-validated (1776/0/0 local automated evidence), passed final PR review, and manually merged via PR #68. `POST_MERGE_SYNC_ONLY` completed successfully.
- Milestone 14A removed the unfinished `Support KnownFirst` and `Report a bug` controls and their placeholder behavior from the production Settings source. It was manually merged via PR #71 (merge commit `39609ffffb39c69238882172d153f4bb795ddab8`) and `POST_MERGE_SYNC_ONLY` completed successfully. Its `UI_CONTRACT_AUTOMATED` evidence is `70 passed / 0 failed / 0 skipped` and is source/markup/Razor/CSS contract evidence only; rendered-Release and AAB-level absence remain unproven.
- Milestone 14 is not complete: Milestone 14B (reopenable release-note history) is still outstanding, has not started, and requires its own separately authorized `PLAN_ONLY`.
- No AAB, APK, Android build, signing, publishing, or store operation is authorized by this package.
