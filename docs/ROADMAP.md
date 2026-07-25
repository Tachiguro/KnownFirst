# KnownFirst roadmap

**Prioritization date:** 2026-07-25

This roadmap records intended order. It does not claim that planned behavior exists. Verified implementation state belongs in [PROJECT_STATE.md](PROJECT_STATE.md).

## Status definitions

- **Committed:** merged and verified at the stated milestone.
- **Current:** active scoped work on a task branch under review.
- **Planned:** accepted ordering, implementation not started.
- **Deferred:** intentionally outside the current sequence.
- **Public-release blocker:** must be resolved before any public Google Play promotion.

## Prioritized milestones

| Priority | Milestone | Status | Required outcome |
| ---: | --- | --- | --- |
| 1 | Beta 10 Internal Testing | Committed | Portable `.kfarchive` export/import, native file pickers, one-time What's New notice, Beta 10 identity; merged via PR #18 (`2f3f89d`). |
| 2 | Core learning correctness | Current | Investigate and resolve the reported accidental duplicate-question behavior in Learn sessions ([KF-LEARN-001](BACKLOG.md)). |
| 3 | Russian language support v1 | Current | Russian UI localization and Russian-as-translation-target for English/German source texts, on `feature/russian-language-support-v1` (not yet merged). |
| 4 | Public-release support surface | Planned — public-release blocker | Implement functional Support KnownFirst and Report a bug controls (or an explicit removal decision), and reopenable release-note history. |
| 5 | Automated GUI validation | Planned | Android-first deterministic GUI automation (Appium/UiAutomator2); Windows automation feasibility spike second. |
| 6 | Public-release readiness | Planned — public-release blocker | Privacy disclosures, attribution/license review, support/payment surface, website, and store materials. |
| 7 | Future recovery evolution | Deferred | Safe merge/overwrite import design, only after a dedicated data-model and safety-backup plan is separately approved. |
| 8 | Russian source-text support | Deferred | Cyrillic tokenization/normalization, Russian Wiktionary language-section parsing, Russian Wikipedia fallback; only after core learning-correctness work (priority 2) stabilizes. |

## Committed

- Beta 10 Internal Testing: portable recovery export/import, native Windows/Android file pickers, one-time What's New notice, Beta 10 build identity — merged via PR #18 (`2f3f89d`).
- Versioning policy and Beta 9 identity (PR #15, `28f8a74`).
- Wikipedia fallback behind Wiktionary (PR #11, PR #14).
- Task-based documentation routing (PR #16, PR #17).
- Stable Windows and Android application foundation; exact text import and resumable review; automatic/manual preparation; recognition and spelling learning; local SQLite persistence at schema version 7.

## Current

**Core learning correctness**
- Read-only investigation of the reported duplicate-looking learning question (same word, same card direction, twice in one session with a small vocabulary pool). See [CURRENT_WORK.md](CURRENT_WORK.md) for the exact next action.

**Russian language support v1**
- Russian UI localization, explicit System/English/Deutsch/Русский preference, and Russian-as-translation-target implemented on `feature/russian-language-support-v1`; awaiting review and merge. Russian source-text support is a separate, deferred milestone.

## Deferred

- Merge/overwrite ("ReplaceAll") portable restore into a populated installation.
- Russian source-text import, Cyrillic tokenization/normalization, Russian Wiktionary language-section parsing, and Russian Wikipedia fallback.
- Additional learning languages beyond English, German, and Russian-as-target.
- Full offline dictionary package pipeline.
- FSRS scheduling.
- PDF, EPUB, and website import.
- Speech, handwriting, and pronunciation features.
- Cloud synchronization and accounts.
- Analytics, advertising, payments, and automatic telemetry.

Deferred items require a future explicit milestone and must not be introduced speculatively while executing the prioritized sequence.

## Public-release blockers

The following must be resolved before any public Google Play promotion (current distribution remains Internal Testing only):

1. Functional (or explicitly removed) Support KnownFirst and Report a bug controls.
2. Reopenable release notes / release-note history.
3. Wikimedia attribution and license-version handling reviewed against actual provider-returned metadata (see [architecture/backup-format-v1.md](architecture/backup-format-v1.md) and Wikipedia/Wiktionary attribution code for current implementation detail).
4. Deterministic GUI validation coverage sufficient to support release confidence without exclusively manual verification.
5. Privacy, support, and payment-surface documentation and external legal/tax review where applicable.
