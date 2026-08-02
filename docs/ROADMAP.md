# KnownFirst roadmap

**Prioritization date:** 2026-08-01

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
| 9 | Import UI & Localization | Current | Import UI, localized preview/result handling, and end-to-end convergence validation; implemented on `feature/meaning-slice9-import-preview-ui` and verified (1626/0/0), manual merge pending. |
| 10 | Public-release support surface | Planned — public-release blocker | Implement functional Support KnownFirst and Report a bug controls (or an explicit removal decision), and reopenable release-note history. |
| 11 | Automated GUI validation | Planned | Android-first deterministic GUI automation (Appium/UiAutomator2); Windows automation launcher integration. |
| 12 | Public-release readiness | Planned — public-release blocker | Privacy disclosures, attribution/license review, support/payment surface, website, and store materials. |
| 13 | Russian source-text support | Deferred | Cyrillic tokenization/normalization, Russian Wiktionary language-section parsing, Russian Wikipedia fallback. |

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

## Current

**Meaning Slice 9 — portable import preview UI, localized handling, and end-to-end convergence validation**
- Implemented and validated on `feature/meaning-slice9-import-preview-ui` (checkpoint commit `bef0a05fe68d5b94d8bc5f2a1f98076fcb03e68f`) with a complete result of 1626 passed, 0 failed, 0 skipped. Manual PR merge is pending.
- Read-only preview before confirmation with distinct restore, merge, and no-change cases. Preview safety: no mutations or writer invocation during preview. Confirmation re-validates independently. Unified Import operation with no separate Merge button. Merge preview and results expose aggregate counts and explain safety-copy creation. Disposition classification: RestoredIntoEmpty, MergeApplied, MergeNoChange; notifications only for RestoredIntoEmpty and MergeApplied. Complete EN/DE/RU localization. Corrected LearningSession identity: distinct sessions using the same card set no longer collapse; identity includes timestamps, queue order, and ratings. End-to-end convergence: real tests exercise archive creation through repeated-import no-change; bidirectional divergent databases converge. Archive-v1 upgrades in memory for Schema-8 targets. Safety-copy validation: copies are reopened and validated; remain available after writer failure. Corrupt archives fail closed before safety-copy creation. See [CURRENT_WORK.md](CURRENT_WORK.md).

## Planned Sequence (Meaning & Merge)

The current product direction is **non-destructive populated-target portable archive import**.

1. **Meaning Slice 4:** Completed and merged via PR #40 (dual-schema compatible).
2. **Meaning Slice 5:** Completed and merged via PR #41 (dual-schema compatible).
3. **Slice 6 (Schema-8 Activation):** Completed and merged via PR #42; `CurrentVersion` is 8 and the first real user-data migration is live.
4. **Slice 7 (Schema-8 MergePreflight adaptation):** Completed and merged via PR #43; merge planning for populated targets is complete and deterministic.
5. **Slice 8 (Populated-target merge writer):** Completed and merged via PR #44; transactional writer with stale-plan refusal, atomic rollback, and full import routing is complete.
6. **Slice 9 (Import preview and localization):** Implemented and validated on `feature/meaning-slice9-import-preview-ui` (checkpoint commit `bef0a05`); read-only preview UI, localized EN/DE/RU handling, corrected LearningSession identity, and end-to-end convergence validation complete. Manual merge pending.

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

## Public-release blockers

The following must be resolved before any public Google Play promotion (current distribution remains Internal Testing only):

1. Functional (or explicitly removed) Support KnownFirst and Report a bug controls.
2. Reopenable release notes / release-note history.
3. Wikimedia attribution and license-version handling reviewed against actual provider-returned metadata.
4. Deterministic GUI validation coverage sufficient to support release confidence without exclusively manual verification.
5. Privacy, support, and payment-surface documentation and external legal/tax review where applicable.
