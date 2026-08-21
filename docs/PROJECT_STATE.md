# KnownFirst Project State

**Status date:** 2026-08-20
**State source:** Synchronized `master` baseline. Authoritative live Git and PR state are discovered dynamically per [docs/NEW_CHAT_BOOTSTRAP.md](NEW_CHAT_BOOTSTRAP.md).

This document records stable, verified architectural facts and current capabilities. Plans belong in [ROADMAP.md](ROADMAP.md); active operational task state belongs in [CURRENT_WORK.md](CURRENT_WORK.md).

## Stable Release & Source Identity

| Field | Verified value |
| :--- | :--- |
| **Project** | KnownFirst |
| **Source Version (`master`)** | `1.0.0-beta.13` (build 13) — merged via PR #92 |
| **Active Database Schema** | SQLite `PRAGMA user_version` 11 |
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
- transactional local persistence (Schema 11), startup maintenance, and bounded structured diagnostics;
- production offline German Enhanced Term Recognition (default OFF, `EnhancedTermRecognitionEnabled` in Settings): conservative German compound decomposition against the production `GeneratedGermanLexicon`, wired into `TextReviewService` analysis, with Schema-11 `DerivedTermEvidence` persistence.

## Development, Tooling & Packaging Foundations

- **Repository Tooling & Path Portability (PR #111):** Organized script hierarchy (`scripts/packaging/`, `scripts/validation/`, `scripts/tools/`) with dynamic root resolution (`$PSScriptRoot` / `__file__`), eliminating fixed clone path dependencies.
- **Safe Artifact Cleanup (PR #110):** Canonical launcher `Clean` and `Clean -Deep` actions with root safety validation, log retention pruning, and protection of user databases and release packages.
- **Windows Distribution Packaging Infrastructure (PR #107, PR #113, PR #114):** Dedicated publishing scripts (`scripts/packaging/publish-windows-portable.ps1`, `scripts/packaging/publish-windows-msix.ps1`) supporting unpackaged win-x64 portable ZIP and MSIX packaging with SHA-256 sidecars, isolated build roots under `artifacts/build/` and `artifacts/obj/`, Store version mapping `1.0.13.0`, and contract test coverage. On 2026-08-16, the canonical `WindowsPortablePackage` action was executed from synchronized `master` commit `9e455d0e03494cac8e713cd4d16c66946124f852`, producing verified self-contained archive `artifacts\windows-portable\KnownFirst-1.0.0-beta.13-build13-win-x64-9e455d0.zip` (SHA-256 `cebd84824aa4e7909edb3a6e83c467573c3e245535ae585b6be832934a45a81e`) with confirmed runtime payload markers.
- **Windows Release Storage Isolation (PR #104):** Test-profile redirection under `artifacts/gui-tests/windows/profiles/` for safe local Gate-12 visual verification without touching real host data.
- **Pre-AAB Gate Test Infrastructure (PR #94, PR #96):** Configuration-sensitive `DefineConstants` test coverage and MSBuild property analysis for Release gating.
- **Android Rendered GUI Automation Foundation (PR #91 / P16-A):** MSBuild intermediate isolation and Release Notes screenshot capture foundation (P16-B/P16-C matrix automation pending).
- **Enhanced Term Recognition Setting Foundation (PR #129):** Persisted application-level `EnhancedTermRecognitionEnabled` setting, default OFF.
- **German Term Provenance & IGermanLexicon Core Seam (PR #130):** `IGermanLexicon` interface seam in `KnownFirst.Core` with German term provenance (`DerivedTermEvidence`) support in the text-analysis pipeline. Deterministic, offline-capable lexical evidence foundation for German vocabulary analysis.
- **Conservative German Compound Decomposition Core Seam (PR #131):** `ConservativeGermanCompoundDecomposer` available through the `IGermanLexicon` opt-in seam. Whole source compound remains a Direct candidate; derived components carry `CandidateProvenanceKind.DerivedFromCompound`. Ambiguous, unsupported, and Fugen-element cases fail closed without guessing. Superseded/extended by PR #134 below (2–4 component decomposition).
- **German Enhanced Term Recognition — Packages 1–4 (PR #134):** merged production German morphology lexicon, MAUI application/Settings integration, `TextReviewService` wiring, Schema-11 `DerivedTermEvidence` persistence, and bounded 2–4 component decomposition. Full detail: "German Enhanced Term Recognition — Packages 1–4 (Merged Production State)" below.
- **German Enhanced Term Recognition — Package 5A (PR #135):** merged derived-term post-review lifecycle integrity — Unknown derived-evidence retention through review completion, document/sentence-span dependency protection, Preparation source-compound context fallback, cleanup on MarkKnown/Exclude, and a portable V2 export filter. Full detail: "German Enhanced Term Recognition — Package 5A (Merged Production State)" below.

## German Enhanced Term Recognition — Packages 1–4 (Merged Production State)

Merged to `master` via PR #134 (merge commit `6c7a89ed6b4b0fc7701fdca8ec85a38b91bbeeb5`; validated PR head `3ca5512c91a6f97459e23ba7b6fdd304774695b2`). Exact-head `FULL_VALIDATION` before merge: 2216 passed / 0 failed / 0 skipped; Windows Debug PASS; Windows Release PASS; Android Debug PASS; Android Release PASS; exit code 0. This is binding current-`master` production state. Full operational/lifecycle status and the deferred Beta-13 Pre-AAB sequencing: [docs/CURRENT_WORK.md](CURRENT_WORK.md). Package 5A is also now merged production state (below); planned follow-on packages (5A-2, 5B): [docs/ROADMAP.md](ROADMAP.md).

- **Package 1 (production morphology lexicon):** a production, offline, deterministic `IGermanLexicon` implementation (`GeneratedGermanLexicon`) backed by a single authoritative runtime bundle (`KnownFirst.Core/Text/German/Assets/german-lexicon.v2.kfgl`, 11,169,687 bytes, SHA-256 `528040afbcd9a4eeb18d269c72ab449e4f9280a87085cf7b56f625371c53ed0a`), generated from the pinned `DuyguA/german-morph-dictionaries` commit `1780890c0fd25a989201c96000af323cd201fa5c` (`morf_dict.zip` / `DE_morph_dict.txt`, source SHA-256 `842e0b2f922e74afbc5961154c6e7935605ac8abbeb8af2fc83e4940db86af52`). Upstream data is treated conservatively as CC BY-SA 4.0 (the pinned commit's actual `LICENSE` file governs; the stale MIT wording in the upstream `README.md` does not). KnownFirst generator/reader source code remains Apache-2.0.
- **Packages 2–3 (application integration):** production `TextReviewService`/DI registration, persisted-setting-gated Settings UI wiring, and Schema-11 `DerivedTermEvidence` persistence are live production wiring — the production lexicon is not unwired. Not yet packaged into a shipped Windows/Android build.
- **Package 4 (multi-component/Fugen/de-inflection decomposition):** `ConservativeGermanCompoundDecomposer` performs a bounded ordered decomposition of 2–4 lexicon-confirmed components; `GermanCompoundDecomposition` exposes an ordered `Components` collection. Minimum component span length 2; final/head component must resolve as `GermanLexemeCategory.Noun`; literal lexicon resolution always wins for a span, falling back only when it fails to a single unified, closed suffix-stripping mechanism whose shipped suffix set is exactly `s`, `es`, `e` (linking-element and de-inflection candidates `n`, `en`, `er` were evaluated but intentionally not shipped for lack of sufficient conservative lexicon-backed justification). Zero or multiple valid complete partitions, and multiple genuinely distinct fallback interpretations for one span, fail closed; a word that itself resolves as a single lexicon entry is never accepted as a trivial one-component decomposition. `TextAnalyzer` consumes every ordered component; the whole source compound remains a Direct candidate; derived components remain `DerivedFromCompound` with no fabricated `TokenOccurrence`; `DerivedTermEvidence` always points to the complete source-compound occurrence; Direct identity wins over Derived identity; feature-OFF and non-German behavior are unchanged. Full binding contract: [docs/WORD_ANALYSIS.md](WORD_ANALYSIS.md).
- **Build-isolation correction:** `KnownFirst.csproj` excludes `scripts\tools\GermanLexiconGenerator\**` through `DefaultItemExcludes`, removing the nested generator project tree from the root application's default Compile items (root cause of an initial `FULL_VALIDATION` attempt failing at the Windows Debug build with CS0579 duplicate assembly-attribute errors); regression-protected by `DefaultCompileItems_ExcludeTheEntireNestedGermanLexiconGeneratorProjectTree` in `KnownFirst.Tests/WindowsPackageVersionMappingTests.cs`. `GermanLexiconGenerator.csproj` remains independently buildable.

## German Enhanced Term Recognition — Package 5A (Merged Production State)

Merged to `master` via PR #135 (merge commit `683f34473dd21417be9d8e1b60d04de539fb35a8`; validated PR head `5d4efa48b0368ef3a68d47963c7643c0ca57b57b`), on top of Packages 1–4 above. This is binding current-`master` production state. Full operational/lifecycle history: [docs/CURRENT_WORK.md](CURRENT_WORK.md). Planned follow-on packages: [docs/ROADMAP.md](ROADMAP.md).

Package 5A corrects the post-review-completion lifecycle of a derived compound component's `ReviewCandidateEntity`/`DerivedTermEvidenceEntity`, given that a derived candidate deliberately never receives a `WordOccurrenceEntity`: Unknown derived evidence retention through review completion, sentence/document dependency protection in generic maintenance cleanup, Preparation source-compound context fallback (display and Accept), cleanup on MarkKnown/Exclude, and a portable V2 export filter that excludes only the specific retained-evidence-owning candidate rows from exported review items. No database schema/version change and no archive DTO/version change — full contract: [docs/DATABASE_CONTRACT.md](DATABASE_CONTRACT.md) "Schema-11 Derived-Term Evidence Contract" and [docs/WORD_ANALYSIS.md](WORD_ANALYSIS.md) "Post-review derived-evidence context lifecycle."

- **Review history:** first independent `REVIEW_ONLY` found 1 BLOCKER / 1 MAJOR / 1 MINOR; all three were corrected, with correction-focused verification 481 passed / 0 failed / 0 skipped and clean `git diff --check`. Final independent `REVIEW_ONLY`: 0 BLOCKER / 0 MAJOR / 0 MINOR, approved for `DOCUMENT_ONLY`; targeted verification during that review reported 141/141 passed and 49/49 passed, `git diff --check` clean.
- **Exact-head `FULL_VALIDATION`:** 2227 passed / 0 failed / 0 skipped; Windows Debug PASS; Windows Release PASS; Android Debug PASS; Android Release PASS; exit code 0; log `artifacts/launcher-logs/ValidateAll-20260821-001937.log`. `POST_MERGE_SYNC_ONLY` completed successfully.
- **Evidence boundary:** this evidence proves only the automated scope the canonical launcher executed (2227 automated tests, Windows Debug/Release builds, Android Debug/Release builds). It does not prove GUI/manual UX correctness, device/emulator runtime behavior, APK/AAB installation behavior, signing correctness, Google Play readiness/publication, or cross-installation Package-5A-2 derived-evidence portability.
- **Next planned German work:** Package 5A-2 (portable/cross-installation transport semantics for retained derived-term provenance/context) is next; no feature branch exists for it and implementation has not started. Package 5B (minimal visible derived-source context in Review Words) is planned after Package 5A-2.

## Evidence Boundaries & Release Limitations

- **Release Packaging & Distribution Boundary:** Source merge is not a packaging or distribution event. A signed Android Release APK (`KnownFirst-1.0.0-beta.13-android-release.apk`, SHA-256 `53bbcb18b62927dae0af0a63d0e6a3cda6a8420c1c9517d354c638504b9ac6b6`) was produced and verified on physical hardware for manual Android validation. Final Release AAB creation is explicitly authorized for Google Play Internal Testing once the mandatory Pre-AAB Gate is certified on merged `master` (AAB is not yet created). Subsequent Google Play Internal Testing upload/distribution is separately authorized (not yet performed). A real self-contained Windows Portable Release ZIP (`KnownFirst-1.0.0-beta.13-build13-win-x64-9e455d0.zip`) and matching SHA-256 sidecar were successfully produced and structurally verified from synchronized `master` commit `9e455d0e03494cac8e713cd4d16c66946124f852` on 2026-08-16. No real MSIX package has yet been produced; the Portable package has not been launched or installed on a clean/secondary PC, and no external distribution has occurred.
- **Pre-AAB Release-Readiness Gate:** Mandatory pre-AAB release-readiness verification ([docs/BUILD_AND_RELEASE.md](BUILD_AND_RELEASE.md) §7) remains strictly required on the live candidate HEAD before any future release package creation.
- **Store Identity:** Partner Center Store identity inputs remain template placeholders (`devidentity`).
- **Support KnownFirst:** Unimplemented planned feature; completely absent from production rendering without placeholders.
- **Cloud & Accounts:** No cloud synchronization, accounts, analytics, advertising, or payments exist. All persistence and backups are local-first.
