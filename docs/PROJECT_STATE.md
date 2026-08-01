# KnownFirst project state

**Status date:** 2026-08-01
**State source:** `master` (`e1724651dd7d4d3ed427b84a96da3d909d0c72ed`, PR #41 merge commit)
**Next product milestone:** KF-MEANING-001 Slice 7 — Schema-8 MergePreflight adaptation

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
- portable recovery import of a `.kfarchive` archive into an empty installation only (native Open dialog on Windows and Android); populated targets are refused, not merged or overwritten;
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
- **Meaning Slice 4 (PR #40):** dormant direction-specific answer assignments and progress replay; verified with 1347 passed, 0 failed, 0 skipped.
- **Meaning Slice 5 (PR #41):** dormant Sense-addressed learning cards, frozen queue targets, and permanent-known cleanup; verified with 1364 passed, 0 failed, 0 skipped.
- **Windows GUI StartupSmoke Launcher (PR #35):** `-Action GuiTest` launcher entry point and profile isolation under `artifacts/`.
- **New-Chat Bootstrap Protocol (PR #36):** permanent dynamic bootstrap governance in `docs/NEW_CHAT_BOOTSTRAP.md`.
- **Google Play Packaging Safeguards (PR #37):** hardened `scripts/publish-google-play-bundle.ps1` with cross-process lock, warning escalation, candidate ownership, and sidecar verification.

**Dormancy Boundaries (master):**
- On `master` the active database schema remains **7** (`PRAGMA user_version = 7`).
- On `master` Schema 8 is dormant and is not invoked during normal application initialization.
- Schema-8 activation is implemented on the unmerged Slice-6 branch (see [Active development](#active-development)).
- Populated-target merge writing and import routing to populated databases remain unexecuted future work.

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

- Portable recovery import is accepted only into an empty installation; a populated target is refused, never merged or overwritten.
- Populated-database merge import is not implemented.
- Exported `.kfarchive` archives are not encrypted and may contain personal imported text and learning history; users are warned before export.
- "Support KnownFirst" and "Report a bug" controls in Settings are placeholders and not yet functional.
- Cloud synchronization, accounts, analytics, advertising, and payments are not implemented.
- Offline dictionary packages and FSRS scheduling are deferred.
- Online lookup requires explicit consent and network access on cache misses.
- Public Google Play release is intentionally not yet pursued.
- Tooling-only improvements (such as PR #37) do not create a new Beta 13 product release.

This document does not claim public-release readiness or draw legal conclusions about license/attribution compliance; those remain open review items tracked in [ROADMAP.md](ROADMAP.md).

## Active development

The stable master baseline is `e1724651dd7d4d3ed427b84a96da3d909d0c72ed` (PR #41 merged), carrying source version `1.0.0-beta.12` (build 12). **KF-MEANING-001 Slice 6 — Schema-8 activation** is implemented and validated as unmerged feature-branch work on `feature/meaning-slice6-schema8-activation`, with a focused result of 466 passed, 0 failed, 0 skipped and a complete result of 1542 passed, 0 failed, 0 skipped. Manual merge is pending.

Verified Slice-6 behavior on that branch:

- `DatabaseSchema.CurrentVersion` is **8** and Schema 8 is active for real application databases.
- A fresh database initializes directly to a validated Schema 8.
- Supported versions 0–6 reach the Schema-7 baseline boundary and are then migrated to Schema 8 in the same initialization; a version-7 database migrates to Schema 8 directly.
- A valid version-8 database is validation-only on reopen and is never mutated.
- Malformed Schema-8 databases and databases newer than version 8 fail closed without repair.
- The migration is transactional, rollback-safe, cancellation-safe, and retryable; a failed attempt leaves the source database byte-for-byte unchanged.
- Structural validation covers tables, columns, declared nullability and primary-key semantics, legacy artifacts, index definitions, enum domains, ownership, queue/review answer-variant targets, and persisted relationships.
- Legacy enum backfills normalize pre-Schema-7 rows to deterministic supported values before activation.
- `DashboardService` and `TextReviewService` use Schema-8 semantics via validated schema-capability resolution.
- Schema-8 archive export and merge safety copies use archive format v2; format v1 remains readable and can restore into an empty Schema-8 target.
- Import into a populated target remains refused.
- `MergePreflightService` intentionally fails closed on a Schema-8 target with `merge-preflight-schema8-adaptation-required`, pending Slice 7.

The populated-target merge writer and Import routing remain unimplemented.
