# KnownFirst Current Work

## Last updated

2026-08-07 (Package C documentation reconciliation)

## Repository

- Repository: https://github.com/Tachiguro/KnownFirst.git
- Only local project folder: `C:\Dev\KnownFirst` (see [ADR-0007](decisions/ADR-0007-single-canonical-working-directory.md))
- Active rule: use the single folder; create no worktree without explicit user approval.
- Only one writing agent may operate at a time.

## Verified product-state milestone

- Most recent product-relevant milestone commit: `f560a6b7ff9109bbee6c46602a002ea8b591de49` (Package B, PR #65 merged). This is historical milestone evidence, not a claim about the literal current `master` HEAD; discover the exact current `master` HEAD dynamically per [docs/NEW_CHAT_BOOTSTRAP.md](NEW_CHAT_BOOTSTRAP.md).
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

## Currently active package

**Schema-9 Completed-Review Convergence — Package C (convergence hardening) — implemented, reviewed, validated on a local branch; not committed, not pushed, not merged**

- Package A and Package B are merged and present on `master` (PR #52, PR #65). Package C is active local work on branch `feature/schema9-completed-review-convergence-v1`; `master` itself does not yet contain Package C.
- A Package C `PLAN_ONLY` was approved by the user, scoping two proven canonical-output defects left open by Package B: (C-1) completed `ReviewSession` ordering falling through to the local `ReviewSession.Id` when two histories tie on every session-level field and differ only through candidate content, and (C-2) `SourceMaterial` ordering not total over distinct emitted documents.
- `IMPLEMENT` was completed on the branch: `Services/DataSafety/BackupModelMapperV2.cs` and `Services/DataSafety/Merge/MergeWriterTargetIndex.cs` were modified; a new caller-neutral `Services/DataSafety/Merge/Schema9ReviewSessionRowIdentities.cs` helper was added; regression tests were added to `KnownFirst.Tests/BackupCreationTests.cs`, `KnownFirst.Tests/MergeWriterServiceTests.cs`, and `KnownFirst.Tests/PortableImportEndToEndConvergenceTests.cs`.
- An independent `REVIEW_ONLY` pass found exactly one MINOR finding: the new `SourceMaterial` ordering covered scalar Document fields but not the emitted child subgraph (`Sentences`/`Occurrences`), leaving `sm-*`/`ss-*` bindings installation-dependent for source materials that tie on every scalar. A RED-first correction added a deterministic content-derived child-subgraph ordering key. An independent re-review returned **`PACKAGE C MINOR-1 CORRECTION REVIEW APPROVED`** — no BLOCKER, MAJOR, or MINOR findings remain.
- `TEST_ONLY` validation passed on the branch: `BackupCreationTests` 50/0/0, merge planner/writer/identity scope (`MergeWriterServiceTests`/`MergePreflightPlannerTests`/`MergeWorkflowIdentityTests`/`MergePreflightServiceTests`) 157/0/0, archive/restore/Schema-9 compatibility scope (`BackupArchiveV2Tests`/`PortableRecoveryTests`/`BackupServiceImportRoutingTests`/`Schema8BackupRestoreTests`/`Schema9RuntimeCompatibilityTests`) 117/0/0, `PortableImportEndToEndConvergenceTests` 6/0/0, and the full `ALL_AUTOMATED` suite **1776 passed / 0 failed / 0 skipped** (the pre-Package-C `master` baseline was 1769/0/0; the delta is exactly the seven added Package-C tests).
- **Package C remains entirely local: uncommitted, unpushed, without a PR, and unmerged.** No remote `feature/schema9-completed-review-convergence-v1` branch exists.
- No GUI, device, platform, standalone-build, packaging, signing, publishing, or release evidence exists for Package C.

## Current blocker or pending validation

- No blocker is active; no pull request is currently open.
- No Beta 13, packaging, device, or store task is active.

## Exact next action

- An independently authorized complete-diff `REVIEW_ONLY` of the full uncommitted Package C diff is the next repository action, before any `COMMIT_ONLY`. This document does not authorize that review; it records the expected next lifecycle gate.

## Concise new-chat handoff

- Most recent recorded product-relevant milestone on `master`: `f560a6b7ff9109bbee6c46602a002ea8b591de49` (PR #65, Package B merged). The exact current `master` HEAD is a live GitHub/Git fact, not this value — discover it dynamically per [docs/NEW_CHAT_BOOTSTRAP.md](NEW_CHAT_BOOTSTRAP.md).
- `DatabaseSchema.CurrentVersion` is 9 and Schema 9 is active on master.
- Beta 12 / build 12 remains the last confirmed external distribution (Google Play Internal Testing, user-tested 2026-07-30). No newer distribution has occurred.
- D1-D5 documentation reconciliation is complete. Package A and Package B are merged and complete on master.
- Package C (convergence hardening) is implemented, independently reviewed and corrected, and `TEST_ONLY`-validated (`ALL_AUTOMATED` 1776/0/0) on local branch `feature/schema9-completed-review-convergence-v1`. It remains uncommitted, unpushed, and unmerged — `master` does not yet contain it.
- No AAB, APK, Android build, signing, publishing, or store operation is authorized by this package.
