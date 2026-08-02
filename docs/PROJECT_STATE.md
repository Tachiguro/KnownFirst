# KnownFirst project state

**Status date:** 2026-08-02
**State source:** `master` (`d53ffe3d92e249e8bc2f191d1b5cc8b9e81681dc`, PR #43 merge commit)
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
- Slices 1–7 are merged and verified on master.
- Slice 8 (populated-target merge writer and Import routing) is awaiting manual merge.

## Confirmed verification

### Automated

- **Contract & Regression Suite:** All unit, contract, and service tests pass on `master`.
- Note: Automated tests cover Core policies, text analysis, temporary SQLite persistence, workflow logic, localization, diagnostics, lookup providers with offline fixtures, script contract invariants, and archive contracts. Automated tests do not make live network requests.

### Platform builds

- **Windows / Android Debug & Release:** Build readiness verified during Beta 10, Beta 11, and Beta 12 release preparation.

## Database status

- Storage is local SQLite in the application data directory (`knownfirst.db3`).
- On `master`, `DatabaseSchema.CurrentVersion` and `PRAGMA user_version` are **7**.
- On the unmerged Slice-6 branch, `DatabaseSchema.CurrentVersion` is **8** and Schema 8 is active.
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
- Import UI, localized preview/result handling, and final end-to-end release validation are deferred to Slice 9.
- Tooling-only improvements (such as PR #37) do not create a new Beta 13 product release.

This document does not claim public-release readiness or draw legal conclusions about license/attribution compliance; those remain open review items tracked in [ROADMAP.md](ROADMAP.md).

## Active development

The stable master baseline is `d53ffe3d92e249e8bc2f191d1b5cc8b9e81681dc` (PR #43 merged), carrying source version `1.0.0-beta.12` (build 12). `DatabaseSchema.CurrentVersion` is **8** and Schema 8 is active for real application databases on master.

**KF-MEANING-001 Slice 8 — transactional Schema-8 populated-target merge writer and Import routing** is implemented and validated as unmerged feature-branch work on `feature/meaning-slice8-core-merge-writer` (checkpoint commit `783f83c9df99de6fa016f1260f95616ca96d7699`), with a complete result of 1593 passed, 0 failed, 0 skipped. Manual merge is pending.

Verified Slice-8 behavior on that branch:

- `PortableMergeWriter` — transactional populated-target merge writer that validates the merge plan, rejects stale or non-executable plans, and atomically commits or rolls back.
- Stable identity resolution using explicit source-local-ID-to-target-ID maps; source integer IDs are never target identities.
- Existing domain entities are reused; missing entities and preserved variants are inserted; enrichment policies are applied.
- Sense-addressed meanings, contexts, answer variants, assignments, progress, cards, reviews, sessions, queues, and review/preparation workflows are merged and preserved.
- Multiple Senses for one Word remain independent.
- Failure and cancellation roll back the complete merge; reimport converges without duplicates; merged review history becomes authoritative.
- Card scheduling is replayed through the existing scheduler in deterministic order (ReviewedAtUtc, then fingerprint ordinally); replay changes only derived fields and does not repoint Sense, PreferredMeaning, or Direction.
- Import routing: empty targets use restore-into-empty; populated Schema-8 targets use validation → preflight → validated safety copy → transactional writer.
- Archive-v1 upgrades in memory for Schema 8; archive-v2 is supported natively; archive-v2 into Schema 7 remains rejected.
- Fully duplicate imports return successful no-change without safety copy or writer invocation.
- Non-seekable source streams are supported; stable errors are preserved; `PortableImportResult` exposes backward-compatible disposition and summary.

Slice 9 (Import UI, localized preview/result handling, and final end-to-end release validation) remains unimplemented.
