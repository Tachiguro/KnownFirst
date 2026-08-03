# KnownFirst Current Work

## Last updated

2026-08-03

## Repository

- Repository: https://github.com/Tachiguro/KnownFirst.git
- Only local project folder: `C:\Dev\KnownFirst` (see [ADR-0007](decisions/ADR-0007-single-canonical-working-directory.md))
- Active rule: use the single folder; create no worktree without explicit user approval.
- Only one writing agent may operate at a time.

## Verified master baseline

- Master commit: `092eafe46fa663b3bfebfe51d639a397bef103a1` (PR #48 merged)
- Source-controlled application identity: `1.0.0-beta.12` (build 12), unchanged by PRs #45-#48
- Confirmed distribution: `1.0.0-beta.12` / build 12 was distributed via Google Play Internal Testing and user-tested (confirmed 2026-07-30). No newer Android build, AAB, Internal Testing release, installation, or user test has occurred since.
- Active database schema on master: SQLite `PRAGMA user_version` 8 (Slice 6 activation merged)
- Supported platforms: Android (Google Play Internal Testing) and Windows development/verification. iOS and Mac Catalyst remain removed.
- Solution: `KnownFirst.slnx`

## Completed packages on master since the previous baseline

- **PR #45 — Meaning Slice 9 import preview UI, localized handling, and end-to-end convergence validation:** merged (checkpoint result 1626 passed, 0 failed, 0 skipped on the feature branch prior to merge). See [PROJECT_STATE.md](PROJECT_STATE.md) for verified capability detail.
- **PR #46 — Preparation selected-meaning acceptance fix:** invalid preparation context is now hidden rather than silently accepted.
- **PR #47 — Diagnostics/export stale lexical-reader fix:** `PreparationCandidates.ResultJson` is now read via the payload codec in diagnostics and export paths, correcting a stale-reader defect.
- **PR #48 — Windows portable-export data-safety fix:** Windows export now stages the archive to a same-directory temporary file, validates it with the production `BackupArchiveReader.ValidateVersionedAsync` path, and only then atomically finalizes via `File.Replace`/`File.Move`, so a failure at any stage before finalization leaves an existing backup byte-for-byte unchanged. See [PROJECT_STATE.md](PROJECT_STATE.md).

## Active work package

**Test-confidence, strict-TDD, production-UI cleanliness, pre-AAB documentation, and safe-cleanup governance program.**

- No product implementation, code change, or release package is currently active. This package (P0) is documentation-only and establishes governance for the packages that follow.
- The approved program sequence (see [ROADMAP.md](ROADMAP.md) for full detail) is:
  - P0 — Documentation reconciliation and governance (this package).
  - P1 — Read-only Android portable-export boundary investigation.
  - P2 — Production UI inventory (unfinished controls, placeholder handlers, debug-only diagnostic UI).
  - P3 — Test-first Release UI cleanliness contracts.
  - P4 — Remove or implement all unfinished Release-visible controls (Support KnownFirst, Report a bug, and any additional item P2 discovers).
  - P5 — Critical workflow coverage-gap packages, prioritized by data-loss and user-blocking risk.
  - P6 — Rendered GUI interaction and Release-appearance coverage for release-critical workflows.
  - P7 — Candidate-specific pre-AAB validation and documentation review.
  - P8 — Evidence-based cleanup and refactoring packages.
  - P9 — Separately authorized AAB creation only after all applicable gates in [BUILD_AND_RELEASE.md](BUILD_AND_RELEASE.md) pass.

## Current blocker or pending validation

- No implementation or validation blocker remains on master.
- No pull request is currently open.
- No Beta 13, packaging, device, or store task is active.

## Exact next action

- Review and merge this documentation-governance package (branch `docs/test-confidence-release-readiness`).

## Concise new-chat handoff

- Master baseline is `092eafe46fa663b3bfebfe51d639a397bef103a1` (PR #48 merged); no open pull request exists.
- `DatabaseSchema.CurrentVersion` is 8 and Schema 8 is active on master.
- Beta 12 / build 12 remains the last confirmed external distribution (Google Play Internal Testing, user-tested 2026-07-30). No newer distribution has occurred.
- The current approved work is the test-confidence and release-readiness governance program (P0-P9, see [ROADMAP.md](ROADMAP.md)). No product code change is authorized by this package.
- Windows portable export is now staged, strictly validated, and atomically finalized before replacing an existing destination (PR #48); Android export boundary parity is unconfirmed and is the subject of planned package P1.
- Support KnownFirst and Report a bug remain nonfunctional placeholder controls that currently render unconditionally, including in Release; their removal or implementation before the next AAB is a recorded blocker (see [PROJECT_STATE.md](PROJECT_STATE.md) and [ROADMAP.md](ROADMAP.md)).
- No AAB, APK, Android build, signing, publishing, or store operation is authorized by this package.
