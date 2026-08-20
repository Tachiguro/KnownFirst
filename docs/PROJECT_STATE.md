# KnownFirst Project State

**Status date:** 2026-08-20
**State source:** Synchronized `master` baseline. Authoritative live Git and PR state are discovered dynamically per [docs/NEW_CHAT_BOOTSTRAP.md](NEW_CHAT_BOOTSTRAP.md).

This document records stable, verified architectural facts and current capabilities. Plans belong in [ROADMAP.md](ROADMAP.md); active operational task state belongs in [CURRENT_WORK.md](CURRENT_WORK.md).

## Stable Release & Source Identity

| Field | Verified value |
| :--- | :--- |
| **Project** | KnownFirst |
| **Source Version (`master`)** | `1.0.0-beta.13` (build 13) — merged via PR #92 |
| **Active Database Schema** | SQLite `PRAGMA user_version` 10 |
| **Package ID** | `com.tachiguro.knownfirst` |
| **Target Distribution** | Google Play Internal Testing |
| **Distributed Status** | `1.0.0-beta.12` distributed and user-tested (confirmed 2026-07-30; see [docs/releases/1.0.0-beta.12.md](releases/1.0.0-beta.12.md)). `1.0.0-beta.13` has not been distributed. |
| **Installed Displayed Identity** | `1.0.0-beta.12` / Release / Build 12 / Commit `cfbaee6a` (DIRTY) |

## Supported Platforms

- **Android:** Distributed through Google Play Internal Testing; minimum Android version is API 24 (Android 7.0).
- **Windows:** Primary local development and automated/manual verification platform.
- **iOS & Mac Catalyst:** Deliberately removed from the project and not supported.

## Production Capabilities

- English, German, and Russian UI localization with persisted System, Light, and Dark appearance modes;
- exact text import with deterministic Unicode-aware sentence and vocabulary analysis;
- Russian as a translation target for English and German source texts (Russian source text remains deferred);
- simplified Definition or Translation import mode selection;
- resumable Known/Unknown vocabulary review with persisted decisions and Undo;
- language-scoped vocabulary identity and global minimal known-word markers;
- frequency-prioritized automatic or manual preparation;
- explicit online-lookup consent, read-only Wiktionary lookup with automatic Wikipedia definition fallback, and local SQLite lexical cache;
- source attribution, alternative-meaning selection, manual correction, and context snapshots;
- recognition and spelling card directions with independent deterministic schedules;
- Learn screen card direction indicators and visual "Repeat" badges for `IsAgainRepeat` cards;
- resumable learning sessions and permanent-known cleanup;
- portable `.kfarchive` (v2) data export with native Save dialog on Windows and Android;
- recovery import of `.kfarchive` into empty installations with native Open dialog;
- transactional populated-target import with validated pre-merge safety copies, preflight preview, collision-free action keys, and atomic commit-or-rollback (stale plans rejected; re-import converges to `NoChanges`);
- portable Active learning-workflow preservation and resume into empty Schema-10 targets (KF-BACKUP-005B) and populated-target Active-workflow convergence/conflict safety (KF-BACKUP-005C);
- card scheduling replay through existing scheduler in deterministic order preserving Sense, PreferredMeaning, and Direction;
- reopenable release-note history (`/release-notes`) and Help & Support entry point;
- functional "Report a bug" email composer action launching with structured template prompts and clipboard copy fallback;
- one-time localized What's New notice shown once per version;
- transactional local persistence (Schema 10), startup maintenance, and bounded structured diagnostics.

## Development, Tooling & Packaging Foundations

