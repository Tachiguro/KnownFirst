# KnownFirst project state

**Status date:** 2026-08-02
**State source:** `master` (`cf1b0995415dc858357f0a5c9e90e7c0aefb327c`, PR #44 merge commit)
**Next product milestone:** KF-MEANING-001 Slice 9 — Import UI, localized preview/result handling, and final release validation

This document is the authoritative snapshot of verified current state. Update it when a milestone is completed or when a release, schema, supported platform, or confirmed limitation changes. Plans belong in [ROADMAP.md](ROADMAP.md).

## Stable release & source identity

| Field | Verified value |
| --- | --- |
| Project | KnownFirst |
| Source Version | `1.0.0-beta.12` (build 12) |
| Package ID | `com.tachiguro.knownfirst` |
| Target Distribution | Google Play Internal Testing |
| Distributed Status | Distributed and user-tested (confirmed 2026-07-30; see [docs/releases/1.0.0-beta.12.md](releases/1.0.0-beta.12.md)) |
| Installed Displayed Identity | `1.0.0-beta.12` / Release / Build 12 / Commit `cfbaee6a` (DIRTY) |
| Exact Distributed Commit | Unverified |

## Supported platforms

- **Android:** distributed through Google Play Internal Testing; minimum Android version is API 24 (Android 7.0).
- **Windows:** primary local development and automated/manual verification platform.
- **iOS:** deliberately removed from the project and not supported.
- **Mac Catalyst:** deliberately removed from the project and not supported.

## Production capabilities

The current product source implements:

- English, German, and Russian UI localization with persisted System, Light, and Dark appearance modes;
- exact text import with deterministic Unicode-aware sentence and vocabulary analysis;
- Russian as a translation target for English and German source texts (Russian source text remains deferred);
- simplified Definition or Translation import mode selection;
- resumable Known/Unknown vocabulary review with persisted decisions and Undo;
- language-scoped vocabulary identity and global minimal known-word markers;
- frequency-prioritized automatic or manual preparation;
- explicit online-lookup consent, read-only Wiktionary lookup with automatic fallback to Wikipedia definitions, and a local SQLite lexical cache;
- source attribution, alternative-meaning selection, manual correction, and context snapshots;
- recognition and spelling card directions with independent deterministic schedules;
- Learn screen card direction indicators and visual "Repeat" badges for `IsAgainRepeat` cards;
- resumable learning sessions and permanent-known cleanup;
- portable `.kfarchive` data export (native Save dialog on Windows and Android);
- portable recovery import of a `.kfarchive` archive into empty installations (native Open dialog on Windows and Android);
- transactional populated-target import with validated safety copy, merge plan validation, and atomic commit-or-rollback; stale plans are rejected; reimport converges without duplicates;
- card scheduling replay through the existing scheduler in deterministic order (ReviewedAtUtc, then review fingerprint); replay preserves Sense, PreferredMeaning, and Direction;
- a one-time localized What's New notice shown once per version;
- transactional local persistence, startup maintenance, and bounded structured diagnostics;
- responsive Windows and Android layouts with localized workflow gating.

## Merged development foundations (Dormant)

The `master` branch includes the following merged technical foundations:

- **Backup Merge Slice 1 (PR #26):** pure merge contracts library (`Services/DataSafety/Merge/`).
- **Backup Merge Slice 2 (PR #27):** validated pre-merge safety-copy foundation (`MergeSafetyCopyService`).
- **Backup Merge Slice 3 (PR #28):** read-only merge preflight planner (`MergePreflightPlanner`).
- **Meaning Slice 0 (PR #29):** meaning-centric architecture specification.
- **Meaning Slice 0.1 (PR #30):** Schema-8 activation sequence definition.
- **Meaning Slice 1 (PR #31):** dormant Schema-8 migration engine (`Schema8DormantMigration`).
- **Meaning Slice 2 (PR #32):** archive format v2 and dual-schema backup support.
- **Meaning Slice 3 (PR #33):** dormant multi-Sense preparation foundation (`PreparationServiceSchema8`).
- **Meaning Slice 4 (PR #40):** direction-specific answer assignments and progress replay; verified with 1347 passed, 0 failed, 0 skipped.
- **Meaning Slice 5 (PR #41):** Sense-addressed learning cards, frozen queue targets, and permanent-known cleanup; verified with 1364 passed, 0 failed, 0 skipped.
- **Meaning Slice 6 (PR #42):** Schema-8 activation and first real user-data migration; verified with 1542 passed, 0 failed, 0 skipped.
- **Meaning Slice 7 (PR #43):** Schema-8 MergePreflight adaptation for merge planning; verified with 1551 passed, 0 failed, 0 skipped.
- **Windows GUI StartupSmoke Launcher (PR #35):** `-Action GuiTest` launcher entry point and profile isolation under `artifacts/`.
- **New-Chat Bootstrap Protocol (PR #36):** permanent dynamic bootstrap governance in `docs/NEW_CHAT_BOOTSTRAP.md`.
- **Google Play Packaging Safeguards (PR #37):** hardened `scripts/publish-google-play-bundle.ps1` with cross-process lock, warning escalation, candidate ownership, and sidecar verification.

**Current Status (master):**
- The active database schema is **8** (`PRAGMA user_version = 8`).
- Schema 8 is active during normal application initialization on master.
- Slices 1–8 are merged and verified on master (PR #44).
- Slice 9 (portable import preview UI, localized handling, and end-to-end convergence validation) is implemented and awaiting manual merge.

## Confirmed verification

### Automated

- **Contract & Regression Suite:** All unit, contract, and service tests pass on `master`.
- Note: Automated tests cover Core policies, text analysis, temporary SQLite persistence, workflow logic, localization, diagnostics, lookup providers with offline fixtures, script contract invariants, and archive contracts. Automated tests do not make live network requests.

### Platform builds

- **Windows / Android Debug & Release:** Build readiness verified during Beta 10, Beta 11, and Beta 12 release preparation.

## Database status

- Storage is local SQLite in the application data directory (`knownfirst.db3`).
- On `master`, `DatabaseSchema.CurrentVersion` and `PRAGMA user_version` are **8** (Slice 6 and later merged).
- Schema 8 is active in real application databases on master.
- Initialization is forward-oriented and preserves existing rows while adding supported tables or columns.
- Initialization reads `PRAGMA user_version` first and rejects any version greater than the current version before modifying tables or cache.
- Complete persisted-data rules are in [DATABASE_CONTRACT.md](DATABASE_CONTRACT.md).
- Portable recovery format v1 is documented in [architecture/backup-format-v1.md](architecture/backup-format-v1.md).

## Known limitations

- Exported `.kfarchive` archives are not encrypted and may contain personal imported text and learning history; users are warned before export.
- "Support KnownFirst" and "Report a bug" controls in Settings are placeholders and not yet functional.
- Cloud synchronization, accounts, analytics, advertising, and payments are not implemented.
- Offline dictionary packages and FSRS scheduling are deferred.
- Online lookup requires explicit consent and network access on cache misses.
- Public Google Play release is intentionally not yet pursued.
- Tooling-only improvements (such as PR #37) do not create a new Beta 13 product release.

This document does not claim public-release readiness or draw legal conclusions about license/attribution compliance; those remain open review items tracked in [ROADMAP.md](ROADMAP.md).

## Active development

The stable master baseline is `cf1b0995415dc858357f0a5c9e90e7c0aefb327c` (PR #44 merged), carrying source version `1.0.0-beta.12` (build 12). `DatabaseSchema.CurrentVersion` is **8** and Schema 8 is active for real application databases on master.

**KF-MEANING-001 Slice 8 (merged PR #44)** — transactional Schema-8 populated-target merge writer and Import routing, with complete result 1593 passed, 0 failed, 0 skipped.

**KF-MEANING-001 Slice 9 — portable import preview UI, localized handling, and end-to-end convergence validation** is implemented and validated on feature branch `feature/meaning-slice9-import-preview-ui` (checkpoint commit `22d7c1fa0afea5f608b0ce60f48b8a1beecd9cdd`), with complete result of **1626 passed, 0 failed, 0 skipped**. Manual PR merge is pending.

Verified Slice-9 behavior on that branch:

- **Import preview UI** — read-only preview before confirmation; distinguishes restore (empty target), merge (populated Schema-8 target), and no-change (duplicate import) cases.
- **Preview safety** — no database mutation, safety copy, or writer invocation during preview; supports non-seekable caller streams.
- **Confirmation workflow** — distinct action labels for restore or merge; no-change presents success without a mutating action; re-validates and re-evaluates independently on confirmation.
- **Unified import operation** — single Import data operation; no separate Merge button or separate merge workflow.
- **Merge preview and results** — expose aggregate inserted, enriched, preserved-variant, and skipped counts; explain that local data is preserved and a validated private safety copy is created before mutation.
- **Disposition classification** — RestoredIntoEmpty, MergeApplied, MergeNoChange; workflow notifications occur only for RestoredIntoEmpty and MergeApplied.
- **Localization** — complete EN/DE/RU coverage for preview, result, and failure handling.
- **Corrected LearningSession identity** — distinct real sessions using the same card set no longer collapse; identity includes StartedAtUtc, CompletedAtUtc, ordered queue digest, and Rating per item; planner and target-index share the same implementation; reimport converges without duplicates.
- **End-to-end convergence validation** — real automated tests exercise archive creation → validation → preview → preflight → validated safety copy → transactional writer → deterministic scheduler replay → result summary → repeated-import no-change; bidirectional divergent Schema-8 databases converge semantically.
- **Archive-v1 upgrade and convergence** — Schema-8 populated-target Import upgrades archive-v1 in memory and converges on reimport.
- **Safety-copy validation** — safety copies are reopened and validated from final paths; represent the pre-merge target state; remain available after later writer failure.
