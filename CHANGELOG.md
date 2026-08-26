# Changelog

All notable user-visible changes to KnownFirst are documented in this file.
The structure follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/)
and uses the application's prerelease version identifiers.

## [Unreleased]

### Added

- Dedicated first-run onboarding experience guiding new installations through initial application language selection, optional local display name, core learning concepts, online dictionary lookup consent, term recognition, study practice directions, daily learning pace, and learning day timing.
- Resumable onboarding: setup progress is persisted locally across application restarts, automatically resuming at the current step and completing into the main application.
- Optional local Display Name configurable during onboarding or in Settings, stored strictly locally on the device.
- Personalized Home greeting displaying a localized welcome message ("Welcome, {0}.", "Willkommen, {0}.", "Добро пожаловать, {0}.") before the subtitle when an optional local Display Name is configured, while preserving the unchanged KnownFirst heading and the subtitle-only presentation when no name is set.
- Daily new-word budget presets (`1`, `5 Recommended`, `10`, and `Custom`) with an expanded technical range (`1..50`), updated recommended default of `5` new words per day, and a non-blocking study workload advisory for custom values above `15`.

### Changed

- Reconciled First-Run Onboarding with Settings: UI-language selection now uses a native select matching Settings, Online Dictionary lookup requires an explicit Enable or Keep Disabled choice before continuing, Display Name presents an explicit Skip action when left empty, and onboarding now includes explanatory guidance for Enhanced Term Recognition, practice modes, and a summary notice that settings can be changed later in Settings.
- Default daily new-word budget for fresh installations and default restorations updated from `10` to `5` words per day (existing grandfathered installations preserve their established limit).
- Settings now provides a dedicated Display Name card in third position (Language, Appearance, Display Name, New words per day).
- Simplified manual vocabulary preparation to a single context-appropriate definition or translation field, removing redundant form inputs and collapsing advanced options (acronym expansions, accepted spelling aliases) by default.
- Clarified "End preparation" action styling and placement as a neutral workflow action with explicit confirmation that retains all already-accepted vocabulary.

### Fixed