- **Repository Tooling & Path Portability (PR #111):** Organized script hierarchy (`scripts/packaging/`, `scripts/validation/`, `scripts/tools/`) with dynamic root resolution (`$PSScriptRoot` / `__file__`), eliminating fixed clone path dependencies.
- **Safe Artifact Cleanup (PR #110):** Canonical launcher `Clean` and `Clean -Deep` actions with root safety validation, log retention pruning, and protection of user databases and release packages.
- **Windows Distribution Packaging Infrastructure (PR #107, PR #113, PR #114):** Dedicated publishing scripts (`scripts/packaging/publish-windows-portable.ps1`, `scripts/packaging/publish-windows-msix.ps1`) supporting unpackaged win-x64 portable ZIP and MSIX packaging with SHA-256 sidecars, isolated build roots under `artifacts/build/` and `artifacts/obj/`, Store version mapping `1.0.13.0`, and contract test coverage. On 2026-08-16, the canonical `WindowsPortablePackage` action was executed from synchronized `master` commit `9e455d0e03494cac8e713cd4d16c66946124f852`, producing verified self-contained archive `artifacts\windows-portable\KnownFirst-1.0.0-beta.13-build13-win-x64-9e455d0.zip` (SHA-256 `cebd84824aa4e7909edb3a6e83c467573c3e245535ae585b6be832934a45a81e`) with confirmed runtime payload markers.
- **Windows Release Storage Isolation (PR #104):** Test-profile redirection under `artifacts/gui-tests/windows/profiles/` for safe local Gate-12 visual verification without touching real host data.
- **Pre-AAB Gate Test Infrastructure (PR #94, PR #96):** Configuration-sensitive `DefineConstants` test coverage and MSBuild property analysis for Release gating.
- **Android Rendered GUI Automation Foundation (PR #91 / P16-A):** MSBuild intermediate isolation and Release Notes screenshot capture foundation (P16-B/P16-C matrix automation pending).
- **Enhanced Term Recognition Setting Foundation (PR #129):** Persisted application-level `EnhancedTermRecognitionEnabled` setting, default OFF. Internal seam only; no visible Settings UI control or production text-analysis wiring is present in this release.
- **German Term Provenance & IGermanLexicon Core Seam (PR #130):** `IGermanLexicon` interface seam in `KnownFirst.Core` with German term provenance (`DerivedTermEvidence`) support in the text-analysis pipeline. Provides a deterministic, offline-capable lexical evidence foundation for German vocabulary analysis. Production lexicon implementations and visible Settings UI remain deferred.
- **Conservative German Compound Decomposition Core Seam (PR #131):** `ConservativeGermanCompoundDecomposer` available through the `IGermanLexicon` opt-in seam. Decomposes a German compound into exactly one unambiguous, fully lexicon-backed two-component split; whole source compound remains a Direct candidate; derived components carry `CandidateProvenanceKind.DerivedFromCompound`. Ambiguous, unsupported, and Fugen-element cases fail closed without guessing. Production `TextReviewService` wiring and visible Settings UI remain deferred.

## Active Uncommitted Feature-Branch Candidate (Not Yet on `master`)

The following describes an independently reviewed and approved, but still **uncommitted**, working-tree candidate on `feature/german-enhanced-term-recognition-e2e-v1`. It is not part of the `master` facts recorded elsewhere in this document and must not be read as such. Full operational/lifecycle status and the deferred Beta-13 Pre-AAB sequencing: [docs/CURRENT_WORK.md](CURRENT_WORK.md). Planned follow-on packages: [docs/ROADMAP.md](ROADMAP.md).

- **German Enhanced Term Recognition — Package 1 (production morphology lexicon):** a production, offline, deterministic `IGermanLexicon` implementation (`GeneratedGermanLexicon`) backed by a single authoritative runtime bundle (`KnownFirst.Core/Text/German/Assets/german-lexicon.v2.kfgl`, 11,169,687 bytes, SHA-256 `528040afbcd9a4eeb18d269c72ab449e4f9280a87085cf7b56f625371c53ed0a`), generated from the pinned `DuyguA/german-morph-dictionaries` commit `1780890c0fd25a989201c96000af323cd201fa5c` (`morf_dict.zip` / `DE_morph_dict.txt`, source SHA-256 `842e0b2f922e74afbc5961154c6e7935605ac8abbeb8af2fc83e4940db86af52`). Upstream data is treated conservatively as CC BY-SA 4.0 (the pinned commit's actual `LICENSE` file governs; the stale MIT wording in the upstream `README.md` does not). KnownFirst generator/reader source code remains Apache-2.0. Independent final acceptance review: 0 BLOCKER / 0 MAJOR / 0 MINOR (one cosmetic NIT). Latest focused verification: 183 passed / 0 failed / 0 skipped (focused evidence only, not `FULL_VALIDATION`).
- **German Enhanced Term Recognition — Packages 2–3 (application integration):** production `TextReviewService`/DI registration, persisted-setting-gated Settings UI wiring, and Schema-11 `DerivedTermEvidence` persistence **now exist** in this candidate — the production lexicon is no longer unwired. This wiring is present in the current uncommitted working tree; it is not yet packaged into a shipped Windows/Android build.
- **German Enhanced Term Recognition — Package 4 (multi-component/Fugen/de-inflection decomposition):** extends `ConservativeGermanCompoundDecomposer` from exactly two components to a bounded ordered decomposition of 2–4 lexicon-confirmed components; `GermanCompoundDecomposition` now exposes an ordered `Components` collection. Minimum component span length 2; final/head component must resolve as `GermanLexemeCategory.Noun`; literal lexicon resolution always wins for a span, falling back only when it fails to a single unified, closed suffix-stripping mechanism whose shipped suffix set is exactly `s`, `es`, `e` (linking-element and de-inflection candidates `n`, `en`, `er` were evaluated but intentionally not shipped for lack of sufficient conservative lexicon-backed justification). Zero or multiple valid complete partitions, and multiple genuinely distinct fallback interpretations for one span, fail closed; a word that itself resolves as a single lexicon entry is never accepted as a trivial one-component decomposition. `TextAnalyzer` consumes every ordered component; the whole source compound remains a Direct candidate; derived components remain `DerivedFromCompound` with no fabricated `TokenOccurrence`; `DerivedTermEvidence` is unchanged and always points to the complete source-compound occurrence; Direct identity wins over Derived identity; feature-OFF and non-German behavior are unchanged. Full binding contract: [docs/WORD_ANALYSIS.md](WORD_ANALYSIS.md). Package 4 is technically complete and independently reviewed after a focused test-correction cycle (original focused suite 120 passed / 0 failed / 0 skipped; correction verification `ProductionGermanLexiconTests` 31 passed / `GermanCompoundDecomposerTests` 23 passed, combined 54 passed / 0 failed / 0 skipped). Package 4 introduced no schema, backup, preparation, learning, Settings, localization, or UI changes. Package 5 (remaining derivation-source persistence/regression-coverage/review-UI items) has not started.

## Evidence Boundaries & Release Limitations

- **Release Packaging & Distribution Boundary:** Source merge is not a packaging or distribution event. A signed Android Release APK (`KnownFirst-1.0.0-beta.13-android-release.apk`, SHA-256 `53bbcb18b62927dae0af0a63d0e6a3cda6a8420c1c9517d354c638504b9ac6b6`) was produced and verified on physical hardware for manual Android validation. Final Release AAB creation is explicitly authorized for Google Play Internal Testing once the mandatory Pre-AAB Gate is certified on merged `master` (AAB is not yet created). Subsequent Google Play Internal Testing upload/distribution is separately authorized (not yet performed). A real self-contained Windows Portable Release ZIP (`KnownFirst-1.0.0-beta.13-build13-win-x64-9e455d0.zip`) and matching SHA-256 sidecar were successfully produced and structurally verified from synchronized `master` commit `9e455d0e03494cac8e713cd4d16c66946124f852` on 2026-08-16. No real MSIX package has yet been produced; the Portable package has not been launched or installed on a clean/secondary PC, and no external distribution has occurred.
- **Pre-AAB Release-Readiness Gate:** Mandatory pre-AAB release-readiness verification ([docs/BUILD_AND_RELEASE.md](BUILD_AND_RELEASE.md) §7) remains strictly required on the live candidate HEAD before any future release package creation.
- **Store Identity:** Partner Center Store identity inputs remain template placeholders (`devidentity`).
- **Support KnownFirst:** Unimplemented planned feature; completely absent from production rendering without placeholders.
- **Cloud & Accounts:** No cloud synchronization, accounts, analytics, advertising, or payments exist. All persistence and backups are local-first.
