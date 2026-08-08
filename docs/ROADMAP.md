# KnownFirst roadmap

**Prioritization date:** 2026-08-07

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
| 10 | Documentation governance and portable export validation | Committed | PR #49 (documentation governance and release-readiness rules) and PR #50 (Android portable export staging and strict validation). |
| 11 | Schema-9 Review-Session History and Package A | Committed | PR #51 (Schema-9 review-session history storage) and PR #52 Package A (Schema-9 completed-review identity, planner, target-index parity, and characterization coverage). |
| 12 | Authoritative Documentation Reconciliation | Committed | Documentation-governance packages D1-D5 establishing truth and safe agent operation. D1 merged via PR #53. D2 merged via PR #55. D3 merged via PR #57. D4 merged via PR #59. D5 merged via PR #61, PR #62, and PR #63; D5 closure and Package B revalidation queued via PR #64. |
| 13 | Schema-9 Completed-Review Convergence (Package B and C) | Committed | Package B (writer evidence) is committed and merged: implemented, independently reviewed (approved), validated (`ALL_AUTOMATED` 1769/0/0), and merged via PR #65 (merge commit `f560a6b7ff9109bbee6c46602a002ea8b591de49`). Package C (convergence hardening) is implemented, independently reviewed, MINOR-1 corrected, independently re-reviewed (approved, no BLOCKER/MAJOR/MINOR findings), `TEST_ONLY`-validated (`ALL_AUTOMATED` 1776/0/0), passed final PR review, and merged via PR #68 (merge commit `db47de3bf48b49b5258ce16acc6e3e543d96143c`). `POST_MERGE_SYNC_ONLY` completed successfully. |
| 14 | Public-release support surface | Committed | Two parts, both merged. **14A (merged via PR #71, merge commit `39609ffffb39c69238882172d153f4bb795ddab8`; `POST_MERGE_SYNC_ONLY` completed successfully):** the explicit-removal path was taken — Support KnownFirst and Report a bug, their "coming soon" placeholder UI, and the shared placeholder handlers are removed from the production Settings source; `UI_CONTRACT_AUTOMATED` `70 passed / 0 failed / 0 skipped` (source-contract evidence only, not GUI, AAB, or release evidence). **14B (merged via PR #73, merge commit `14138ccdab1e9b09a12ded002ff198d9b7312fcf`; `POST_MERGE_SYNC_ONLY` completed successfully):** reopenable release-note history — Settings → Help & Support entry point plus a dedicated `/release-notes` route listing every catalog entry newest-first, committed (`940f54d59697b4d5744355634f6ae52b6cb40692`) on `feature/milestone14b-release-note-history-v1`; focused TDD completed and targeted automated validation returned `110 passed / 0 failed / 0 skipped` (service/unit/contract plus source/markup contract evidence only, no rendered-GUI, platform, or AAB evidence). This entry remains a public-release blocker only insofar as rendered-Release and AAB-level evidence for both parts is still unproven (see public-release blockers below). |
| 15 | Portable merge integrity hardening | Current | Portable database export/import integrity is hardened in bounded packages before GUI automation begins. **KF-BACKUP-003 Package D is committed and merged via PR #76** (merge commit `17d3f1a031b9f319041ff1034a227d17b1029c4f`; `POST_MERGE_SYNC_ONLY` completed successfully), making the affected v2 export ordering total over emitted content. **KF-BACKUP-004 (Schema-9 `LearningReview` merge integrity — collision-free physical review action keys, meaning-aware answer-variant identity, scheduler-replay alignment, `LearningSessionId` deliberately excluded from event identity) is committed and merged via PR #77** (merge commit `bec861fb8a054beb2804f1132b450da1e45dee90`; `POST_MERGE_SYNC_ONLY` completed successfully). The remaining portable-integrity residuals (`LegacyReviewSummaries` ordering, mid-session review-event export policy, `Learning.Cards`/Sense `StableId` cross-installation ordering, and the legacy v1 planner's analogous synthesized label) are assessed in further bounded packages before GUI automation. Evidence is automated only — no rendered-GUI, runtime, platform, Release-build, device, or AAB evidence. |
| 16 | Automated GUI validation | Planned | Android-first deterministic GUI automation (Appium/UiAutomator2); Windows automation launcher integration. |
| 17 | Public-release readiness | Planned — public-release blocker | Privacy disclosures, attribution/license review, support/payment surface, website, and store materials. |
| 18 | Russian source-text support | Deferred | Cyrillic tokenization/normalization, Russian Wiktionary language-section parsing, Russian Wikipedia fallback. |

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
- Windows portable-export atomic-replacement fix — merged via PR #48 (`092eafe4`).
- Documentation governance and release-readiness rules — merged via PR #49.
- Android portable export staging and strict validation before destination acquisition — merged via PR #50.
- Schema-9 review-session history storage activation — merged via PR #51.
- Package A (Schema-9 completed-review convergence) — merged via PR #52. Adds deterministic Schema-9 completed-review identities, preflight classification, duplicate rejection, target-index parity, and characterization coverage.
- D1 Authoritative State and Database Truth — merged via PR #53.
- D1 closure and D2 activation — merged via PR #54.
- D2 Agent Communication and Operation Governance — merged via PR #55.
- D2 closure and D3 activation — merged via PR #56.
- D3 Backup and Import Contracts — merged via PR #57.
- D3 closure and D4 activation — merged via PR #58.
- D4 Product, Workflow, and Release-Facing Documentation — merged via PR #59.
- D4 closure and D5 activation — merged via PR #60.
- D5 Testing, GUI Status, Historical Banners, and Markdown Hygiene — merged via PRs #61-#63.
- D5 closure and Package B revalidation queued — merged via PR #64.
- Schema-9 Completed-Review Convergence Package C (convergence hardening) — merged via PR #68.
- Milestone 14A (explicit-removal package for the unfinished Support KnownFirst and Report a bug controls and their placeholder behavior) — merged via PR #71 (merge commit `39609ffffb39c69238882172d153f4bb795ddab8`).
- Milestone 14B (reopenable release-note history: Settings → Help & Support entry point plus a dedicated `/release-notes` route) — merged via PR #73 (merge commit `14138ccdab1e9b09a12ded002ff198d9b7312fcf`). Milestone 14 as a whole is now committed: both 14A and 14B are merged to `master` and `POST_MERGE_SYNC_ONLY` completed successfully for both.

## Current

D1-D5 documentation reconciliation is complete. Package B (Schema-9 Completed-Review Convergence writer evidence) is implemented, independently reviewed (approved), validated, and merged via PR #65 (merge commit `f560a6b7ff9109bbee6c46602a002ea8b591de49`); it is present on master. Package C (convergence hardening) was implemented, independently reviewed, MINOR-1 corrected, independently re-reviewed (approved), `TEST_ONLY`-validated (`ALL_AUTOMATED` 1776/0/0), passed final PR review, and merged via PR #68 (merge commit `db47de3bf48b49b5258ce16acc6e3e543d96143c`).

Milestone 14 is complete. Milestone 14A (removal of the unfinished Support KnownFirst and Report a bug controls and their placeholder behavior from the production Settings source) was implemented, independently reviewed, MINOR-1 corrected, independently re-reviewed (approved), `TEST_ONLY`-validated (`UI_CONTRACT_AUTOMATED` `70 passed / 0 failed / 0 skipped` — source-contract evidence only, not GUI, device, AAB, or release evidence), passed final PR review, and merged via PR #71 (merge commit `39609ffffb39c69238882172d153f4bb795ddab8`). `POST_MERGE_SYNC_ONLY` completed successfully. Rendered-Release and AAB-level absence of the removed controls remains unproven and belongs to the future pre-AAB validation gate. Milestone 14B (reopenable release-note history) was committed (`940f54d59697b4d5744355634f6ae52b6cb40692`) on `feature/milestone14b-release-note-history-v1`: `PLAN_ONLY` was approved and corrected, `IMPLEMENT` completed a genuine focused TDD cycle (RED 5 failed, then GREEN 5 passed on the identical scope), targeted `TEST_ONLY` validation returned `110 passed / 0 failed / 0 skipped` (`ReleaseNotesTests` 38/38, `UiWorkflowContractTests` 72/72), pre-commit review plus pre-PR re-review were approved, final PR review was approved, and PR #73 was manually merged (merge commit `14138ccdab1e9b09a12ded002ff198d9b7312fcf`) with `POST_MERGE_SYNC_ONLY` completing successfully. That evidence is automated service/unit/contract plus source/markup/Razor/CSS contract evidence only; no rendered-GUI, runtime, platform, Release-build, or AAB evidence exists yet — that remains the future pre-AAB validation gate.

**Portable merge integrity hardening (priority 15) is the current milestone, and precedes automated GUI validation.** KF-BACKUP-003 Package D is merged via PR #76 (merge commit `17d3f1a031b9f319041ff1034a227d17b1029c4f`) and KF-BACKUP-004 (Schema-9 `LearningReview` merge integrity) is merged via PR #77 (merge commit `bec861fb8a054beb2804f1132b450da1e45dee90`), with `POST_MERGE_SYNC_ONLY` completed successfully for both. Remaining portable-integrity residuals are assessed in further bounded packages before GUI automation begins. See [CURRENT_WORK.md](CURRENT_WORK.md) for the active package state.

## Planned Sequence (Meaning & Merge)

The current product direction is **non-destructive populated-target portable archive import**.

1. **Meaning Slice 4:** Completed and merged via PR #40 (dual-schema compatible).
2. **Meaning Slice 5:** Completed and merged via PR #41 (dual-schema compatible).
3. **Slice 6 (Schema-8 Activation):** Completed and merged via PR #42; `CurrentVersion` is 8 and the first real user-data migration is live.
4. **Slice 7 (Schema-8 MergePreflight adaptation):** Completed and merged via PR #43.
5. **Slice 8 (Populated-target merge writer):** Completed and merged via PR #44.
6. **Slice 9 (Import preview and localization):** Completed and merged via PR #45.
7. **Schema-9 Completed-Review Convergence (Package A):** Completed and merged via PR #52.
8. **Schema-9 Completed-Review Convergence (Package B):** Completed and merged via PR #65 (merge commit `f560a6b7ff9109bbee6c46602a002ea8b591de49`); `TEST_ONLY`-validated (`ALL_AUTOMATED` 1769/0/0). `POST_MERGE_SYNC_ONLY` completed successfully.
9. **Schema-9 Completed-Review Convergence (Package C):** Completed and merged via PR #68 (merge commit `db47de3bf48b49b5258ce16acc6e3e543d96143c`). Convergence hardening (completed-`ReviewSession` and `SourceMaterial` canonical ordering) and two-installation synchronization testing implemented, independently reviewed and corrected, `TEST_ONLY`-validated (`ALL_AUTOMATED` 1776/0/0), and passed final PR review. `POST_MERGE_SYNC_ONLY` completed successfully.
10. **Schema-9 Portable Workflow Canonical Ordering (KF-BACKUP-003 Package D):** Completed and merged via PR #76 (merge commit `17d3f1a031b9f319041ff1034a227d17b1029c4f`); `TEST_ONLY`-validated (`ALL_AUTOMATED` 1786/0/0), final PR re-review approved 0 BLOCKER / 0 MAJOR / 0 MINOR / 0 NIT. `POST_MERGE_SYNC_ONLY` completed successfully.
11. **Schema-9 LearningReview Merge Integrity (KF-BACKUP-004):** Completed and merged via PR #77 (merge commit `bec861fb8a054beb2804f1132b450da1e45dee90`); `TEST_ONLY`-validated (`ALL_AUTOMATED` 1795/0/0), final PR review approved 0 BLOCKER / 0 MAJOR / 0 MINOR / 0 NIT. `POST_MERGE_SYNC_ONLY` completed successfully. Portable-integrity hardening continues in bounded packages before automated GUI validation begins.

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

## Documentation Reconciliation and Release-Readiness Program

**Governing policy (established by this program):** every enabled and visible actionable control in a Release build must produce a meaningful implemented outcome. Planned but unimplemented features remain documented in this file or other planning documentation only; they must not appear in Release rendering as an enabled button, a disabled button, a link, a menu entry, a card, a placeholder label, a "coming soon" control, or an inaccessible/visually hidden interactive element — an unfinished control must be absent from the rendered Release component tree and accessibility tree, not merely hidden with CSS. Debug-only exposure of a planned control is permitted only when it is explicitly gated by an approved diagnostic build condition, cannot be activated in a normal Release build, is clearly marked as diagnostic and unfinished, and is excluded from the Google Play Release AAB; debug-only visual diagnostics (layout outlines, borders, bounding boxes, overlays, developer badges) must not appear in a Release build or AAB. Full detail lives in [PROJECT_STATE.md](PROJECT_STATE.md) "Production-control and debug-UI policy", [TESTING.md](TESTING.md), and the mandatory pre-AAB gate in [BUILD_AND_RELEASE.md](BUILD_AND_RELEASE.md).

**An unresolved visible unfinished control, or an unresolved Release-visible debug outline or diagnostic overlay, is a blocker for every future AAB** — Internal Testing and public alike — not only for public promotion. This is distinct from, and in addition to, the public-release-only blockers listed below.

Prioritized work-package sequence:

1. **D1 Authoritative state and database truth** (Completed and merged via PR #53).
2. **D2 Agent communication and operation governance** (Completed and merged via PR #55).
3. **D3 Backup and import contracts** (Completed and merged via PR #57).
4. **D4 Product, workflow, and release-facing documentation** (`README.md`, `docs/KNOWNFIRST_ARCHITECTURE.md`, `docs/MVP_WORKFLOW.md`, `docs/VERSIONING.md`, `docs/BETA_TESTING.md`) (Completed and merged via PR #59).
5. **D5 Testing, GUI status, historical banners, and Markdown hygiene** (confirmed primary targets `docs/TESTING.md`, `docs/GUI_TEST_MATRIX.md`; historical-banner and Markdown-hygiene investigation categories) (Completed and merged via PR #61, PR #62, and PR #63).
6. **Schema-9 completed-review Package B** (Completed and merged via PR #65, merge commit `f560a6b7ff9109bbee6c46602a002ea8b591de49`). `PLAN_ONLY` was approved, `IMPLEMENT` and an independent `REVIEW_ONLY`/correction/re-review cycle completed (final verdict `PACKAGE B IMPLEMENTATION REVIEW APPROVED`), `TEST_ONLY` passed (`ALL_AUTOMATED` 1769/0/0), a final pre-commit complete-diff review returned `PACKAGE B FINAL REVIEW APPROVED`, and the published PR received `PACKAGE B PRE-PUSH REVIEW APPROVED` and `PACKAGE B FINAL PR REVIEW APPROVED`. `POST_MERGE_SYNC_ONLY` completed successfully.
7. **Package C** (Completed and merged via PR #68): `PLAN_ONLY` was approved; `IMPLEMENT` completed; an independent `REVIEW_ONLY` found exactly one MINOR (SourceMaterial child-subgraph ordering totality), corrected RED-first, and independently re-reviewed with verdict `PACKAGE C MINOR-1 CORRECTION REVIEW APPROVED` (no BLOCKER/MAJOR/MINOR findings remaining); `TEST_ONLY` passed (`ALL_AUTOMATED` 1776/0/0); passed final PR review, and manually merged via PR #68 (merge commit `db47de3bf48b49b5258ce16acc6e3e543d96143c`). `POST_MERGE_SYNC_ONLY` completed successfully. This `DOCUMENT_ONLY` reconciliation completes its documentation phase.

After these packages complete, additional release-readiness packages (production UI inventory, test-first UI cleanliness, removing unfinished controls, pre-AAB documentation review) will be prioritized.

## Public-release blockers

The following must be resolved before any public Google Play promotion (current distribution remains Internal Testing only), in addition to the every-AAB unfinished-control and debug-UI blockers above:

1. Functional (or explicitly removed) Support KnownFirst and Report a bug controls. **Milestone 14A took the explicit-removal implementation path:** both controls and their placeholder behavior are absent from the production Settings source, established by source-contract evidence (`UI_CONTRACT_AUTOMATED` `70 passed / 0 failed / 0 skipped`). Source-contract evidence is not package- or AAB-level evidence; confirming their absence in an actual Release build and AAB belongs to the mandatory pre-AAB validation gate in [BUILD_AND_RELEASE.md](BUILD_AND_RELEASE.md).
2. Reopenable release notes / release-note history. **Milestone 14B took the implementation path:** the Settings → Help & Support link and `/release-notes` route are merged to `master` via PR #73 (merge commit `14138ccdab1e9b09a12ded002ff198d9b7312fcf`), established by source/unit/contract evidence (`110 passed / 0 failed / 0 skipped`). Confirming behavior in an actual Release build and AAB belongs to the mandatory pre-AAB validation gate in [BUILD_AND_RELEASE.md](BUILD_AND_RELEASE.md).
3. Wikimedia attribution and license-version handling reviewed against actual provider-returned metadata.
4. Deterministic GUI validation coverage sufficient to support release confidence without exclusively manual verification.
5. Privacy, support, and payment-surface documentation and external legal/tax review where applicable.
