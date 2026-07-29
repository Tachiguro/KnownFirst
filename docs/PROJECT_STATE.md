# KnownFirst project state

**Status date:** 2026-07-29
**State source:** `master` (`b405aa49001538ac60f45dc9697d9308e48e9eb2`, PR #33 merge commit)
**Next product milestone:** Beta 12 Internal Testing validation and packaging

This document is the authoritative snapshot of verified current state. Update it when a milestone is completed or when a release, schema, supported platform, or confirmed limitation changes. Plans belong in [ROADMAP.md](ROADMAP.md).

## Stable release & source identity

| Field | Verified value |
| --- | --- |
| Project | KnownFirst |
| Source Version | `1.0.0-beta.12` (build 12, merged PR #32 `b5e4b05` / PR #33 `b405aa4`) |
| Package ID | `com.tachiguro.knownfirst` |
| Target Distribution | Google Play Internal Testing |
| Distribution Note | Source version is `1.0.0-beta.12` on `master`. Merged source code does not imply that a Beta 12 AAB package has been compiled, signed, uploaded, or distributed. |

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

**Dormancy Boundaries:**
- The active database schema remains **7** (`PRAGMA user_version = 7`).
- Schema 8 is dormant and is not invoked during normal application initialization.
- Populated-target merge writing, import routing to populated databases, and Schema-8 activation remain unexecuted future work.

## Confirmed verification

### Automated

- **Beta 10 Release Candidate:** 698 passing tests (historical evidence pre-580bdcd).
- **PR #33 Validation:** 1190 passing tests recorded on feature branch prior to master merge (historical evidence for PR #33).
- Note: Automated tests cover Core policies, text analysis, temporary SQLite persistence, workflow logic, localization, diagnostics, lookup providers with offline fixtures, and archive contracts. Automated tests do not make live network requests.

### Platform builds

- **Windows / Android Debug & Release:** Build readiness verified during Beta 10 and Beta 11 release preparation. Fresh master verification is performed per prompt authorization.

## Database status

- Storage is local SQLite in the application data directory (`knownfirst.db3`).
- Current `PRAGMA user_version` is **7** (unchanged).
- Initialization is forward-oriented and preserves existing rows while adding supported tables or columns.
- Initialization reads `PRAGMA user_version` first and rejects a version greater than 7 before modifying tables or cache.
- Complete persisted-data rules are in [DATABASE_CONTRACT.md](DATABASE_CONTRACT.md).
- Portable recovery format v1 is documented in [architecture/backup-format-v1.md](architecture/backup-format-v1.md).

## Known limitations

- Portable recovery import is accepted only into an empty installation; a populated target is refused, never merged or overwritten.
- Populated-database merge import is not implemented.
- Exported `.kfarchive` archives are not encrypted and may contain personal imported text and learning history; users are warned before export.
- "Support KnownFirst" and "Report a bug" controls in Settings are placeholders and not yet functional.
- Full deterministic GUI automation is not yet merged to master (local development branch `feature/windows-gui-test-launcher-v1` pending verification).
- Cloud synchronization, accounts, analytics, advertising, and payments are not implemented.
- Offline dictionary packages and FSRS scheduling are deferred.
- Online lookup requires explicit consent and network access on cache misses.
- Public Google Play release is intentionally not yet pursued.

This document does not claim public-release readiness or draw legal conclusions about license/attribution compliance; those remain open review items tracked in [ROADMAP.md](ROADMAP.md).

## Active development

The stable master baseline is `b405aa49001538ac60f45dc9697d9308e48e9eb2` (PR #33 merged), carrying source version `1.0.0-beta.12` (build 12).

A separate, unmerged local branch `feature/windows-gui-test-launcher-v1` (`7704118`) contains GUI test launcher profile-isolation contracts and script updates awaiting real PowerShell `StartupSmoke` verification.