- Fixed first-run onboarding vertical scroll reachability: `.onboarding-host` now establishes a bounded vertical scroll surface so all onboarding content and actions remain fully reachable on constrained viewport heights without horizontal expansion (PR #169).
- Reconciled shell navigation drawer scroll lock with window breakpoint transitions: resizing across the 800px desktop boundary cleanly dismisses transient drawer state, unlocks background page scrolling, and removes stale back-navigation handlers (PR #168).
- Normalized Home greeting responsive layout and spacing when an optional local Display Name is configured (PR #167).
- Fixed manual vocabulary preparation failure where saving user-entered definitions or translations without an automatic dictionary lookup result was incorrectly rejected (PR #160).
- Fixed recovery after successful item saving: if loading the next preparation candidate encounters an error, the saved item is not misreported as a save failure, and retrying advances directly without repeating the save (PR #160).

### Internal

- Advanced release-preparation candidate build number from `14` to `15` for the upcoming Beta 13 Google Play package and reconciled durable release records (KF-RELEASE-002).

## [1.0.0-beta.13] - 2026-08-23 (merged source — next Internal Testing candidate, not yet gated or distributed)

**`1.0.0-beta.13` / build `13` is merged source on `master` via PR #92 through PR #145 (current synchronized master `35cd15bf3e0dda0816bb2dd2c9dc35fa91ddd430`). Merging is not a build, package, signing, device-validation, or distribution event: `1.0.0-beta.13` has not passed the mandatory exact-candidate Pre-AAB Release-Readiness Gate, and has not been packaged as an AAB, signed, installed on a device, uploaded to Google Play, or distributed to testers. The last confirmed externally distributed and device-tested release remains `1.0.0-beta.12` / build `12` (2026-07-30); see [docs/releases/1.0.0-beta.12.md](docs/releases/1.0.0-beta.12.md) and [docs/releases/1.0.0-beta.13.md](docs/releases/1.0.0-beta.13.md) for the Beta-13 evidence record.**

### Added

- German Enhanced Term Recognition: production offline German morphology lexicon (`german-lexicon.v2.kfgl`) integrated into the text-analysis pipeline, supporting conservative 2–4 component compound decomposition and derivation analysis (PR #134, PR #135).
- Review Words now displays a minimal, always-visible source-compound notice for derived German review candidates, identifying the parent compound directly from the card list without opening metadata panels (PR #140).
- Retained German derived-term provenance (Schema-11 `DerivedTermEvidence`) travels through portable backup/restore and cross-installation merge in archive format V2, preserving source-compound context across devices (PR #135, PR #137).
- Daily new-word limit ($N \in \{5, 10, 20, 30, 50\}$, default 10) with Schema-12 persistence (`LearningDayState`, `LearningDayGrants`), logical learning-day boundary, and active-session rollover reconciliation (PR #142).
- Settings now provides a comprehensive settings surface:
  - Daily new-word limit selector ($N \in \{5, 10, 20, 30, 50\}$).
  - Learning timezone configuration: System device timezone or 50 curated IANA zones spanning inhabited UTC-11 through UTC+14 with dynamic DST-aware labels.
  - Deterministic 24-hour learning-day cutoff selector (`00..23` hour and `00..59` minute selectors, formatted as `HH:mm`).
  - Enhanced term recognition toggle (default ON).
  - Default-first option layouts for Card directions and Learning mode.
  - Distinct "Restore default settings" (non-destructive reset of preferences and defaults, preserving online dictionary consent and learning state) and "Reset all application data" (destructive complete data reset) (PR #144).
- Settings now provides a functional "Report a bug" action under Help and Support. It opens the user's email application with the support address (`Tachiguro+KnownFirst_BugReport@gmail.com`), a versioned subject, and structured template prompts while including only safe technical metadata and never sending automatically. If the email application cannot be opened, a fallback action allows copying the support address to the clipboard.
- Settings now provides a reopenable release-note history under Help and Support. Earlier release notes can be viewed again at any time, including after the one-time What's New notice has been dismissed, and they are listed newest first (Milestone 14B, PR #73).
- Portable import preview UI with read-only preview before confirmation, distinguishing restore (empty target), merge (populated target), and no-change (duplicate import) cases; localized EN/DE/RU coverage for preview, result, and failure handling (PR #45).
- Schema-10 portable export now carries a supported Active learning session together with its persisted queue and committed review history. Restoring into an empty installation resumes the session from its last durably committed state (KF-BACKUP-005A/005B, PR #79/#81). Restoring into a populated, learning-quiescent installation additively merges that Active session using the same preview/merge/no-change safety contract already established for Completed content, including exact same-workflow convergence to no-change and a non-executable, zero-mutation outcome for any conflicting Active state (KF-BACKUP-005C, PR #83).
- Windows portable ZIP packaging foundation: `scripts/packaging/publish-windows-portable.ps1` and canonical launcher action `WindowsPortablePackage` producing a self-contained, unpackaged x64 Release ZIP archive with a SHA-256 sidecar under `artifacts/windows-portable/` as a manual replacement channel.
- Windows MSIX packaging foundation: `scripts/packaging/publish-windows-msix.ps1` and canonical launcher action `WindowsMsixPackage` producing an x64 Release MSIX package with a SHA-256 sidecar under `artifacts/windows-msix/`, with unsigned default and optional certificate thumbprint signing via external environment variable (`KNOWNFIRST_WINDOWS_MSIX_CERT_THUMBPRINT`).
- Shared distribution helper `scripts/packaging/windows-distribution-common.ps1` providing a single source of truth for artifact naming, signing markers, Store identity classification, candidate selection, and ZIP file-entry counting.
- A localized Beta 13 What's New entry (English, German, Russian) covering populated-target backup merge, strengthened backup/import safety, portable learning-session resume, and the reopenable release-note history.

### Fixed

- Windows portable data export no longer opens or truncates the selected destination before the archive is generated. The archive is now staged to a same-directory temporary file, validated through the production archive validator, and only then atomically finalized (`File.Replace` for an existing destination, `File.Move` for a nonexistent one); a failure at any stage before finalization leaves an existing backup byte-for-byte unchanged (PR #48).
- Android portable export stages and strictly validates the archive before opening the destination picker; invalid or failed staging never acquires or writes the destination (PR #50).
- An invalid preparation context is now hidden rather than silently accepted during preparation selected-meaning acceptance (PR #46).
- Diagnostics and export now read `PreparationCandidates.ResultJson` via the payload codec instead of a stale reader, correcting a defect in the diagnostics/export path (PR #47).
- **Priority 15 — Portable merge integrity hardening (complete and binding since this candidate):** a sequence of populated-target merge/export-ordering corrections closes archive-emission and merge-identity defects that could misclassify or non-canonically order emitted archive content across otherwise-equivalent installations. This includes Schema-9 `LearningReview` merge-key collision correction (KF-BACKUP-004, PR #77), Schema-9 portable workflow canonical export ordering (KF-BACKUP-003 Package D, PR #76), `LegacyReviewSummaries` canonical ordering (PR #85), `Learning.Cards`/Sense-`StableId` canonical ordering (PR #87), and the `Occurrence` action-key lookup collision correction that completed Priority 15 (PR #89). None of these changed the archive format, database schema, or public merge outcome contract; they close rare non-canonical-ordering and misclassification defects rather than any observed data loss.

### Changed

- Database schema advances to **12** (`PRAGMA user_version = 12`), adding immutable `StableId` columns in Schema 10, `DerivedTermEvidence` in Schema 11, and `LearningDayState`/`LearningDayGrants` in Schema 12. The `.kfarchive` outer format remains **V2**; no archive V3 was introduced.
- Enhanced Term Recognition missing-preference default resolves to ON (`EnhancedTermRecognitionPolicy.DefaultEnabled = true`), while explicitly persisted `false` remains OFF (PR #144).
- Windows MSIX packaging maps `ApplicationDisplayVersion` to `1.0.<BuildNumber>` and `ApplicationVersion` to `0` to produce Store-compliant four-part version `1.0.13.0` while preserving application runtime product identity (`1.0.0-beta.13` / build `13`).
- Packaging build and intermediate outputs are isolated under `artifacts/build/` and `artifacts/obj/` so normal `bin/Release/` outputs backing validation evidence remain untouched.
- `LearningSession` identity now includes `StartedAtUtc`, `CompletedAtUtc`, ordered queue digest, and per-item `Rating`, so distinct real sessions using the same card set no longer collapse into one (PR #45).
- Schema 9 activates completed review-session history storage by replacing the unconditional `ReviewSessions(DocumentId)` uniqueness rule with one-Active/multiple-Completed index semantics (PR #51).
- Package A adds deterministic Schema-9 completed-review identities, preflight classification, duplicate rejection, and target-index parity (PR #52). Writer-level convergence (Package B, PR #65) and two-installation cross-convergence hardening (Package C, PR #68) are both now complete and merged.
- Product version raised to `1.0.0-beta.13` (build `13`). Package ID (`com.tachiguro.knownfirst`), signing configuration, and the portable archive outer format are unchanged.

### Internal

- An Android-only automated GUI-navigation test foundation (P16-A) was merged as source infrastructure under a dedicated, isolated `com.tachiguro.knownfirst.guitest` identity (PR #91). It is test tooling, not a product feature: it has not been runtime-executed, does not automate any row of the GUI test matrix, and establishes no Android platform build, packaging, or device evidence.
- The unfinished "Support KnownFirst" and "Report a bug" placeholder controls and their shared placeholder-handler behavior were removed from the production Settings source (Milestone 14A, PR #71); they were not rendered to users before removal and this is a source-cleanliness change, not a user-facing behavior change.
- The canonical Google Play packaging script now treats an absent post-clean Android Release output directory as zero stale candidates rather than raising a terminating error, with isolated behavioral regression tests (PR #101).
- Documentation-governance packages D1-D5 reconciled `CURRENT_WORK.md`, `PROJECT_STATE.md`, `ROADMAP.md`, and related contracts with merged product state on an ongoing basis (PRs #53-#64 and subsequent per-package closures).
- German Enhanced Term Recognition text-analysis pipeline, offline morphology lexicon, and characterization coverage foundations were merged across PRs #128, #129, #130, #131, #134, #135, #137, and #140.
- Daily new-word budget infrastructure and Settings GUI Slice 2A were merged across PRs #142, #144, and #145.

## [1.0.0-beta.12] - 2026-07-30 (confirmed distributed)

**Beta 12 distribution through Google Play Internal Testing and physical Android testing were confirmed by the user on 2026-07-30. The exact original rollout date is unknown. The installed package displayed commit `cfbaee6a` and `DIRTY`, so the exact source commit is unverified. See [docs/releases/1.0.0-beta.12.md](docs/releases/1.0.0-beta.12.md) for details.**

### Fixed

- Russian translation targets now work correctly for German-to-Russian and English-to-Russian text imports. `TextReviewService` previously re-validated `ExplanationLanguage`/`TargetLanguage` against a local English/German-only set and rejected `ru` before the lexical lookup started, even though `LexicalLookupLanguagePolicy` already supported Russian as a translation target. The duplicated, incorrect validation was removed; `LexicalLookupLanguagePolicy` is now the sole authority for source/target language capability.
- Android portable data export no longer reports a false failure after a successful write. `AndroidPortableArchiveFileService` verified the saved destination with `Stream.Length`, which non-seekable Android content-provider streams can throw on even though the archive was written correctly. Verification now opens the destination and reads a single byte instead, which works for both seekable and non-seekable streams. This was a verification defect, not archive corruption.
- The Settings portable-import confirmation no longer overlaps the Data Export/Data Import actions. The normal action row is now hidden while the confirmation panel is visible and is restored on Cancel, on validation failure, or after the import completes.
- Home and the burger menu now refresh immediately after a successful portable import or a successful full data reset, instead of requiring navigation to another page first.

### Changed

- Text import now offers only Definition or Translation. The combined Definition-and-Translation choice has been removed from the Import Text selector; existing database rows, preparation state, and portable archives that already use the combined mode continue to be read and processed unchanged.
- Product version raised to `1.0.0-beta.12` (build `12`). Package ID, database schema (`7`), signing configuration, and the portable archive format are unchanged.

### Added

- A localized Beta 12 What's New entry (English, German, Russian) covering the Russian-translation-target fix, the simplified Definition/Translation import choice, and the continued absence of Russian source-text analysis.
- Settings now offers an explicit "Activate online dictionary" action, with the binding online-lookup disclosure, when consent has not been granted. Portable archives continue to exclude online-lookup consent and preferences; importing an archive or resetting local data does not grant or restore consent. Users must grant consent independently after installation or restore.

## [1.0.0-beta.11] - 2026-07-25 (merged via PR #22)

*Intended for Google Play Internal Testing, including testing by the user's father.*

### Added

- Russian application-interface localization, with a Language setting offering System, English, Deutsch, and Русский. System automatically follows the device language on every start; Russian devices resolve to Russian, and unsupported device languages fall back to English.
- Russian as a translation target for English and German imported texts. Russian is not yet available as a source (imported-text) language.
- A localized Beta 11 What's New entry (English, German, Russian) covering the Russian UI, the System-language behavior, Russian as a translation target, learning-card direction display, the Again-repeat badge, and the continued absence of Russian source-text analysis.

### Changed

- The Learn screen now shows a small, secondary direction label (Term → meaning / Meaning → term) and a "Repeat" badge when a card is the one-time re-appearance after an Again rating, so a legitimate repeat or opposite-direction card is no longer visually indistinguishable from a first-time card ([KF-LEARN-001](docs/BACKLOG.md)).
- Product version raised to `1.0.0-beta.11` (build `11`). Package ID, database schema (`7`), signing configuration, and the portable archive format are unchanged.

### Internal

- Added an internal `docs/BACKLOG.md` for solo-development bugs and small UX improvements, replacing GitHub Issues for now.
- Russian UI/translation-target support was implemented on `feature/russian-language-support-v1` and merged via PR #20. Russian source-text import, Cyrillic tokenization/normalization, Russian Wiktionary language-section parsing, and Russian Wikipedia fallback remain deferred to a separate milestone. Native-speaker review of the Russian wording has not yet been performed.
- Investigated [KF-LEARN-001](docs/BACKLOG.md) (duplicate-looking learning question): confirmed no accidental card/queue duplication in `LearningService`; the UX clarity fix was implemented on `feature/learning-repeat-direction-clarity` and merged via PR #21.
- Public release remains blocked by the placeholder Support/Report-a-bug controls, missing deterministic GUI automation, and outstanding legal/store-readiness review; see `docs/CURRENT_WORK.md`.

## [1.0.0-beta.10] - 2026-07-25

### Added

- Portable Data Export: save your data to a `.kfarchive` file using the native Save dialog on Windows and Android. Exported archives may contain personal imported text and learning history and are not encrypted; store and share them carefully.
- Portable Data Import: load a `.kfarchive` file into a new, empty KnownFirst installation using the native Open dialog. Import into an installation that already has data, and merging existing data, are not yet supported.
- A one-time in-app What's New notice that appears once per version with release notes and can be dismissed.

### Changed

- Aligned source-controlled application identity to `1.0.0-beta.10` / build `10` across Windows and Android Debug, Diagnostic, and Release configurations, and established binding repository versioning governance.
- Wiktionary remains the primary lexical provider. Wikipedia is attempted once only after deterministic final Wiktionary `NotFound`.
- Only `Definition` and `DefinitionAndTranslation` are eligible for fallback; `Translation`-only is not eligible.
- `Timeout`, rate limit, network/transient failure, `ParseFailure`, `PermanentFailure`, and caller cancellation do not trigger fallback.
- Wikipedia provides definitions or encyclopedic context, not translations.
- Wikipedia metadata and provider-specific cache identity are preserved.
- Rendered trusted Wiktionary (`.wiktionary.org`) and Wikipedia (`.wikipedia.org`) source-page titles as HTTPS hyperlinks and recognized Creative Commons Attribution-ShareAlike 4.0 International (CC BY-SA 4.0) licenses as clickable license links (`https://creativecommons.org/licenses/by-sa/4.0/`) with `target="_blank"` and `rel="noopener noreferrer"`.
- Updated online lookup privacy disclosure (`Prepare_OnlineDisclosure`) in English and German to state that Wiktionary is queried first, Wikipedia is queried only for definitions after a Wiktionary `NotFound`, Wikipedia does not provide translations, standard network metadata is transmitted, and data remains local.
- Updated Wikipedia result attribution string to explicitly identify CC BY-SA 4.0 and state that excerpts are converted to plain text, normalized, and may be truncated by KnownFirst.

### Internal

- Added binding architecture plan (`docs/plans/structured-vocabulary-import-and-sense-learning.md`) defining the vision, requirements, open design questions, data model options, PDF pipeline, sense-level learning progression, sync domain model, and multi-phase roadmap for structured vocabulary list import, sense-level knowledge, and Linux host feasibility.
- Wikipedia fallback behind Wiktionary is implemented on PR #11. If the primary Wiktionary lookup returns a clean NotFound, the system gracefully queries Wikipedia in a separate lookup context, preserving essential redirect metadata without introducing provider cycles or requiring schema migrations. The orchestration accurately handles explicit transient failures, parse errors, and caller cancellation without fabricating Wikipedia entries or leaking exceptions.
- Implemented `WikipediaLookupProvider` as an explicitly selectable lexical lookup provider. It leverages the Wikipedia JSON API client foundation and maps encyclopedic context into standard domain definitions. It enforces deterministic identity and empty metadata boundaries without generating fabricated dictionary items.
- Added a low-level, source-generated Wikipedia JSON API client (`IWikipediaApiClient`). This client implements robust text extraction, redirect chains, and rate limiting.
- Added provider-neutral routing foundation (`ILexicalLookupProvider` and
  `ILexicalLookupProviderResolver`) to allow safe resolution of dictionary
  providers without hardcoded instantiation.
- Updated `LexicalEnrichmentService` to safely resolve the requested provider
  and return a permanent failure (`provider-not-registered`) for unknown providers.
- Enforced strict provider identity matching to prevent caching misaligned results.

- Removed the unsupported iOS and Mac Catalyst application targets, platform
  folders, Apple-only diagnostics branches, and local Apple launch settings.
- Android and Windows are now the only active application target platforms.

- Added immutable version-1 backup data contracts, explicit external enum
  mappings, centralized format limits, and privacy-safe stable error codes.
- Added strict source-generated JSON metadata and typed UTF-8 codecs for the
  future `manifest.json` and `data.json` payloads without reflection fallback.
- Hardened database initialization so an unsupported future schema is rejected
  before any table, cache, or version mutation.
- ZIP/archive creation, database snapshotting, usable backup, restore, file
  selection, and backup/restore UI remain unimplemented and unreachable.

## [1.0.0-beta.8] - 2026-07-22

### Added

- No new user-facing features. Beta 8 is a release-stability update.

### Changed

- No intentional workflow or persisted-data-model changes from Beta 7.

### Fixed

- Fixed the Android Release crash during automatic online vocabulary lookup.
- Replaced reflection-dependent JSON serialization in the Release AOT path
  with source-generated serialization metadata.
- Replaced the AOT-unsafe CSS-selector path in the Wiktionary parser.

### Known limitations

- Versioned backup and restore are not implemented.
- Cloud synchronization is not implemented.
- A dictionary cache miss requires a network connection and explicit Wikimedia
  lookup consent.
- iOS and Mac Catalyst are intentionally not supported product platforms; their
  targets and platform folders were removed in the current platform-cleanup
  branch.
- The direct-install Android test-package script still uses legacy Beta 6
  artifact labels and must not be used to claim a Beta 8 package.

See the [Beta 8 release notes](docs/releases/1.0.0-beta.8.md) for release
identity and verification evidence.
