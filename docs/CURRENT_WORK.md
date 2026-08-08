# KnownFirst Current Work

## Last updated

2026-08-08 (Milestone 14B merged; Milestone 14 complete on `master`)

## Repository

- Repository: https://github.com/Tachiguro/KnownFirst.git
- Only local project folder: `C:\Dev\KnownFirst` (see [ADR-0007](decisions/ADR-0007-single-canonical-working-directory.md))
- Active rule: use the single folder; create no worktree without explicit user approval.
- Only one writing agent may operate at a time.

## Verified product-state milestone

- Most recent product-relevant milestone commit: `14138ccdab1e9b09a12ded002ff198d9b7312fcf` (Milestone 14B, PR #73 merged). This is historical milestone evidence, not a claim about the literal current `master` HEAD; discover the exact current `master` HEAD dynamically per [docs/NEW_CHAT_BOOTSTRAP.md](NEW_CHAT_BOOTSTRAP.md).
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
- **PR #73 — Milestone 14B (reopenable release-note history):** Settings → Help & Support link and new `/release-notes` route exposing the complete existing release-note catalog; merge commit `14138ccdab1e9b09a12ded002ff198d9b7312fcf`. `POST_MERGE_SYNC_ONLY` completed successfully.

## Currently active package

**Milestone 14 product/source work is complete: Milestone 14B was manually merged via PR #73** (merge commit `14138ccdab1e9b09a12ded002ff198d9b7312fcf`), `POST_MERGE_SYNC_ONLY` completed successfully, and local `master` is fast-forwarded to that commit.

**The currently active repository package is a separate, uncommitted documentation package: the Milestone 14B post-merge documentation closure**, on branch `docs/milestone14b-post-merge-closure-v1` (three files: this file, [PROJECT_STATE.md](PROJECT_STATE.md), [ROADMAP.md](ROADMAP.md)). This closure package reconciles those three documents with the already-merged Milestone 14B product state. It is **not yet committed, pushed, opened as a pull request, reviewed as a pull request, merged, or post-merge synchronized.** Its immediate next lifecycle action is independent re-review of the corrected uncommitted closure package; after approval it still proceeds through commit, push, PR creation, final PR review, manual user merge, and `POST_MERGE_SYNC_ONLY`. The separately authorized standing-delegation governance package is queued only after this closure package is fully merged and synchronized; governance work has not started. Discover live branch and pull-request lifecycle state dynamically per [NEW_CHAT_BOOTSTRAP.md](NEW_CHAT_BOOTSTRAP.md).

- Settings → Help & Support now offers one production-visible link to the new `/release-notes` route; the page exposes the complete existing release-note catalog.
- History is returned newest-first: `1.0.0-beta.12`, `1.0.0-beta.11`, `1.0.0-beta.10`, through the new `IReleaseNotesService.GetReleaseNoteHistory()` API.
- History access neither reads nor mutates the persisted seen-version state, so the history stays available after the one-time notice was dismissed.
- The automatic one-time What's New behavior is unchanged: `GetUnseenReleaseNotes()`, `MarkSeen()`, `WhatsNewModal`, and the seen-version preference store are untouched.
- Existing Beta 10/11/12 release-note content is unchanged. **Zero new localization keys** were added — the page reuses `WhatsNew_Title`, `WhatsNew_VersionLabel`, and the existing bullet keys.
- No Beta 13 identity or release-note entry, and no database, schema, archive, network, packaging, or release-identity change.
- `Support KnownFirst` and `Report a bug` remain absent from production controls.
- **IMPLEMENT focused TDD evidence:** genuine behavioral RED `0 passed / 5 failed / 0 skipped`, then GREEN `5 passed / 0 failed / 0 skipped` on the identical focused scope. Zero production files were modified before the RED run.
- **`TEST_ONLY` evidence:** `110 passed / 0 failed / 0 skipped` (`ReleaseNotesTests` 38/38, `UiWorkflowContractTests` 72/72). `TEST_ONLY` modified no files.
- **This 110-test evidence is automated service/unit/contract evidence plus static source/markup/Razor/CSS contract evidence only.** Rendered GUI behavior, actual click/navigation behavior, runtime focus/keyboard behavior, visual layout and responsiveness, Windows/Android runtime behavior, Release-build rendering, APK/AAB behavior, and physical-device behavior all remain **unproven**.

Milestone 14A history is unaffected: it passed final PR review, was manually merged via PR #71 (merge commit `39609ffffb39c69238882172d153f4bb795ddab8`), and `POST_MERGE_SYNC_ONLY` completed successfully. Package C history is likewise unaffected: merged via PR #68 (merge commit `db47de3bf48b49b5258ce16acc6e3e543d96143c`), with final local automated evidence `1776 passed / 0 failed / 0 skipped`.

## Current blocker or pending validation

- None for Milestone 14B specifically: implementation, targeted automated validation, independent review, commit (`940f54d59697b4d5744355634f6ae52b6cb40692`), push, PR creation, final PR review, and manual merge (PR #73, merge commit `14138ccdab1e9b09a12ded002ff198d9b7312fcf`) are all complete; `POST_MERGE_SYNC_ONLY` completed successfully.
- Milestone 14 as a whole is now complete on `master`.
- Rendered-GUI, runtime, platform, Release-build, and AAB-level behavior of the new history page and Settings entry point remains unproven and belongs to separately authorized manual/GUI verification and the future pre-AAB validation gate in [BUILD_AND_RELEASE.md](BUILD_AND_RELEASE.md).
- No Beta 13, build, packaging, signing, publishing, store, or device activity has occurred, and no such task is active.

## Exact next action

- **Independent re-review of the corrected, still-uncommitted Milestone 14B post-merge documentation closure** on branch `docs/milestone14b-post-merge-closure-v1`. Only after that review is approved does the closure proceed through commit, push, PR creation, final PR review, and manual user merge, followed by `POST_MERGE_SYNC_ONLY`.
- No repository-writing next phase beyond that review is automatically authorized. The separately authorized standing-delegation governance package, and any future Milestone 15+ work package, come only after this closure package is fully merged and synchronized (see [ROADMAP.md](ROADMAP.md) priority 15 and beyond).
- Automated agents never merge PRs; pull requests are merged exclusively by the repository owner manually through GitHub.

## Concise new-chat handoff

- Most recent recorded product-relevant milestone on `master`: `14138ccdab1e9b09a12ded002ff198d9b7312fcf` (PR #73, Milestone 14B merged). The exact current `master` HEAD is a live GitHub/Git fact, not this value — discover it dynamically per [docs/NEW_CHAT_BOOTSTRAP.md](NEW_CHAT_BOOTSTRAP.md).
- `DatabaseSchema.CurrentVersion` is 9 and Schema 9 is active on master.
- Beta 12 / build 12 remains the last confirmed external distribution (Google Play Internal Testing, user-tested 2026-07-30). No newer distribution has occurred.
- D1-D5 documentation reconciliation is complete. Package A, Package B, and Package C are merged and complete on master.
- Package C was implemented, MINOR-1 corrected, independently reviewed, `TEST_ONLY`-validated (1776/0/0 local automated evidence), passed final PR review, and manually merged via PR #68. `POST_MERGE_SYNC_ONLY` completed successfully.
- Milestone 14A removed the unfinished `Support KnownFirst` and `Report a bug` controls and their placeholder behavior from the production Settings source. It was manually merged via PR #71 (merge commit `39609ffffb39c69238882172d153f4bb795ddab8`) and `POST_MERGE_SYNC_ONLY` completed successfully. Its `UI_CONTRACT_AUTOMATED` evidence is `70 passed / 0 failed / 0 skipped` and is source/markup/Razor/CSS contract evidence only; rendered-Release and AAB-level absence remain unproven.
- Milestone 14B (reopenable release-note history) was committed (`940f54d59697b4d5744355634f6ae52b6cb40692`) on branch `feature/milestone14b-release-note-history-v1`, and manually merged via PR #73 (merge commit `14138ccdab1e9b09a12ded002ff198d9b7312fcf`); `POST_MERGE_SYNC_ONLY` completed successfully. Focused TDD completed RED 5-failed then GREEN 5-passed; `TEST_ONLY` returned `110 passed / 0 failed / 0 skipped` (`ReleaseNotesTests` 38/38, `UiWorkflowContractTests` 72/72). That evidence is service/unit/contract plus source/markup/Razor/CSS contract evidence only.
- Milestone 14 is complete on `master`: no rendered-GUI, runtime, platform, or AAB evidence exists for 14B yet — that remains separately authorized manual/GUI verification and the future pre-AAB validation gate in [BUILD_AND_RELEASE.md](BUILD_AND_RELEASE.md).
- No AAB, APK, Android build, signing, publishing, or store operation is authorized by this package.
