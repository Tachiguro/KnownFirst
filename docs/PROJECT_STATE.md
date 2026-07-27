# KnownFirst project state

**Status date:** 2026-07-25
**State source:** `master` (`7076aa4ac0617bd55ff97821ea69dc6b0e1228b0`, PR #22 merge commit)
**Next product milestone:** Beta 12 hotfix (Russian-translation-target defect fix and removal of the combined lookup choice)

This document is the authoritative snapshot of verified current state. Update it when a milestone is completed or when a release, schema, supported platform, or confirmed limitation changes. Plans belong in [ROADMAP.md](ROADMAP.md).

## Stable release

| Field | Verified value |
| --- | --- |
| Project | KnownFirst |
| Source Version | `1.0.0-beta.11` (build 11, merged PR #22 `7076aa4`) |
| Package ID | `com.tachiguro.knownfirst` |
| Distribution | Google Play Internal Testing |

## Supported platforms

- **Android:** distributed through Google Play Internal Testing; minimum Android version is API 24 (Android 7.0).
- **Windows:** primary local development and automated/manual verification platform.
- **iOS:** deliberately removed from the project and not supported.
- **Mac Catalyst:** deliberately removed from the project and not supported.

## Production capabilities

The current product implements:

- English and German UI localization with persisted System, Light, and Dark appearance modes;
- exact text import with deterministic Unicode-aware sentence and vocabulary analysis;
- resumable Known/Unknown vocabulary review with persisted decisions and Undo;
- language-scoped vocabulary identity and global minimal known-word markers;
- frequency-prioritized automatic or manual preparation;
- explicit online-lookup consent, read-only Wiktionary lookup with automatic fallback to Wikipedia definitions, and a local SQLite lexical cache;
- source attribution, alternative-meaning selection, manual correction, and context snapshots;
- recognition and spelling card directions with independent deterministic schedules;
- resumable learning sessions and permanent-known cleanup;
- portable `.kfarchive` data export (native Save dialog on Windows and Android);
- portable recovery import of a `.kfarchive` archive into an empty installation only (native Open dialog on Windows and Android); populated targets are refused, not merged or overwritten;
- a one-time localized What's New notice shown once per version;
- transactional local persistence, startup maintenance, and bounded structured diagnostics;
- responsive Windows and Android layouts with localized workflow gating.

## Confirmed verification

### Automated

**Full automated suite:** 698 passing tests executed during Beta 10 release-candidate preparation before the final argument-binding commit (`580bdcd`). Automated tests cover Core policies, text analysis, temporary-SQLite persistence and migration, review/preparation/learning workflows, localization, diagnostics, build identity, UI markup contracts, Wikipedia/Wiktionary lookup providers with offline fixtures, and the portable backup format (export, checksum, rollback, resource limits). Automated tests do not use live Wikimedia requests.

### Platform builds

**Windows:** Debug build passed; Release build passed (executed during Beta 10 RC prep, pre-580bdcd).

**Android:** Debug build passed (`-m:1` serial); Release build passed (`-m:1` serial); signed Release APK was successfully created, signature-verified, and used for the subsequent manual installation (pre-580bdcd).

### Manual Android confirmation

The localized Beta 10 What's New notice was manually confirmed to appear on a real Android installation. This is a point-observation of the What's New feature, not a complete device GUI test, Google-Play installation-path validation, or full export/import workflow confirmation per [GUI_TEST_MATRIX.md](GUI_TEST_MATRIX.md) and [BETA_TESTING.md](BETA_TESTING.md).

## Database status

- Storage is local SQLite in the application data directory (`knownfirst.db3`).
- Current `PRAGMA user_version` is **7** (unchanged since Beta 9).
- Initialization is forward-oriented and preserves existing rows while adding supported tables or columns.
- Initialization reads `PRAGMA user_version` first and rejects a version greater than 7 before modifying tables or cache.
- Complete persisted-data rules are in [DATABASE_CONTRACT.md](DATABASE_CONTRACT.md).
- Portable recovery format v1 is implemented in a narrower scope than the original architecture proposal; see [architecture/backup-format-v1.md](architecture/backup-format-v1.md) for the current binding contract.

## Known limitations

- Portable recovery import is accepted only into an empty installation; a populated target is refused, never merged or overwritten.
- No `ReplaceAll`-style restore into a populated installation exists yet.
- Exported `.kfarchive` archives are not encrypted and may contain personal imported text and learning history; users are warned before export.
- "Support KnownFirst" and "Report a bug" controls in Settings are placeholders and not yet functional.
- Full deterministic GUI automation (Appium/UiAutomator2 or equivalent) is not yet implemented; visual acceptance remains manual.
- Cloud synchronization, accounts, analytics, advertising, and payments are not implemented.
- Offline dictionary packages and FSRS scheduling are deferred.
- Online lookup requires explicit consent and network access on cache misses.
- Public Google Play release is intentionally not yet pursued.

This document does not claim public-release readiness or draw legal conclusions about license/attribution compliance; those remain open review items tracked in [ROADMAP.md](ROADMAP.md).

## Active development

The stable master baseline is `7076aa4ac0617bd55ff97821ea69dc6b0e1228b0` (PR #22 merged), carrying product version `1.0.0-beta.11` (build 11).

**Confirmed defect (KF-LANG-001):** Beta 11 exposed Russian as a valid translation target, but `TextReviewService` incorrectly treated `ExplanationLanguage`/`TargetLanguage` as a source-language capability, validating both against a local English/German-only set and rejecting `ru` before the lexical lookup started — even though `LexicalLookupLanguagePolicy` already supported Russian as a translation target. German-to-Russian and English-to-Russian imports were therefore incorrectly rejected.

A hotfix is being prepared on the unmerged branch `hotfix/beta-12-russian-translation` (base `7076aa4`): the duplicated, incorrect validation in `TextReviewService` was removed in favor of `LexicalLookupLanguagePolicy.Validate` as sole authority; the combined Definition-and-Translation lookup choice was removed from the Import Text selector (existing/legacy combined data remains readable); and the product identity was bumped to `1.0.0-beta.12` (build `12`) with a localized Beta 12 What's New entry. Russian **source**-text support remains explicitly deferred. This package is intended for Google Play Internal Testing, including testing by the user's father, once validated. Public release remains blocked by the outstanding support/bug-report, GUI-automation, legal, and store-readiness work below. See [CURRENT_WORK.md](CURRENT_WORK.md) for exact branch status.

## Immediate action

- Run local validation (`scripts\knownfirst.ps1`) on the Beta 12 hotfix branch `hotfix/beta-12-russian-translation`; once tests and builds pass, commit/push/open a PR (no AAB has been created or uploaded).

## Next milestones (Future Work)

1. Validate and merge the Beta 12 hotfix branch; no AAB or Play upload until separately authorized.
2. Functional support/bug-report surface.
3. Reopenable release notes and release-note history.
4. Deterministic GUI automation (Android first).
5. Public-release readiness review.
