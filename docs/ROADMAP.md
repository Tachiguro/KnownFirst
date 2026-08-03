# KnownFirst roadmap

**Prioritization date:** 2026-08-03

This roadmap records intended order. It does not claim that planned behavior exists. Verified implementation state belongs in [PROJECT_STATE.md](PROJECT_STATE.md).

## Status definitions

- **Committed:** merged and verified on `master`.
- **Current:** active scoped work on a local branch under review/verification.
- **Planned:** accepted ordering, implementation not started.
- **Deferred:** intentionally outside the current sequence.
- **Public-release blocker:** must be resolved before any public Google Play promotion.

## Prioritized milestones

| Priority | Milestone | Status | Required outcome |
| ---: | --- | --- | --- |
| 1 | Beta 10 Internal Testing | Committed | Portable `.kfarchive` export/import for empty targets, native file pickers, one-time What's New notice, Beta 10 identity; merged via PR #18 (`2f3f89d`). |
| 2 | Russian UI & Translation Target | Committed | Russian UI localization (PR #20), Learn repeat/direction clarity (PR #21), Beta 11 identity (PR #22), Beta 12 Russian translation target fix (PR #23/PR #32); distributed & user-tested via Google Play Internal Testing. |
| 3 | Meaning & Backup Foundations | Committed | Backup Merge Slices 1–3 (PRs #26–#28), Meaning Slices 0–3 (PRs #29–#33); dormant Schema-8 foundations merged to master (`ad6f1456`). |
| 4 | Tooling Infrastructure & Safeguards | Committed | Windows StartupSmoke GUI launcher (PR #35), New-Chat Bootstrap Protocol (PR #36), Google Play packaging safeguards (PR #37). |
| 5 | Meaning Slices 4–5 | Committed | Slice 4 merged via PR #40 and verified (1347/0/0); Slice 5 merged via PR #41 and verified (1364/0/0). |
| 6 | Schema 8 Activation | Committed | `CurrentVersion` is 8 and live database migration is active; merged via PR #42 and verified (1542/0/0). |
| 7 | Schema-8 MergePreflight | Committed | Slice 7 MergePreflight adaptation for merge planning; merged via PR #43 and verified (1551/0/0). |
| 8 | Populated Target Merge Writer | Committed | Transactional populated-target merge writer and Import routing; merged via PR #44 and verified (1593/0/0). |
| 9 | Import UI & Localization | Committed | Import UI, localized preview/result handling, and end-to-end convergence validation; merged via PR #45 (checkpoint result 1626/0/0 prior to merge). Follow-up correctness and data-safety fixes merged via PR #46 (preparation selected-meaning acceptance), PR #47 (diagnostics/export stale lexical-reader), and PR #48 (Windows portable-export atomic replacement). |
| 10 | Test-confidence and release-readiness governance | Current | Documentation-governance package (this program, P0-P9 below) establishing strict-TDD evidence rules, a production unfinished-control policy, a debug-versus-Release UI separation rule, a mandatory pre-AAB gate, and an evidence-based cleanup sequence. |
| 11 | Public-release support surface | Planned — every-AAB blocker (see policy below) and public-release blocker | Implement functional Support KnownFirst and Report a bug controls, or explicitly remove them from Release rendering; and add reopenable release-note history. |
| 12 | Automated GUI validation | Planned | Android-first deterministic GUI automation (Appium/UiAutomator2); Windows automation launcher integration. |
| 13 | Public-release readiness | Planned — public-release blocker | Privacy disclosures, attribution/license review, support/payment surface, website, and store materials. |
| 14 | Russian source-text support | Deferred | Cyrillic tokenization/normalization, Russian Wiktionary language-section parsing, Russian Wikipedia fallback. |

## Committed

- Beta 10 Internal Testing: portable recovery export/import for empty targets, native Windows/Android file pickers, one-time What's New notice — merged via PR #18 (`2f3f89d`).
- Wikipedia fallback behind Wiktionary — merged via PR #11 (`d33cd80`).
- Task-based documentation routing — merged via PR #16 / PR #17.
- Russian UI localization and Russian as translation target for English/German source texts — merged via PR #20 (`8cc2bd9`).
- Learn repeat/direction clarity and `IsAgainRepeat` visual badges — merged via PR #21 (`52e74f2`).
- Beta 11 release identity — merged via PR #22 (`7076aa4`).
- Beta 12 Russian translation target fix and simplified import mode selector — merged via PR #23 / PR #32 (`b5e4b05`); distributed via Google Play Internal Testing and user-tested (confirmed 2026-07-30).
- Backup Merge Slices 1–3 (contracts library, safety-copy service, read-only preflight) — merged via PRs #26, #27, #28.
- Meaning Slices 0–3 (architecture, activation sequence, dormant migration engine, archive v2, multi-Sense preparation foundation) — merged via PRs #29, #30, #31, #32, #33 (`b405aa4`).
- Windows GUI StartupSmoke launcher — merged via PR #35 (`caf4221`).
- New-Chat Bootstrap Protocol — merged via PR #36 (`4308533`).
- Google Play packaging safeguards — merged via PR #37 (`ad6f1456`).
- Meaning Slice 4 direction-specific answer assignments and progress replay — merged via PR #40 (`60d8f073`); full suite 1347 passed, 0 failed, 0 skipped.
- Meaning Slice 5 Sense-addressed cards and queue behavior — merged via PR #41 (`e1724651`); full suite 1364 passed, 0 failed, 0 skipped.
- Meaning Slice 6 Schema-8 activation and the first real user-data migration — merged via PR #42 (`3debd7a1`); full suite 1542 passed, 0 failed, 0 skipped.
- Meaning Slice 7 Schema-8 MergePreflight adaptation for merge planning — merged via PR #43 (`d53ffe3d`); full suite 1551 passed, 0 failed, 0 skipped.
- Meaning Slice 8 transactional Schema-8 populated-target merge writer and Import routing — merged via PR #44 (`cf1b0995`); full suite 1593 passed, 0 failed, 0 skipped.
- Meaning Slice 9 portable import preview UI, localized handling, and end-to-end convergence validation — merged via PR #45 (`37e6b552`); checkpoint result on the feature branch prior to merge was 1626 passed, 0 failed, 0 skipped. Read-only preview before confirmation with distinct restore, merge, and no-change cases; preview performs no mutation or writer invocation; confirmation re-validates independently; unified Import operation with no separate Merge button; merge preview and results expose aggregate counts and explain safety-copy creation; disposition classification RestoredIntoEmpty/MergeApplied/MergeNoChange with notifications only for the first two; complete EN/DE/RU localization; corrected LearningSession identity so distinct sessions using the same card set no longer collapse.
- Preparation selected-meaning acceptance fix — merged via PR #46 (`8b5e524c`); an invalid preparation context is now hidden rather than silently accepted.
- Diagnostics/export stale lexical-reader fix — merged via PR #47 (`57ed35f8`); `PreparationCandidates.ResultJson` is now read via the payload codec in diagnostics and export paths.
- Windows portable-export atomic-replacement fix — merged via PR #48 (`092eafe4`); Windows export stages the archive to a same-directory temporary file, validates it through the production archive validator, and only then atomically finalizes, preserving an existing backup byte-for-byte on any failure before finalization.

## Current

**Test-confidence, strict-TDD, production-UI cleanliness, pre-AAB documentation, and safe-cleanup governance program**

This program is documentation-governance only; no product code change is authorized by establishing it. See [CURRENT_WORK.md](CURRENT_WORK.md) for the active package and ["Test-confidence and release-readiness program"](#test-confidence-and-release-readiness-program) below for the full prioritized sequence (P0-P9).

## Planned Sequence (Meaning & Merge)

The current product direction is **non-destructive populated-target portable archive import**.

1. **Meaning Slice 4:** Completed and merged via PR #40 (dual-schema compatible).
2. **Meaning Slice 5:** Completed and merged via PR #41 (dual-schema compatible).
3. **Slice 6 (Schema-8 Activation):** Completed and merged via PR #42; `CurrentVersion` is 8 and the first real user-data migration is live.
4. **Slice 7 (Schema-8 MergePreflight adaptation):** Completed and merged via PR #43; merge planning for populated targets is complete and deterministic.
5. **Slice 8 (Populated-target merge writer):** Completed and merged via PR #44; transactional writer with stale-plan refusal, atomic rollback, and full import routing is complete.
6. **Slice 9 (Import preview and localization):** Completed and merged via PR #45; read-only preview UI, localized EN/DE/RU handling, corrected LearningSession identity, and end-to-end convergence validation complete.

## Deferred

- Re-validation of packaging script runtime behavior (deferred to `KF-RELEASE-001` before the next genuine release).
- Russian source-text import, Cyrillic tokenization/normalization, Russian Wiktionary language-section parsing, and Russian Wikipedia fallback.
- Additional learning languages beyond English, German, and Russian-as-target.
- Full offline dictionary package pipeline.
- FSRS scheduling.
- PDF, EPUB, and website import.
- Speech, handwriting, and pronunciation features.
- Cloud synchronization and accounts.
- Analytics, advertising, payments, and automatic telemetry.

## Test-confidence and release-readiness program

**Governing policy (established by this program):** every enabled and visible actionable control in a Release build must produce a meaningful implemented outcome. Planned but unimplemented features remain documented in this file or other planning documentation only; they must not appear in Release rendering as an enabled button, a disabled button, a link, a menu entry, a card, a placeholder label, a "coming soon" control, or an inaccessible/visually hidden interactive element — an unfinished control must be absent from the rendered Release component tree and accessibility tree, not merely hidden with CSS. Debug-only exposure of a planned control is permitted only when it is explicitly gated by an approved diagnostic build condition, cannot be activated in a normal Release build, is clearly marked as diagnostic and unfinished, and is excluded from the Google Play Release AAB; debug-only visual diagnostics (layout outlines, borders, bounding boxes, overlays, developer badges) must not appear in a Release build or AAB. Full detail lives in [PROJECT_STATE.md](PROJECT_STATE.md) "Production-control and debug-UI policy", [TESTING.md](TESTING.md), and the mandatory pre-AAB gate in [BUILD_AND_RELEASE.md](BUILD_AND_RELEASE.md).

**An unresolved visible unfinished control, or an unresolved Release-visible debug outline or diagnostic overlay, is a blocker for every future AAB** — Internal Testing and public alike — not only for public promotion. This is distinct from, and in addition to, the public-release-only blockers listed below.

Prioritized work-package sequence:

- **P0 — Documentation reconciliation and governance.** This package: reconciles stale state, establishes the policies above.
- **P1 — Read-only Android portable-export boundary investigation.** Confirms whether `AndroidPortableArchiveFileService` has the same open-truncate-before-write exposure that Windows had before PR #48.
- **P2 — Production UI inventory.** Identify all unfinished controls, all placeholder handlers, and all debug-only outlines/overlays/diagnostic badges/developer aids; classify each as production functionality, debug-only diagnostics, or documentation-only planned work.
- **P3 — Test-first Release UI cleanliness contracts.** Add tests verifying unfinished controls are absent from Release rendering, debug-only controls/overlays are absent from Release, CSS hiding is not used as the sole exclusion mechanism, and no dead route/placeholder action/debug-only navigation entry remains in Release.
- **P4 — Remove or implement all unfinished Release-visible controls.** Support KnownFirst, Report a bug, and any additional item P2 discovers. Planned functionality may remain documented here without appearing in Release UI.
- **P5 — Critical workflow coverage-gap packages**, prioritized by data-loss and user-blocking risk.
- **P6 — Rendered GUI interaction and Release-appearance coverage** for release-critical workflows.
- **P7 — Candidate-specific pre-AAB validation and documentation review** against the exact candidate commit.
- **P8 — Evidence-based cleanup and refactoring packages**, only after the relevant behavior is protected by tests; divided into small independently reviewable packages; no broad "delete everything unused" operation; textual search alone never proves a target is unused.
- **P9 — Separately authorized AAB creation**, only after all applicable gates in the pre-AAB release-readiness gate ([BUILD_AND_RELEASE.md](BUILD_AND_RELEASE.md)) pass.

This program does not claim that every package here is already scheduled with a date, that all workflows are already tested, or that the next AAB is authorized. Package sequencing may be reprioritized by explicit user decision.

## Public-release blockers

The following must be resolved before any public Google Play promotion (current distribution remains Internal Testing only), in addition to the every-AAB unfinished-control and debug-UI blockers above:

1. Functional (or explicitly removed) Support KnownFirst and Report a bug controls (also an every-AAB blocker under the policy above).
2. Reopenable release notes / release-note history.
3. Wikimedia attribution and license-version handling reviewed against actual provider-returned metadata.
4. Deterministic GUI validation coverage sufficient to support release confidence without exclusively manual verification.
5. Privacy, support, and payment-surface documentation and external legal/tax review where applicable.
