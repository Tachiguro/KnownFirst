# KnownFirst Project State

**Status date:** 2026-09-02
**State source:** merged Schema-13 persistence, Archive V3, and production FSRS-6 runtime cutover (`KF-PERSIST-013-001`, `KF-BACKUP-006`, `KF-FSRS-003`). Live Git remains authoritative for branch and pull-request state, discovered dynamically per [docs/NEW_CHAT_BOOTSTRAP.md](NEW_CHAT_BOOTSTRAP.md).

This document records stable, verified architectural facts and current capabilities. Plans belong in [ROADMAP.md](ROADMAP.md); active operational task state belongs in [CURRENT_WORK.md](CURRENT_WORK.md).

## Stable Release & Source Identity

| Field | Verified value |
| :--- | :--- |
| **Project** | KnownFirst |
| **Source Version (`master`)** | `1.0.0-beta.13` (build 15) — prepared via release-identity package KF-RELEASE-002 |
| **Active Database Schema** | SQLite `PRAGMA user_version` 13 on current production `master`; fresh databases bootstrap directly to Schema 13; existing Schema 1–12 databases fail closed in the production startup path |
| **Package ID** | `com.tachiguro.knownfirst` |
| **Target Distribution** | Google Play Internal Testing |
| **Distributed Status** | `1.0.0-beta.12` distributed and user-tested (confirmed 2026-07-30; see [docs/releases/1.0.0-beta.12.md](releases/1.0.0-beta.12.md)). Signed replacement bundle `KnownFirst-1.0.0-beta.13-code14.aab` (`48,002,097` bytes, SHA-256 `7a84da599ae7435614d95ff316707669d69e21b311fe252f5419ac9cb8ecbbcd`, `StrictVerified`) was created and verified locally from certified `master` commit `8cd98d27ff81d8134b4e3b9d4b32b9b85abe3cb2`. Historical `KnownFirst-1.0.0-beta.13-code13.aab` was verified locally but rejected on Google Play Console upload due to duplicate version code 13. Active candidate build identity is Build 15, for which no AAB package has yet been created or distributed. |
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
- explicit online-lookup consent (governed post-onboarding by Settings with fail-closed transport authorization gating, authorization epochs, and revocation cancellation), read-only Wiktionary lookup with automatic Wikipedia definition fallback, local SQLite lexical cache, and disabled/blocked Prepare Words state when consent is absent;
- source attribution, alternative-meaning selection, manual correction, and context snapshots;
- recognition and spelling card directions with independent deterministic schedules;
- Learn screen card direction indicators and visual "Repeat" badges for `IsAgainRepeat` cards;
- resumable learning sessions and permanent-known cleanup;
- portable `.kfarchive` (v3) data export with Schema-13 FSRS state/history transport and native Save dialog on Windows and Android; V3 archives carry `requiredFeatures: ["learning-review-causal-order-v1"]` declaring normative causal interaction order; V1/V2 archives remain importable only where Schema-13 causal interaction order is provable;
- recovery import of `.kfarchive` into empty installations with native Open dialog;
- transactional populated-target import with validated pre-merge safety copies, preflight preview, collision-free action keys, and atomic commit-or-rollback (stale plans rejected; re-import converges to `NoChanges`);
- portable Active learning-workflow preservation and resume into empty Schema-10 targets (KF-BACKUP-005B) and populated-target Active-workflow convergence/conflict safety (KF-BACKUP-005C);
- FSRS-6 production scheduling (`IFsrs6SchedulingService` / `Fsrs6SchedulingService`) with card-state replay from append-only `FsrsReviewHistoryEntries` in deterministic `SequenceNumber` order; factual review history and FSRS card state are persisted atomically, exactly replay-consistent, and separate from compatibility `LearningReviews` and legacy scheduling columns;
- distinct Schema-13 AlreadyKnown word controls and StopLearning sense controls, preserving factual history while governing runtime eligibility;
- coherent `ReviewDiagnosticsSnapshot` capture from one database snapshot;
- reopenable release-note history (`/release-notes`) and Help & Support entry point;
- functional "Report a bug" email composer action launching with structured template prompts and clipboard copy fallback;
- one-time localized What's New notice shown once per version;
- transactional local persistence (Schema 13), global `PRAGMA foreign_keys = ON` enforcement, startup maintenance, and bounded structured diagnostics;
- production offline German Enhanced Term Recognition (missing-preference default ON on `master` via PR #144, `EnhancedTermRecognitionEnabled` in Settings): conservative German compound decomposition against the production `GeneratedGermanLexicon`, wired into `TextReviewService` analysis, with Schema-11 `DerivedTermEvidence` persistence;
- full Settings GUI exposing learning timezone (System or 50 curated IANA zones spanning inhabited UTC-11 through UTC+14, with dynamic DST-aware labels), deterministic 24-hour learning-day cutoff (`00..23` hour, `00..59` minute selectors), default-first card direction and learning mode choices, non-destructive Restore Defaults preserving online dictionary consent, and destructive Full Reset revoking consent (merged via PR #144);
- preference-backed onboarding state foundation (`Required`, `InProgress`, `Completed`), startup install-origin classification distinguishing fresh from grandfathered existing installations, grandfathered 10-word daily budget pinning, and reset contracts (merged via PR #153);
- dedicated 9-screen first-run onboarding host with restart resume, optional local Display Name, daily new-word budget range policy (`1..50`, default/recommended `5`, presets `1 / 5 / 10`, advisory warning `>15`), What's New completion coordination, and reset preservation contracts (merged via PR #155);
- unified Settings and first-run onboarding visual primitives and controls: shared design tokens (eliminating `--kf-color-*`), semantic button variants (`button-primary`, `button-secondary`, `button-danger`), shared choice grids and active states (`.choice-button.active`, `aria-pressed`), input formatting (`.text-input`, `.field-group`, native select styling), and shared feedback banners (`.setting-feedback`) (merged via PR #156);
- daily new-word budget parity across Settings and Onboarding: consistent visual order (`5 Recommended`, `1`, `10`, `Custom`), contiguous range `1..50`, default `5`, non-blocking advisory warning `>15`, and semantic commit boundary canonicalization (merged via PR #156);
- onboarding System Language and Appearance selection: System, English, German, Russian language options (with localized informational notice for unsupported device cultures), and Welcome-step System, Light, and Dark appearance selection backed by existing singleton services (merged via PR #156);
- accessible inline destructive confirmation parity: explicit inline confirmation for Online Dictionary consent revocation in Onboarding and Settings, post-render focus transfer to Cancel, non-destructive Cancel and Escape dismissing confirmation and restoring focus to trigger, and destructive Confirm acting as the sole revocation execution path (merged via PR #156);
- personalized Home greeting: `Home.razor` consumes the synchronous `IDisplayNameStore` singleton to render a localized greeting (`Welcome, {0}.` / `Willkommen, {0}.` / `Добро пожаловать, {0}.`) before the existing subtitle when a normalized Display Name is configured, while preserving the unchanged `KnownFirst` heading and subtitle-only fallback when absent (merged via PR #158);
- manual Preparation reliability and UX: user-entered Definition or Translation without online lookup result, authoritative candidate lookup context mapping, exact manual semantic reuse without duplicate identities/cards, neutral End Preparation workflow action, save/progression recovery separation, and shared multiline visual primitives (merged via PR #160);
- first-run onboarding and Settings parity: native UI language selector matching Settings with shared `LanguagePreferencePolicy.UiLanguageOptions` source, explicit Online Dictionary Enable vs Keep Disabled decision buttons before first-run progression with confirmed destructive revocation, dynamic Display Name Skip labeling, benefit-oriented German compound decomposition ETR copy, Practice helper text reuse, and Summary Settings notice (merged via PR #162);
- shell navigation drawer breakpoint reconciliation: transient navigation drawer state closed, backdrop dismissed, and content scroll lock cleared when resizing across the 800px desktop boundary (merged via PR #168);
- first-run onboarding vertical scroll reachability: bounded scroll surface on `.onboarding-host` with auto-centering on `.onboarding-main`, ensuring all onboarding steps and actions remain fully reachable at constrained viewport heights (merged via PR #169);
- scheduled review next-due summary authority: `LearningSessionSummary.NextDueAtUtc` is strictly restricted to scheduled work in Learning, Review, or Relearning states that have at least one active Required answer assignment, excluding genuinely-new card admission seed timestamps and nonqueueable zero-Required cards without altering persisted timestamps or card states;
- clock-driven Learn summary due monitoring: mounted completed learning session summaries dynamically detect when a scheduled review becomes due via `IClock.UtcNow` authority, reactively revealing an explicit primary Learn action without automatic session replacement, while preserving completed session statistics and local-time presentation formatting;
- unified Review Words workflow action bar: Known, Unknown, and Undo organized into a compact content-sized left action group and Discard import positioned as a separately aligned destructive end action on wide layouts, with flexible separation between primary decisions and full-import discard; common standard button geometry and minimum height across all four actions; safe responsive stacking across narrow and extra-narrow viewports; standard secondary button styling and disabled-state binding for Undo; preserved destructive styling and irreversible confirmation for Discard import; and concise localized Undo button labels across English, German, and Russian;
- authoritative post-onboarding online lookup consent enforcement and fail-closed privacy architecture (merged via PR #181 / KF-ONLINE-LOOKUP-CONSENT-001): `IOnlineLookupAuthorizationGate` / `OnlineLookupAuthorizationHandler` transport gate blocking unauthorized outbound lexical HTTP; authorization-epoch-bound orchestration and prefetch safety with immediate cancellation on consent revocation; contextual consent disclosure removed from Prepare Words so Settings is the sole post-onboarding authority; dedicated blocked-candidate state with Settings navigation and manual fallback without data loss; Automatic Online method disabled and lookup retry disabled while consent is absent; *(Candidate branch `fix/prepare-online-settings-deeplink-v1` [KF-PREP-001] deep-links Prepare Words disabled Online Dictionary "Open Settings" actions to `#online-lookup-title` and reveals/focuses the section heading once; verified by automated source/contract tests, not yet merged to master);*
- transactional first-run onboarding settings and startup recovery (merged via PR #182 / KF-TRANSACTIONAL-ONBOARDING-001): versioned persisted `OnboardingDraft` accumulating setup choices across steps with persisted restart resume; Finish Setup as the sole atomic commit boundary; immediate non-persisting language and theme preview during setup; deterministic `OnboardingCompletionJournal` with SHA-256 fingerprint and pre-write durability barrier; idempotent startup recovery executing before database initialization; fail-closed handling of unsupported future journal versions; crash-safe legacy migration with consent reconfirmation; and strict preservation of Package A's fail-closed online-lookup transport authorization gate (draft consent true does not authorize transport until verified completion roll-forward).

## Schema-13 / FSRS-6 Merged Production State (KF-FSRS-003)

Current `master` implements the clean Schema-13 production cutover, FSRS-6 authority, factual FSRS state/history persistence, Archive V3 integrity and causal interaction ordering, and Repairs 001–006. This records source/runtime truth only; it does not establish GUI, device, package, release, or distribution evidence.

## Transactional Onboarding Settings & Recovery — Merged Production State (KF-TRANSACTIONAL-ONBOARDING-001)

**Lifecycle status:** Merged production `master` state via PR #182 (`feat(onboarding): make setup settings transactional` / KF-TRANSACTIONAL-ONBOARDING-001; merge commit `172587f4dc52bf3f5573bcfda53297de3216d3b6`; validated PR head `5d8cada0406c4243a9a4d2cea51c6c1491ab2c6d`). Exact candidate `FULL_VALIDATION` passed with 2753 / 2753 tests and all Windows/Android Debug/Release plus AOT/trimming/linker gates (exit code 0; log `artifacts/launcher-logs/ValidateAll-20260828-143720.log`). `POST_MERGE_SYNC_ONLY` completed. Package B is now a merged production source capability on `master`. `DatabaseSchema.CurrentVersion` was 12 and portable archive format was V2 at this package's merge, before the Schema-13 / Archive V3 cutover.

This package replaces the step-by-step direct-persistence onboarding model with a fully transactional draft-and-commit model. All onboarding choices are staged in a single versioned persisted draft and applied atomically only when Finish Setup is confirmed.

**1. Draft ownership**

- One versioned persisted `OnboardingDraft` accumulates all onboarding-configurable values throughout the flow, including a nullable `OnlineLookupConsent`.
- The draft supports persisted restart resume: an interrupted setup resumes at the last completed step with its saved choices intact.
- `OnboardingHost` is the full aggregate owner of the draft; child setting steps are parameter/callback components with no independent persistence authority.
- No individual step commits its values to Settings while onboarding is in progress.

**2. Language and theme preview**

- Language and theme can be previewed immediately and non-persistingly during setup: the user sees the effect without committing it to Settings.
- Committed language and theme remain unchanged until Finish Setup applies the draft.

**3. Completion**

- Completion creates and verifies a single `OnboardingCompletionJournal` with a deterministic target fingerprint (SHA-256 of the draft's canonical representation).
- A durability barrier is enforced before any committed Settings writes.
- Roll-forward is idempotent: if interrupted, the next startup replays the journal to completion without duplicating writes.
- Completed state becomes authoritative before any cleanup (clearing the draft, clearing progress).
- Journal consent must be non-null; a null consent blocks completion.

**4. Startup recovery**

- Startup recovery executes before database initialization, not after.
- Unsupported future Package B data (unrecognized journal version) fails closed without guessing.
- Corrupt or incomplete journal state is recovered deterministically.
- No compensating fake multi-key transaction is used; replay is deterministic from the journal.

**5. Legacy migration**

- Existing incomplete onboarding (InProgress state without a draft) is normalized via a crash-safe Capturing/Normalizing marker protocol.
- An existing true consent becomes draft true, then the live consent is revoked during normalization so the user re-confirms during completion.
- An absent or false legacy consent becomes draft null (undecided).
- A user with undecided consent cannot retain setup progress past the Online Lookup step.

**6. Privacy**

- An onboarding draft value of true for Online Dictionary consent does **not** authorize external lookup. Package A's (`KF-ONLINE-LOOKUP-CONSENT-001`, merged on `master`) transport authorization gate remains closed until verified completion roll-forward grants true to the authoritative preference.
- After onboarding is completed, Settings is the sole authority for granting or revoking Online Dictionary consent. Post-onboarding destructive consent revocation behavior in Settings is unchanged by this package.

**7. Persistence boundary**

- All draft and journal state is preference-backed only; no database schema change was introduced.
- At this package's merge, `DatabaseSchema.CurrentVersion` was 12 and portable archive format was V2 (historical pre-cutover boundary).

**8. Evidence boundary**

- Exact-candidate `FULL_VALIDATION` proves automated test execution (2753 / 2753 passed) and Windows/Android Debug/Release build compilation under AOT and linker warning-as-error gates.
- These automated gates do **not** establish: rendered WebView visual appearance, actual Windows focus behavior, Android touch behavior, physical device testing, APK/AAB packaging, signing, upload, or distribution.

## Onboarding & Settings Tester-Feedback Parity (Merged Production State)

Merged to `master` via PR #162 (`fix: align onboarding with settings feedback`; merge commit `e29a292832612a0f5041636126628437a553c2a3`; validated PR head `d2171872cdfdf366642afa685924a23507d7dacd`). Full operational/lifecycle status: [docs/CURRENT_WORK.md](CURRENT_WORK.md).

- **Scope & Parity Refinements:**
  - **Language Selection Native Parity:** Onboarding Step 1 (Welcome) replaces the four-button language choice grid with a native `<select id="onboarding-ui-language-select">` matching Settings, enclosed in `.field-group` with explicit `<label for=...>`, preserving immediate switching and system language detection.
  - **Shared Ordered Language Catalog:** Single shared source in `LanguagePreferencePolicy.UiLanguageOptions` (System, English, German, Russian) consumed by both `Settings.razor` and `OnboardingHost.razor`.
  - **Online Dictionary Explicit Decision:** Onboarding copy simplified into readable highlights with visible provider names (Wiktionary primary, Wikipedia definition fallback, local storage privacy); requires an explicit user choice (`[ Enable Online Dictionary ]` vs `[ Keep Online Dictionary Disabled ]`) before Continue becomes enabled (persisted default Off is not an explicit choice); selecting Keep Disabled when consent was granted invokes the destructive confirmation dialog with focus/Escape lifecycle.
  - **Display Name Dynamic Skip Semantics:** Action button dynamically displays `Onboarding_DisplayNameSkip` ("Skip") when empty/whitespace and `Common_Continue` when filled, preserving optionality and normalization to `null`.
  - **Enhanced Term Recognition Copy:** Benefit-oriented description explaining German compound word decomposition into foundational vocabulary running offline on device, without general multi-language or AI claims.
  - **Practice Step Helper Text:** Renders explanatory guidance under Card Direction and Learning Mode titles with `aria-describedby` (using `Settings_CardDirectionHelp` and `Settings_LearningModeHelp`). `Settings_CardDirectionHelp` was absent from resource dictionaries on the PR #162 baseline (causing a raw-key fallback) and was subsequently restored across English, German, and Russian with repository-wide literal Razor localization-key guard coverage via package KF-LOCALIZATION-PLACEHOLDER-AUDIT-001.
  - **Summary Settings Notice:** Added `Onboarding_SummarySettingsNotice` informing users that all choices can be changed later in Settings.
- **Verification Evidence:**
  - Initial genuine RED: 46 passed / 7 failed (failed on missing language select, shared options, dynamic skip, explicit consent choices, practice helpers, summary notice).
  - Initial package-focused suite (GREEN): 204 passed / 0 failed.
  - Targeted continuity/regression: 38 passed / 0 failed (`DisplayNameTests`, `LanguageSelectionServiceTests`).
  - `git diff --check`: passed cleanly (0 errors).
  - Consolidated code review: 0 BLOCKER / 0 MAJOR / 0 MINOR / 0 NIT; disposition `REVIEW_APPROVED_FOR_DOCUMENT_ONLY`.
  - Exact-candidate `FULL_VALIDATION` (validated PR head `d2171872cdfdf366642afa685924a23507d7dacd`): 2570 passed / 0 failed / 0 skipped; Windows Debug PASS; Windows Release PASS; Android Debug PASS; Android Release PASS; strict warning/linking gate PASS; exit code 0; log `artifacts/launcher-logs/ValidateAll-20260825-124542.log`.
- **Evidence Boundary:** Automated source/markup, state transition, and localization contract tests verify component binding, error handling, and DOM structure. Rendered WebView/GUI appearance, actual Windows focus behavior, Android touch behavior, and native select dialogs were not manually proven by this package and are not claimed.
- **Persistence & Architecture Invariants:** At this package's merge, `DatabaseSchema.CurrentVersion` was 12 and portable archive format was V2 (historical pre-cutover boundary); Daily Pace presets, range (1..50), default (5), and warning (>15) unchanged; Learning Day minute precision (00..59) unchanged; zero network requests in onboarding or tests.

## Development, Tooling & Packaging Foundations

- **Repository Tooling & Path Portability (PR #111):** Organized script hierarchy (`scripts/packaging/`, `scripts/validation/`, `scripts/tools/`) with dynamic root resolution (`$PSScriptRoot` / `__file__`), eliminating fixed clone path dependencies.
- **Safe Artifact Cleanup (PR #110):** Canonical launcher `Clean` and `Clean -Deep` actions with root safety validation, log retention pruning, and protection of user databases and release packages.
- **Windows Distribution Packaging Infrastructure (PR #107, PR #113, PR #114):** Dedicated publishing scripts (`scripts/packaging/publish-windows-portable.ps1`, `scripts/packaging/publish-windows-msix.ps1`) supporting unpackaged win-x64 portable ZIP and MSIX packaging with SHA-256 sidecars, isolated build roots under `artifacts/build/` and `artifacts/obj/`, Store version mapping `1.0.13.0`, and contract test coverage. On 2026-08-16, the canonical `WindowsPortablePackage` action was executed from synchronized `master` commit `9e455d0e03494cac8e713cd4d16c66946124f852`, producing verified self-contained archive `artifacts\windows-portable\KnownFirst-1.0.0-beta.13-build13-win-x64-9e455d0.zip` (SHA-256 `cebd84824aa4e7909edb3a6e83c467573c3e245535ae585b6be832934a45a81e`) with confirmed runtime payload markers.
- **Windows Release Storage Isolation (PR #104):** Test-profile redirection under `artifacts/gui-tests/windows/profiles/` for safe local Gate-12 visual verification without touching real host data.
- **Pre-AAB Gate Test Infrastructure (PR #94, PR #96):** Configuration-sensitive `DefineConstants` test coverage and MSBuild property analysis for Release gating.
- **Android Rendered GUI Automation Foundation (PR #91 / P16-A) & S36 Source Mapping (P16-B):** Opt-in GUI test variant (`com.tachiguro.knownfirst.guitest`) with MSBuild intermediate isolation (`artifacts/obj/android-gui-test/`), fail-closed build-identity verification, Appium/UiAutomator2 harness, and S36 scenario mapping (`P16B-SettingsReleaseNotesHistory`, `matrixMapping: "S36"`) covering profile safety, What's New initial seen state, Settings -> Release Notes navigation, Beta 13/12/11/10 newest-first catalog order, bullet presence, localized header title, second activation, controlled `terminateApp`/`activateApp` restart, What's New non-reappearance, and obsolete Support KnownFirst UI absence. (Source mapping and contract tests only; no rendered/runtime Android evidence exists; S36 remains unpassed in the matrix; Windows P16-C launcher integration pending).
- **Enhanced Term Recognition Setting Foundation (PR #129):** Persisted application-level `EnhancedTermRecognitionEnabled` setting, default OFF.

- **German Term Provenance & IGermanLexicon Core Seam (PR #130):** `IGermanLexicon` interface seam in `KnownFirst.Core` with German term provenance (`DerivedTermEvidence`) support in the text-analysis pipeline. Deterministic, offline-capable lexical evidence foundation for German vocabulary analysis.
- **Conservative German Compound Decomposition Core Seam (PR #131):** `ConservativeGermanCompoundDecomposer` available through the `IGermanLexicon` opt-in seam. Whole source compound remains a Direct candidate; derived components carry `CandidateProvenanceKind.DerivedFromCompound`. Ambiguous, unsupported, and Fugen-element cases fail closed without guessing. Superseded/extended by PR #134 below (2–4 component decomposition).
- **German Enhanced Term Recognition — Packages 1–4 (PR #134):** merged production German morphology lexicon, MAUI application/Settings integration, `TextReviewService` wiring, Schema-11 `DerivedTermEvidence` persistence, and bounded 2–4 component decomposition. Full detail: "German Enhanced Term Recognition — Packages 1–4 (Merged Production State)" below.
- **German Enhanced Term Recognition — Package 5A (PR #135):** merged derived-term post-review lifecycle integrity — Unknown derived-evidence retention through review completion, document/sentence-span dependency protection, Preparation source-compound context fallback, cleanup on MarkKnown/Exclude, and a portable V2 export filter. Full detail: "German Enhanced Term Recognition — Package 5A (Merged Production State)" below.
- **German Enhanced Term Recognition — Package 5A-2 (PR #137):** merged cross-installation transport of retained derived-term evidence through portable export, full backup, merge safety copy, empty-target restore, and populated-target merge. Full detail: "German Enhanced Term Recognition — Package 5A-2 (Merged Production State)" below.
- **German Enhanced Term Recognition — Package 5B (PR #140):** merged minimal, always-visible Review Words derivation-source indication for derived compound candidates, plus native Exclude cleanup and Preparation→Learning continuity regression-coverage closure. Full detail: "German Enhanced Term Recognition — Package 5B (Merged Production State)" below.

## German Enhanced Term Recognition — Packages 1–4 (Merged Production State)

Merged to `master` via PR #134 (merge commit `6c7a89ed6b4b0fc7701fdca8ec85a38b91bbeeb5`; validated PR head `3ca5512c91a6f97459e23ba7b6fdd304774695b2`). Exact-head `FULL_VALIDATION` before merge: 2216 passed / 0 failed / 0 skipped; Windows Debug PASS; Windows Release PASS; Android Debug PASS; Android Release PASS; exit code 0. This is binding current-`master` production state. Full operational/lifecycle status and the deferred Beta-13 Pre-AAB sequencing: [docs/CURRENT_WORK.md](CURRENT_WORK.md). Package 5A, Package 5A-2, and Package 5B are also merged production state (below); future work is tracked in [docs/ROADMAP.md](ROADMAP.md).

- **Package 1 (production morphology lexicon):** a production, offline, deterministic `IGermanLexicon` implementation (`GeneratedGermanLexicon`) backed by a single authoritative runtime bundle (`KnownFirst.Core/Text/German/Assets/german-lexicon.v2.kfgl`, 11,169,687 bytes, SHA-256 `528040afbcd9a4eeb18d269c72ab449e4f9280a87085cf7b56f625371c53ed0a`), generated from the pinned `DuyguA/german-morph-dictionaries` commit `1780890c0fd25a989201c96000af323cd201fa5c` (`morf_dict.zip` / `DE_morph_dict.txt`, source SHA-256 `842e0b2f922e74afbc5961154c6e7935605ac8abbeb8af2fc83e4940db86af52`). Upstream data is treated conservatively as CC BY-SA 4.0 (the pinned commit's actual `LICENSE` file governs; the stale MIT wording in the upstream `README.md` does not). KnownFirst generator/reader source code remains Apache-2.0.
- **Packages 2–3 (application integration):** production `TextReviewService`/DI registration, persisted-setting-gated Settings UI wiring, and Schema-11 `DerivedTermEvidence` persistence are live production wiring — the production lexicon is not unwired. Not yet packaged into a shipped Windows/Android build.
- **Package 4 (multi-component/Fugen/de-inflection decomposition):** `ConservativeGermanCompoundDecomposer` performs a bounded ordered decomposition of 2–4 lexicon-confirmed components; `GermanCompoundDecomposition` exposes an ordered `Components` collection. Minimum component span length 2; final/head component must resolve as `GermanLexemeCategory.Noun`; literal lexicon resolution always wins for a span, falling back only when it fails to a single unified, closed suffix-stripping mechanism whose shipped suffix set is exactly `s`, `es`, `e` (linking-element and de-inflection candidates `n`, `en`, `er` were evaluated but intentionally not shipped for lack of sufficient conservative lexicon-backed justification). Zero or multiple valid complete partitions, and multiple genuinely distinct fallback interpretations for one span, fail closed; a word that itself resolves as a single lexicon entry is never accepted as a trivial one-component decomposition. `TextAnalyzer` consumes every ordered component; the whole source compound remains a Direct candidate; derived components remain `DerivedFromCompound` with no fabricated `TokenOccurrence`; `DerivedTermEvidence` always points to the complete source-compound occurrence; Direct identity wins over Derived identity; feature-OFF and non-German behavior are unchanged. Full binding contract: [docs/WORD_ANALYSIS.md](WORD_ANALYSIS.md).
- **Build-isolation correction:** `KnownFirst.csproj` excludes `scripts\tools\GermanLexiconGenerator\**` through `DefaultItemExcludes`, removing the nested generator project tree from the root application's default Compile items (root cause of an initial `FULL_VALIDATION` attempt failing at the Windows Debug build with CS0579 duplicate assembly-attribute errors); regression-protected by `DefaultCompileItems_ExcludeTheEntireNestedGermanLexiconGeneratorProjectTree` in `KnownFirst.Tests/WindowsPackageVersionMappingTests.cs`. `GermanLexiconGenerator.csproj` remains independently buildable.

## German Enhanced Term Recognition — Package 5A (Merged Production State)

Merged to `master` via PR #135 (merge commit `683f34473dd21417be9d8e1b60d04de539fb35a8`; validated PR head `5d4efa48b0368ef3a68d47963c7643c0ca57b57b`), on top of Packages 1–4 above. This is binding current-`master` production state. Full operational/lifecycle history: [docs/CURRENT_WORK.md](CURRENT_WORK.md). Subsequent merged and planned work is tracked in [docs/ROADMAP.md](ROADMAP.md).

Package 5A corrects the post-review-completion lifecycle of a derived compound component's `ReviewCandidateEntity`/`DerivedTermEvidenceEntity`, given that a derived candidate deliberately never receives a `WordOccurrenceEntity`: Unknown derived evidence retention through review completion, sentence/document dependency protection in generic maintenance cleanup, Preparation source-compound context fallback (display and Accept), cleanup on MarkKnown/Exclude, and a portable V2 export filter that excludes only the specific retained-evidence-owning candidate rows from exported review items. No database schema/version change and no archive DTO/version change — full contract: [docs/DATABASE_CONTRACT.md](DATABASE_CONTRACT.md) "Schema-11 Derived-Term Evidence Contract" and [docs/WORD_ANALYSIS.md](WORD_ANALYSIS.md) "Post-review derived-evidence context lifecycle."

- **Review history:** first independent `REVIEW_ONLY` found 1 BLOCKER / 1 MAJOR / 1 MINOR; all three were corrected, with correction-focused verification 481 passed / 0 failed / 0 skipped and clean `git diff --check`. Final independent `REVIEW_ONLY`: 0 BLOCKER / 0 MAJOR / 0 MINOR, approved for `DOCUMENT_ONLY`; targeted verification during that review reported 141/141 passed and 49/49 passed, `git diff --check` clean.
- **Exact-head `FULL_VALIDATION`:** 2227 passed / 0 failed / 0 skipped; Windows Debug PASS; Windows Release PASS; Android Debug PASS; Android Release PASS; exit code 0; log `artifacts/launcher-logs/ValidateAll-20260821-001937.log`. `POST_MERGE_SYNC_ONLY` completed successfully.
- **Evidence boundary:** this evidence proves only the automated scope the canonical launcher executed (2227 automated tests, Windows Debug/Release builds, Android Debug/Release builds). It does not prove GUI/manual UX correctness, device/emulator runtime behavior, APK/AAB installation behavior, signing correctness, Google Play readiness/publication, or evidence recorded separately below for Package 5A-2.
- **Next planned/current German work:** Package 5A-2 (portable/cross-installation transport of retained derived-term evidence) and Package 5B (minimal visible derived-source indication in Review Words) are both merged production `master` state — see below and "German Enhanced Term Recognition — Package 5B (Merged Production State)" below. No later German implementation package has started.

## German Enhanced Term Recognition — Package 5A-2 (Merged Production State)

Package 5A-2 is merged production `master` state via PR #137 (`feat: transport German derived evidence portably`; merge commit `5d1d3c05bae6ab9f1c56d8c5f9a227121f432f9a`; validated PR head `2ff447e9f874d49e72fee0a549820adc1bdc3b39`), on top of Package 5A above. This is binding current-`master` production state. Full operational/lifecycle history: [docs/CURRENT_WORK.md](CURRENT_WORK.md). Exact archive/schema/merge contract: [docs/DATABASE_CONTRACT.md](DATABASE_CONTRACT.md) "Schema-11 Derived-Term Evidence Contract."

Package 5A-2 implements the cross-installation transport that Package 5A intentionally left unserialized: the retained `DerivedTermEvidenceEntity` row(s) owned by a candidate that survives review completion while its word stays Unknown now travel through the ordinary portable/backup/merge pipeline, superseding Package 5A's temporary portable-export exclusion of that candidate. No database schema/version change (then-current `DatabaseSchema.CurrentVersion` was 11) and no archive-format change (then-current archive V2, no new required/optional feature).

- **Portable representation:** the V2 payload gains a top-level `DerivedTermEvidence` collection (`BackupDerivedTermEvidenceV2`: owning archive review-item reference, `SourceIdentity`, `SourceSurfaceForm`, `SourceStartPosition`, `SourceLength`, `SourceSentenceOrder`, `ComponentForm`) and a corresponding `BackupRecordCountsV2.DerivedTermEvidence` count. The retained candidate now exports through the ordinary completed-review-item path with its existing history/state fields unchanged — no synthetic `ReviewCandidate`/history vessel is created.
- **Coverage:** Schema-11 evidence is now captured consistently in ordinary portable export, full/internal backup, pre-merge safety copy, and the target-state capture used by populated-target preflight/writer re-evaluation — including the merge-safety-copy capture path, which previously never applied this enrichment at all.
- **Restore/merge:** empty-target restore resolves the transported evidence to the newly allocated local `ReviewCandidateEntity` (multiple evidence rows can reference one candidate without multiplying it; no synthetic `WordOccurrenceEntity` is created). Populated-target merge classifies `DerivedTermEvidence` as its own merge entity kind, using an installation-independent semantic identity built from the existing `ReviewCandidateIdentity` of the owning candidate, the source compound's own vocabulary identity (owning-document language plus `SourceIdentity`), `SourceStartPosition`, `SourceLength`, `SourceSentenceOrder`, and `ComponentForm` — no SQLite id and no archive-local id participate. Merge is additive only: exact semantic duplicates are skipped, target-only state is untouched, there is no overwrite/delete, and the transported evidence attaches to the one resolved final `ReviewCandidate` whether that candidate was newly inserted or already matched by identity.
- **Lifecycle parity:** imported/merged evidence participates in the same Package-5A lifecycle as natively created evidence — Preparation recovers real source-compound context from it, MarkKnown and Exclude both clean up the evidence and its owning retained candidate, and generic document/sentence-span cleanup continues to protect the dependency while retained evidence exists.
- **Validation:** portable graph validation mirrors the binding Schema-11 physical invariants (nonblank identity/surface/component fields, valid candidate/session/document ownership, in-bounds UTF-16 source range, exact source substring, exactly one matching sentence order, range contained inside that sentence, source identity resolving to a vocabulary row in the document language, duplicate semantic evidence rejected) and runs before any database mutation.
- **Test evidence:** the `IMPLEMENT` phase's focused scopes passed. An independent `REVIEW_ONLY` found no production-code defect and two MAJOR test-coverage gaps (Exclude cleanup and generic-cleanup protection for imported/merged evidence, respectively); both were closed by two new characterization/hardening tests in `KnownFirst.Tests/GermanDerivedTermEvidenceMergeTests.cs` (`PopulatedTargetMerge_ExcludeCleansUpMergedEvidence`, `PopulatedTargetMerge_GenericDocumentCleanupDoesNotOrphanMergedEvidence`), each passing immediately (1 passed / 0 failed). A combined focused scope of those two tests plus the existing imported-evidence MarkKnown test and the existing native-evidence cleanup-protection test passed 4 passed / 0 failed. The final independent `REVIEW_ONLY` reported 0 BLOCKER / 0 MAJOR findings (three MINOR findings deferred as non-blocking) and `Ready for DOCUMENT_ONLY: YES`.
- **Exact-head `FULL_VALIDATION`:** 2248 passed / 0 failed; Windows Debug PASS; Windows Release PASS; Android Debug PASS; Android Release PASS; exit code 0; log `artifacts/launcher-logs/ValidateAll-20260821-233217.log`. `POST_MERGE_SYNC_ONLY` completed successfully.
- **Evidence boundary (explicit):** this evidence proves only the automated scope the canonical launcher executed (2248 automated tests, Windows Debug/Release builds, Android Debug/Release builds). It does not prove GUI/manual UX correctness, device/emulator runtime behavior, APK/AAB installation behavior, signing correctness, or Google Play readiness/publication.
- **Remaining lifecycle:** none. `DOCUMENT_ONLY`, `COMMIT_ONLY`, exact-candidate-HEAD `FULL_VALIDATION`, `PUSH_ONLY`, `PR_ONLY`, manual user merge, and `POST_MERGE_SYNC_ONLY` are all complete for Package 5A-2.

## German Enhanced Term Recognition — Package 5B (Merged Production State)

Package 5B is merged production `master` state via PR #140 (`feat: show German derivation source in review`; merge commit `bd67393f81cece98c3d8c58c5ea26ef3e8920079`; validated PR head `d1de7556bfd346f3aa1fdd19741c7d1a6d647ba6`), on top of Package 5A-2 above. This is binding current-`master` production state. Full operational/lifecycle history: [docs/CURRENT_WORK.md](CURRENT_WORK.md).

Package 5B adds a minimal, always-visible Review Words derivation-source indication for genuine `CandidateProvenanceKind.DerivedFromCompound` candidates, and closes the remaining approved German derived-term regression-coverage gaps (native Exclude cleanup and Preparation→Learning continuity) via new characterization tests. No database schema/version change and no archive DTO/version change: the then-current `DatabaseSchema.CurrentVersion` was 11 and archive format was V2.

- **Data source:** Review Words now consumes the already-existing `ReviewCandidateDetails.Provenance`/`DerivationEvidence` projection, which `TextReviewService.GetCurrentCandidateAsync()` already populated from `DerivedTermEvidenceEntity` before this package. No new service method, query, or database boundary was introduced; the UI performs no database access of its own.
- **Displayed source:** `DerivedTermEvidence.SourceSurfaceForm` is the only field rendered — deduplicated, blank-filtered, and deterministically joined when more than one distinct source compound exists. `SourceIdentity`, database/archive ids, positions, hashes, and `ComponentForm` are never rendered.
- **Placement and scope:** the notice is derived-only, renders outside and before the collapsed metadata `<details>` panel, and is normal (non-Debug-gated) production UI. Direct candidates are visually unaffected. Active Review Words `Contexts` behavior is unchanged — no sentence-context reconstruction was added to Review Words, and the Package-5A Preparation retained-evidence context fallback remains the sole post-review-completion context-recovery mechanism.
- **Regression-coverage closure:** `CompletingPreparationWithoutLearning_ExcludeRemovesRetainedDerivationEvidence` characterizes the native (non-merge) Exclude cleanup path, mirroring the already-existing native MarkKnown characterization test; `DerivedUnknownAccept_EntersNormalLearningPathWithoutDuplicateIdentity` characterizes that an accepted derived word enters the normal Preparation→Learning pipeline with exactly one learning-card identity and exactly one resulting `LearningReview`, with no duplicate/synthetic identity created because of provenance. Both passed immediately against unmodified production code — no production correction was required.
- **Test evidence:** Stage-A genuine TDD on `ReviewWords_DerivedCandidateShowsSourceCompoundNoticeOutsideCollapsedDetails` — RED 0 passed / 1 failed (missing derived-source Review Words markup) → GREEN 1 passed / 0 failed. Characterization tests: 2 passed / 0 failed. Focused affected-scope verification (`UiWorkflowContractTests|GermanDerivedTermPreparationTests|TextReviewServiceTests|LocalizationResourceTests`): 163 passed / 0 failed. `git diff --check`: PASS.
- **Independent `REVIEW_ONLY`:** 0 BLOCKER / 0 MAJOR findings; two non-blocking MINOR findings were recorded (a source-level observation that very long German source compounds could benefit from explicit wrapping/hyphenation styling on narrow viewports, and a singular-wording observation when multiple distinct source compounds are joined into one label) plus one NIT (negligible per-render label recomputation); decision `READY_FOR_DOCUMENT_ONLY`. These are recorded operational review-history observations, not confirmed rendered-GUI defects and not established product limitations, since no rendered-GUI or platform-runtime evidence exists for this candidate.
- **Exact-head `FULL_VALIDATION` (validated PR head `d1de7556bfd346f3aa1fdd19741c7d1a6d647ba6`):** 2251 passed / 0 failed; Windows Debug PASS; Windows Release PASS; Android Debug PASS; Android Release PASS; strict warning/linking gate PASS; exit code 0; log `artifacts/launcher-logs/ValidateAll-20260822-142947.log`. `POST_MERGE_SYNC_ONLY` completed successfully; local `master`/`origin/master` synchronized to `bd67393f81cece98c3d8c58c5ea26ef3e8920079`.
- **Evidence boundary (explicit):** the `IMPLEMENT`/`REVIEW_ONLY` evidence above proves only source/markup-contract checks and automated service/integration-characterization tests against isolated temporary SQLite databases. The exact-head `FULL_VALIDATION` evidence additionally proves the complete automated test suite and Windows/Android Debug/Release build success for that exact commit. Neither proves rendered GUI/WebView correctness, device/emulator runtime behavior, APK/AAB installation behavior, signing correctness, or Google Play/store readiness.
- **Remaining lifecycle:** none. `DOCUMENT_ONLY`, `COMMIT_ONLY`, exact-candidate-HEAD `FULL_VALIDATION`, `PUSH_ONLY`, `PR_ONLY`, manual user merge, and `POST_MERGE_SYNC_ONLY` are all complete for Package 5B.

## Daily New-Word Budget & Learning-Day Infrastructure — Slice 1 (Merged Production State)

**Lifecycle status:** merged production `master` state via PR #142 (`feat: add daily new-word learning-day budget`; merge commit `34afed431711dd165b334d66b50b251a839faf02`; validated PR head `e7b6a0ad6a1159f94035b813bf747325bc314e8a`). Exact-head `FULL_VALIDATION` on the validated PR head: 2276 passed / 0 failed / 0 skipped, Windows Debug PASS, Windows Release PASS, Android Debug PASS, Android Release PASS, strict warning/linking gate PASS with 0 warnings / 0 errors, exit code 0 (log `artifacts/launcher-logs/ValidateAll-20260823-004831.log`). `POST_MERGE_SYNC_ONLY` completed successfully; Schema-12 activation was this package's historical contribution; current production has since advanced to Schema 13.

This merged slice establishes the non-visual daily new-word budget, durable `ActiveBudgetDay` and `Bridge` state, timezone/cutoff infrastructure, Schema-12 persistence, and active-session rollover reconciliation:

- **Hard Daily New-Word Limit:** The existing `PreparationLimit` ($N \in \{5, 10, 20, 30, 50\}$, default 10) governs the daily new-word admission budget. $N$ is enforced as a hard maximum of distinct genuinely-new `WordId`s per logical learning day.
- **Single `WordId` Slot Consumption:** Exactly one slot is consumed per `WordId` regardless of whether the word has multiple card directions (`TermToMeaning`, `MeaningToTerm`) or multiple senses.
- **Genuinely New Classification:** A word is "genuinely new" if and only if no persisted genuine `LearningReview` / rating exists for any card of that `WordId`. Queueing, rendering, reveal, typing checks, and `LearningDayGrant` evidence do not count as learning.
- **Slot Ordinal Immutability & Limit Gating:** Admitted words receive immutable `SlotOrdinal` assignments ($0, 1, \dots, N-1$). Reducing $N$ preserves existing queue rows, grants, and order, but restricts presentation to items with `SlotOrdinal < N`. Deferred items remain durably persisted. Raising $N$ admits additional candidates into higher slot ordinals.
- **No Same-Day Reopening:** Rating a card or marking a word Permanently Known never reopens or recycles a slot on the same day.
- **Learning Day & Timezone Model:** Effective timezone is resolved from `LearningTimezoneMode` (System or Explicit) and effective cutoff defaults to `00:00` (configurable minute-of-day). The active budget day freezes its effective timezone, cutoff, start, and end timestamps until transition.
- **Bridge Phase Semantics:** At old day end, if the next regular boundary under the requested configuration is in the future, the system enters `Bridge` phase. Bridge grants 0 new-word budget and blocks genuinely-new cards, while due reviews and already-learned New sibling cards proceed normally. Exact boundary equality transitions directly with no Bridge.
- **Active-Session Rollover Reconciliation:** Active sessions surviving day rollover consume the new day's slots with carry-over genuinely-new words first. If carry-over count $K < N$, remaining capacity admits fresh candidates. If $K \ge N$, no fresh candidates are admitted and excess carry-over grants remain durable but deferred. Deduplication inspects incomplete queue representation rather than historical completed rows.
- **Schema 12 Persistence:** Implements local singleton `LearningDayState` and `LearningDayGrants` tables. Strictly installation-local and excluded from V2 portable archives.

## Settings GUI & Learning-Day Defaults — Slice 2A (Merged Production State)

**Lifecycle status:** merged production `master` state via PR #144 (`feat: add settings GUI and learning-day defaults`; merge commit `3c3b976b25a8e90da8c6f41ab8b9d667dead99cb`; validated PR head `40deec3be3b9672130804b42b1967922a07c1815`). Exact-head `FULL_VALIDATION` on the validated PR head: 2339 passed / 0 failed / 0 skipped, Windows Debug PASS, Windows Release PASS, Android Debug PASS, Android Release strict PASS, strict warning/linking gate PASS, exit code 0 (log `artifacts/launcher-logs/ValidateAll-20260823-044728.log`). `POST_MERGE_SYNC_ONLY` completed successfully; `DatabaseSchema.CurrentVersion` was 12 and portable archive format was V2 at this package's merge, before the Schema-13 / Archive V3 cutover.

This merged slice provides full user control over learning-day parameters and restores standard application defaults:

- **Settings Product Surface:** Settings renders in exact 13-card product order: (1) Language, (2) Appearance, (3) New words per day, (4) Learning time zone, (5) New learning day begins at, (6) Card directions, (7) Learning mode, (8) Enhanced Term Recognition, (9) Online dictionary lookup, (10) Portable data, (11) Help & Support, (12) Restore default settings, (13) Reset all application data.
- **Default-First Option Layout:** Card directions presents choices leftmost: Both directions, Term -> Meaning, Meaning -> Term. Learning mode presents choices leftmost: Automatic, Reading, Typing.
- **Learning Timezone Catalog:** Selection offers System (`TimeZoneInfo.Local`) or explicit selection from a curated catalog of 50 canonical IANA timezone identifiers (`KnownFirst.Core.Settings.LearningTimezoneCatalog`) spanning inhabited UTC-11 through UTC+14 (including American Samoa, Chatham Islands, Tonga, and Kiribati). Offset labels `(UTC±HH:mm) City` are computed dynamically at render time from `TimeZoneInfo`/`DateTime.UtcNow` (including Chatham fractional UTC+12:45 standard / UTC+13:45 daylight). An invalid persisted identity falls back safely to System. Positive vertical separation (`.setting-status`) is declared above the effective-timezone status line.
- **Deterministic 24-Hour Cutoff Selector:** `New learning day begins at` presents a two-part 24-hour selector (`#learning-day-cutoff-hour` covering `00`..`23`, `#learning-day-cutoff-minute` covering `00`..`59`, separated by visual colon `.time-separator`) with localized accessible labels, replacing native `<input type="time">` without AM/PM or host regional format dependencies. Persists exact integer minutes $0..1439$; `24:00` is impossible.
- **Restore Default Settings vs. Reset All Application Data:** "Restore default settings" is non-destructive: restores target defaults (Language=System, Appearance=System, New words per day=10, Timezone=System, Cutoff=00:00, Card directions=Both, Learning mode=Automatic, ETR=On) and strictly preserves the user's online-dictionary lookup consent without database reset. "Reset all application data" remains the destructive action: revokes online lookup consent, resets database, and restores target defaults.
- **Enhanced Term Recognition Default:** The missing-preference default is ON (`EnhancedTermRecognitionPolicy.DefaultEnabled = true`). An explicit `false` remains OFF.

## First-Run Onboarding & Daily-Budget UX — Slice 1 (Merged Production State)

**Lifecycle status:** merged production `master` state via PR #153 (`feat: add onboarding install-origin foundation`; merge commit `aef5662cf4c4ad07ad937a35cdd15b3a793e4e59`; validated PR head `36534afa4664eea99fcb41b2554b72e64d7a35ec`). `POST_MERGE_SYNC_ONLY` completed successfully; `DatabaseSchema.CurrentVersion` was 12 and portable archive format was V2 at this package's merge, before the Schema-13 / Archive V3 cutover.

This merged foundation slice establishes preference-backed onboarding state, startup install-origin classification, grandfathered budget pinning, and reset contracts:

- **Onboarding State Lifecycle:** `KnownFirst.Core.Settings.OnboardingState` defines application-local lifecycle states `Required = 1`, `InProgress = 2`, `Completed = 3`. `OnboardingStatePolicy` enforces serialization, valid transitions, and default values without SQLite dependencies.
- **Startup Install-Origin Classification:** `IInstallOriginClassifier` / `InstallOriginClassifier` runs as a singleton during application startup in `MauiProgram.cs` before language initialization. It distinguishes fresh installations (no onboarding marker + no legacy preference evidence => `Required`) from existing installations (no onboarding marker + legacy preference evidence => `Completed`). Already valid onboarding state is preserved without reclassification. Database-file existence is deliberately not used as evidence to avoid false positives on fresh installs where database files may be initialized before classification.
- **Grandfathered Daily-Budget Pinning:** Grandfathered existing installations without an explicit `preparation_limit` preference have the legacy effective value `10` pinned to `preparation_limit` so future default changes will not alter their established study rhythm. Existing explicit values are preserved; fresh installations are not pinned. `PreparationLimitPolicy.DefaultLimit` remains 10 with existing presets ($N \in \{5, 10, 20, 30, 50\}$).
- **Reset Contracts:** Destructive full reset sets `OnboardingState.Required` first before default restoration recreates legacy markers; online dictionary consent remains unconditionally revoked. Non-destructive "Restore default settings" leaves onboarding state untouched and preserves current online dictionary consent.
- **Persistence Boundary:** Onboarding state and install-origin markers are Preferences/application-local state, not SQLite. At this package's merge, `DatabaseSchema.CurrentVersion` was 12 and archive format was V2; current production uses Schema 13 / Archive V3.

## First-Run Onboarding & Daily-Budget UX Core (Merged Production State)

**Lifecycle status:** Merged production `master` state via PR #155 (`feat: add first-run onboarding and daily budget ux`) and PR #156 (`fix: unify settings and onboarding visual consistency`). `DatabaseSchema.CurrentVersion` was 12 and portable archive format was V2 at this package's merge, before the Schema-13 / Archive V3 cutover.

This multi-slice package completes first-run onboarding and daily new-word budget UX:

- **Dedicated First-Run Onboarding Host:** Rendered outside the standard `Router`/`MainLayout` tree whenever `OnboardingState` is `Required` or `InProgress`. Normal navigation chrome (desktop sidebar, mobile headers) and the `WhatsNewModal` are suppressed during onboarding.
  1. *Welcome & UI Language:* Introduces the core concept and allows selecting English, German, or Russian, plus System, Light, or Dark appearance.
  2. *Display Name:* Optional local name configuration.
  3. *Workflow:* Concise 3-step explanation of importing text, reviewing words, and practicing vocabulary.
  4. *Online Lookup:* Privacy-sensitive dictionary lookup consent (default OFF, requires affirmative user action).
  5. *Enhanced Term Recognition:* Opt-in German compound decomposition toggle (default ON).
  6. *Practice Setup:* Choice of Card Direction (Both, Term->Meaning, Meaning->Term) and Learning Mode (Automatic, Reading, Typing).
  7. *Daily Pace:* Daily new-word budget selection (`1`, `5 Recommended`, `10`, `Custom`).
  8. *Learning Day Timing:* Timezone selection (System vs 50 curated IANA cities) and 24-hour cutoff time (`HH:mm`).
  9. *Summary:* Review of all selected configuration and explicit Finish Setup action.
- **Lifecycle & Resume Safety:** Current step is persisted locally under `onboarding_step` via `IOnboardingProgressStore`. Unrecognized or invalid stored values normalize safely to the first step (`WelcomeLanguage`). No global Skip action exists.
- **Terminal Completion Coordination:** Coordinated via `IOnboardingCompletionService` / `OnboardingCompletionService` in strict sequence:
  1. Marks current build version seen in the What's New store (`whats_new_seen_version`) so What's New is suppressed on first run.
  2. Persists durable `OnboardingState.Completed`.
  3. Clears `onboarding_step` progress.
  4. Only after successful persistence raises `OnCompleted` callback to `Routes.razor` for same-process transition to the standard Router/MainLayout shell.
  *(Merged via PR #193 [KF-NAV-001]: explicit `Navigation.NavigateTo("/", replace: true)` upon completion guarantees returning always to Home and replacing the pre-onboarding route history entry).*
- **Optional Local Display Name:** Stored under `display_name` via `IDisplayNameStore` / `MauiDisplayNameStore`. Strictly device-local; not an account or profile; excluded from SQLite and portable archives. Blank or whitespace inputs map to absent (`null`). Editable and removable in Settings.
- **Daily New-Word Budget Domain Policy:**
  - Technical valid range: contiguous `1..50` in `PreparationLimitPolicy`.
  - Product default and recommended value: `5`.
  - Presets: `1`, `5`, `10`.
  - Custom values: Any valid integer $1..50$. Values above `15` render a localized, non-blocking workload advisory warning.
  - Existing grandfathered installations without explicit limit retain their pinned effective value `10`.
  - Invalid stored values normalize to `5`.
- **Reset Invariants:**
  - *Destructive Full Reset:* Sets `OnboardingState.Required`, clears progress, clears Display Name, revokes online lookup consent, and resets daily budget to `5`.
  - *Non-Destructive Restore Default Settings:* Preserves `OnboardingState` and `onboarding_step` progress, preserves Display Name, preserves online lookup consent, and resets daily budget to `5`.
- **Persistence Boundary:** At this package's merge, database schema was 12 and portable archive format was V2 (historical pre-cutover boundary). All onboarding, progress, and Display Name states reside in application Preferences.

## Home Personalization & Greeting (Merged Production State)

**Lifecycle status:** Merged production `master` state via PR #158 (`feat: personalize home greeting`; merge commit `955b27695eb0e1761b8c9f9604cbfbf1335e57b6`; validated PR head `ddc5663b5cc6b7b6b9494646d3977441ea9f1e66`). `DatabaseSchema.CurrentVersion` was 12 and portable archive format was V2 at this package's merge, before the Schema-13 / Archive V3 cutover.

- **Scope & Consumption:** `Home.razor` consumes the synchronous `IDisplayNameStore` singleton.
- **Localized Personalized Greeting:** When a normalized Display Name is present, renders a localized greeting (`Home_Greeting`) before the existing subtitle (`Home_Subtitle`) separated by a single whitespace:
  - EN: `Welcome, {0}.`
  - DE: `Willkommen, {0}.`
  - RU: `Добро пожаловать, {0}.`
- **Subtitle-Only Fallback:** When no Display Name is configured (null / absent), Home preserves the existing subtitle-only rendering without an empty greeting, placeholder, or spurious separator whitespace.
- **Home Heading:** The visible `KnownFirst` heading remains unchanged.
- **Persistence & Reset Boundaries:** Display Name remains application/device-local Preferences state. At this package's merge, `DatabaseSchema.CurrentVersion` was 12 and portable archive format was V2 (historical pre-cutover boundary). Excluded from SQLite and portable archives; preserved on Restore Defaults; cleared on destructive full reset.
- **Non-Goals:** No account, profile, cloud identity, new persistence abstraction, time-of-day greeting, avatar, Home redesign, or unrelated personalization.

## Manual Preparation Reliability & UX (Merged Production State)

**Lifecycle status:** Merged production `master` state via PR #160 (`fix: repair manual preparation entry`; merge commit `793bd9959b9e17c2c4579df4c22a928bf8a4222a`; validated PR head `351abcd643f046e11993b4af93a1fb92ba437ea9`). `DatabaseSchema.CurrentVersion` was 12 and portable archive format was V2 at this package's merge, before the Schema-13 / Archive V3 cutover.

This merged package resolves the manual preparation persistence defect, aligns Definition/Translation authority, establishes deterministic manual semantic identity reuse, streamlines the manual Preparation UI, and hardens save-versus-progression recovery:

- **Manual Schema-12 Acceptance:** Manual Definition-mode and Translation-mode preparation can now succeed without requiring an online lookup or lexical provider result. Manual fallback after an automatic lookup returns no selectable provider meaning is fully supported. Acceptance executes through the standard transactional pipeline (`Senses`, `Meanings`, `ContextSnapshots`, `LearningCards`, `Word.PreparationState`). No database schema migration was introduced.
- **Definition vs. Translation Authority:** Persisted preparation/import context derived from the owning `Document` authoritatively determines whether manual input represents a Definition or a Translation: Definition context persists Definition only (with null Translation); Translation context persists Translation only (with empty Definition). Legacy `DefinitionAndTranslation` is retained only as a bounded compatibility exception. Irrelevant hidden values cannot redirect semantic identity or persistence. No AI or network inference is involved.
- **Manual Semantic Identity and Reuse:** Exact repeated manual semantic meaning for the same vocabulary identity reuses the existing appropriate Sense and Meaning rather than creating duplicate semantic or card identities. Matching is deterministic and exact via exact meaning variant identity policies. Genuinely different manual meanings remain separated under split-not-guess behavior. Retained German derived whole-compound evidence or candidate frozen evidence links cleanly to the reused Meaning/Sense without duplicating learning cards.
- **Provenance and Evidence Boundaries:** Manual acceptance does not fabricate provider result data (no fake provider Meaning ID, source project/page/revision, attribution, or resolved provider index). Candidate frozen evidence remains authoritative.
- **Simplified Manual Preparation UX:** Normal Definition mode presents one primary multiline Definition field; normal Translation mode presents one primary multiline Translation field. Redundant form controls (canonical term, encountered form, Additional Note) are removed from the normal editor; candidate term and context metadata remain visible. Advanced options (Acronym expansion when applicable, Accepted spelling aliases) are collapsed by default. Mode-specific empty field validation displays dedicated localized error messages and focuses/reveals the invalid field. Raw exception details are never exposed to the user.
- **Shared Multiline Visual Language:** Preparation and Text Import use centralized `.text-area` styling (border, radius, background, padding, typography, focus ring, disabled state, vertical resize).
- **Save-versus-Progression Recovery:** Successful acceptance is transactionally committed before attempting to load the next candidate. Failure to retrieve or render the next candidate is not reported as a save failure; the UI informs the user that the item was saved but the next item could not be loaded, and Retry executes progression/loading only without repeating acceptance.
- **Neutral Preparation Action Semantics:** "End preparation" is styled as a neutral/secondary action in the bottom action bar with inline confirmation. Confirmation ends the active batch and its resumability while retaining already accepted learning cards and lasting Known/Ignored decisions; unresolved and skipped items return to the backlog. Competing disposition actions are suppressed while confirmation is open.

## Clean Domain & Application Layer Architecture (Merged Production State)

**Lifecycle status:** Merged production `master` state via PR #186 (`feat(clean-domain): clean domain learning-control and application boundary foundation` / `KF-CLEAN-DOMAIN-013-001`; merge commit `e7cb91aad8db49b5366dab96295c2e8aa20c92c7`; validated PR head `eae1ca38d157d13ddd830a635d6ce31dd2fe4336`). Exact-head `FULL_VALIDATION` passed (2896 passed / 0 failed / 0 skipped, all four Windows/Android Debug/Release build gates PASS, strict warning/linking gate PASS, exit code 0). `DatabaseSchema.CurrentVersion` was 12 and portable archive format was V2 at this package's merge, before the Schema-13 / Archive V3 cutover.

The original clean-domain package established the model in `KnownFirst.Core` and the separate `KnownFirst.Application` project before persistence and runtime activation. Schema-13 persistence, Archive V3, and production runtime integration have since been completed. The following describes the retained domain boundaries and current composition:

**1. Clean Core Domain Foundation (`KnownFirst.Core.Learning`, `KnownFirst.Core.Preparation`)**

- **AlreadyKnown (Word-Level):** Explicit, reversible user decision (`WordLearningControl`, `AlreadyKnownDecision`) indicating that the user knew the vocabulary before KnownFirst taught it. Non-destructive, does not manage Sense-level stop state, preserves initial decision UTC timestamp across idempotent repeated markings, and cleanly restores normal learning eligibility on `ClearAlreadyKnown()`.
- **StopLearning (Sense-Level):** Explicit, reversible Sense-scoped user decision (`SenseLearningControl`, `StopLearningDecision`) that halts learning for that specific Sense without deleting history or mutating scheduler state. Reversible via `Resume()`.
- **Active Learning Eligibility Policy:** `ActiveLearningEligibilityPolicy.IsEligible(wordControl, senseControl)` enforces that Word-level `IsAlreadyKnown` gates all Senses of that Word, while Sense-level `IsStopped` gates only the selected Sense.
- **Workflow-Local Preparation Disposition:** `PreparationCandidateDisposition.Excluded` is strictly a workflow-local candidate disposition and does not create a durable Word-level "Ignore" domain state or imply semantic deletion.
- **Answer Variant Roles:** `AnswerVariantRole` distinguishes `Required` from `AcceptedOnly` as response roles. Answer variants of one Sense do not create independent learning cards.
- **Mastery-Independent Interaction Presentation:** `LearningInteractionProgress` and `LearningInteractionPolicy` govern the presentation-level `Reading` versus `Typing` progression independently from memory stability, interval, difficulty, or mastery. `Automatic` mode advances to `Typing` after 2 consecutive recall successes and reverts to `Reading` after 2 consecutive typing failures (counters bounded $0..2$).
- **No Mastered/Retired Clean Domain States:** Clean domain contracts introduce no terminal `Mastered` or `Retired` states and no mastery thresholds.

**2. Separate Application Boundary (`KnownFirst.Application.Learning`)**

- **Layering & Project Isolation:** Real separate `KnownFirst.Application` class library (`net10.0`) depending strictly on `KnownFirst.Core`. `KnownFirst.Core` does not reference `KnownFirst.Application`. The production application project (`KnownFirst.csproj`) references `KnownFirst.Application`, and `MauiProgram` calls `AddKnownFirstLearningRuntime()`.
- **FSRS-6 Application Boundary:** `IFsrs6SchedulingService` / `Fsrs6SchedulingService` provides deterministic scheduling and factual review replay over the in-tree Core FSRS-6 engine.
- **Fail-Closed Immutability:** `Fsrs6ScheduleProjection` is a sealed record with get-only auto-properties validated at construction against Core invariants across all 4 FSRS states (`New`, `Learning`, `Review`, `Relearning`).
- **Fail-Closed Review Facts:** `Fsrs6ReviewFact` is a `readonly record struct` with a private initialization guard; uninitialized/default facts fail closed with `LearningScheduleCorruptionException`.
- **Single Authoritative Scheduler:** A single `Fsrs6Scheduler` governs both `Schedule` and `Replay`, preventing divergent FSRS policies.
- **Deterministic Replay & Exception Safety:** Review fact materialization occurs outside Core try/catch boundaries so caller enumerable exceptions propagate natively, while Core invariant rejections are safely wrapped in `LearningScheduleCorruptionException`.
- **Legacy Isolation:** Application projection contains no `IntervalDays`, `EaseFactor`, `Mastered`, `Retired`, `Suspended`, SQLite entity, or persistence ID.

**3. Current Production Invariants (After Foundation and Runtime Cutover)**

- **Active Schema & Archive:** `DatabaseSchema.CurrentVersion` is 13; current production exports use Archive V3. Fresh genuinely empty databases bootstrap directly to Schema 13; existing Schema 1–12 databases fail closed without automatic migration, reset, or mutation.
- **Production Scheduler Wiring:** `IFsrs6SchedulingService` / `Fsrs6SchedulingService` is the production scheduling authority; `KnownFirst.Application` is part of current production composition.
- **Schema-13 controls:** `WordLearningControls` and `SenseLearningControls` are persisted and consumed by runtime eligibility; Learn permanent-known saves the word-level control while preserving graph/history. User-facing reversal and sense stop/resume integration remain open under `KF-VOCAB-005` and `KF-VOCAB-006`.
- **Downstream Initiatives:** Archive V3 infrastructure (`KF-BACKUP-006`) and FSRS runtime cutover (`KF-FSRS-003`) are complete. The parent initiative remains open for Vocabulary workflows (`KF-VOCAB-005`, `KF-VOCAB-006`); `KF-CLEANUP-001` remains separate later cleanup in [docs/BACKLOG.md](BACKLOG.md).

## Historical Dormant Schema-13 Persistence & Migration Foundation (KF-PERSIST-013-001)

**Historical lifecycle status:** This records the pre-cutover foundation state. Its Schema-12, V2, dormant-runtime, and deferred-FK statements are superseded by the [Schema-13 / FSRS-6 merged production state](#schema-13--fsrs-6-merged-production-state-kf-fsrs-003) above.

This package establishes the physical SQLite Schema-13 storage structures, repositories, atomic persistence coordinator, and deterministic Schema 12 $\to$ 13 migration engine as a dormant foundation ahead of runtime FSRS activation and Archive V3:

**1. Physical Schema-13 Target Structures (`KnownFirst.Data.Migrations.Schema13.Schema13Ddl`)**

- **`FsrsCardStates`:** One-to-one scheduling-state table for `LearningCards` (`CardId` PK/FK to `LearningCards.Id`), storing `State` ($0 \dots 3$), nullable `Stability` ($\ge 0.001$), nullable `Difficulty` ($1.0 \dots 10.0$), nullable `LastReviewedAtUtc`, nullable `StepIndex`, and nullable `DueAtUtc`. State-dependent CHECK constraints enforce valid FSRS state configurations (`New`: null parameters; `Learning`/`Relearning`: non-null stability/difficulty/last-reviewed, step index 0; `Review`: non-null stability/difficulty/last-reviewed, null step index). Indexed by `IX_FsrsCardStates_State_DueAtUtc` on `(State, DueAtUtc)`.
- **`FsrsReviewHistoryEntries`:** Append-only factual review log storing `Id` (PK autoincrement), `StableId` (TEXT NOT NULL, unique event identifier), `CardId` (FK to `LearningCards.Id`), `SequenceNumber` (INTEGER NOT NULL $> 0$, per-card causal order), `Rating` ($0 \dots 3$, corresponding to `ReviewRating`), and `ReviewedAtUtc` (TEXT NOT NULL). Constrained by unique indexes `IX_FsrsReviewHistoryEntries_StableId` on `(StableId)` and `IX_FsrsReviewHistoryEntries_Card_Sequence` on `(CardId, SequenceNumber)`, and indexed by `IX_FsrsReviewHistoryEntries_Card_Replay` on `(CardId, ReviewedAtUtc, SequenceNumber)`.
- **`WordLearningControls`:** Physical storage for reversible Word-level `AlreadyKnown` user decisions (`WordId` PK/FK to `Words.Id`, `DecidedAtUtc` TEXT NOT NULL). Absence of a row represents `WordLearningControl.Default`; saving `Default` deletes the row. No dual-write to `Words.Status` and no semantic graph deletion.
- **`SenseLearningControls`:** Physical storage for reversible Sense-level `StopLearning` user decisions (`SenseId` PK/FK to `Senses.Id`, `DecidedAtUtc` TEXT NOT NULL). Absence of a row represents `SenseLearningControl.Default`; saving `Default` deletes the row. No dual-write to `Sense.Status` or scheduler columns.
- **Fail-Closed Shape Validation:** `Schema13ShapeValidator` enforces table existence, column nullability/affinities, primary keys, foreign key declarations, CHECK constraints, and exact index definitions before exposure.

**2. Clean Repositories & Atomic Persistence Coordinator (`KnownFirst.Data.Schema13`)**

- **`WordLearningControlRepository` & `SenseLearningControlRepository`:** Clean domain persistence mapping between Core control types and SQLite tables. Missing rows yield default control instances.
- **`FsrsCardStateRepository` & `FsrsReviewHistoryRepository`:** Separate repositories for FSRS card scheduling state and append-only factual review history. Replay history ordering is causal and deterministic.
- **`FsrsReviewPersistenceCoordinator`:** Atomically persists one caller-computed resulting `Fsrs6Card` and the corresponding factual `Fsrs6ReviewEvent` in a single SQLite transaction. Does not duplicate or recompute FSRS scheduling logic. State write and history append roll back together on failure.

**3. Deterministic Transactional Migration (`Schema13DormantMigration`)**

- **Version Boundaries & Transaction Safety:** `SourceVersion = 12`, `TargetVersion = 13`. Executes inside a single SQLite transaction. Source user_version $> 13$ fails closed with `FutureVersion`; source version 13 validates existing shape and integrity without mutation (`AlreadyApplied`); source version $\ne 12$ fails closed with `UnsupportedSourceVersion`.
- **Source Verification & Pre-Existing Artifact Guards:** Validates Schema 12 source shape via `Schema12ShapeValidator` before creating target structures. Fails closed if any Schema 13 target table or index already exists on a version-12 database.
- **Deterministic Historical Bootstrap (`Schema13LearningBootstrap`):**
  - Source `LearningReview` rows map 1:1 to `FsrsReviewHistoryEntries`, ordered deterministically by `ReviewedAtUtc` ascending with legacy `Id` as tie-breaker.
  - Deterministic `StableId` generated by `Schema13HistoricalReviewStableIdPolicy` using Sense identity, `CardDirection`, factual timestamp, rating, and multiplicity ordinal under the domain `KnownFirst.Identity.FsrsReviewHistoryEntry.MigrationBootstrap.v1`.
  - Target `Fsrs6Card` states are derived exclusively through `Fsrs6Replayer` over the mapped factual history. Legacy `IntervalDays` and `EaseFactor` do not synthesize FSRS stability or difficulty. Replay-derived `DueAtUtc` is preserved without legacy override.
  - Genuinely unreviewed cards map to `Fsrs6Card.New`. Progressed cards with missing review history fail closed.
  - `WordStatus.Known` words produce `WordLearningControl` rows using the surviving legacy `Words.UpdatedAt` timestamp. Schema 12 migration produces zero `SenseLearningControls`.
- **Exact Target Integrity Validation (`Schema13MigrationIntegrityValidator`):** Validates actual materialized target data against the source-derived bootstrap plan with exact equality for controls, event facts, causal sequences, stable identities, and binary64 FSRS parameters. Dropped, excess, wrong, or orphan rows fail closed.
- **Atomic Finalization:** `PRAGMA user_version = 13` is written only after shape and integrity validation pass. Any error rolls back all target DDL, data, and version changes. Source Schema 12 tables and rows remain preserved and unmodified.

**4. Preserved Production Boundaries**

- **Dormancy:** `DatabaseSchema.CurrentVersion` remains 12. Production `DatabaseSchema.InitializeAsync` does not invoke `Schema13DormantMigration`. Ordinary initialized production databases remain `user_version 12`.
- **Production Scheduler:** This historical foundation section predates the cutover; current production scheduling is owned by `IFsrs6SchedulingService` / `Fsrs6SchedulingService`.
- **Archive Format:** Portable archive format remains V2.
- **Foreign-Key Activation:** Physical target foreign keys are verified under explicit connection enforcement, but global production `PRAGMA foreign_keys = ON` activation remains deferred to `KF-FSRS-003`.
- **Downstream Ownership Today:** Archive V3 (`KF-BACKUP-006`) and production FSRS-6 cutover/DI composition (`KF-FSRS-003`) are complete. Vocabulary UI/service workflows (`KF-VOCAB-005`, `KF-VOCAB-006`) remain open; legacy cleanup (`KF-CLEANUP-001`) remains separate later work in [docs/BACKLOG.md](BACKLOG.md).

## Historical Archive V3 Transport & Schema-13 Merge Candidate (KF-BACKUP-006)

**Historical lifecycle status:** This records the pre-cutover V3 transport candidate; Archive V3 infrastructure is now merged and complete. Its Schema-12, ordinary-V2, and legacy-scheduler boundaries are superseded by the [Schema-13 / FSRS-6 merged production state](#schema-13--fsrs-6-merged-production-state-kf-fsrs-003) above.

This package implements the Archive V3 portable archive format evolution, export, restore, preflight planning, and transactional populated-target merge for validated Schema-13 databases:

**1. Portable Archive V3 Contract (`BackupModelContractV3`)**
- **Schema-13 Extensions:** Adds top-level payload collections for `WordLearningControls` (`BackupWordLearningControlV3`: `WordIdentity`, `DecidedAtUtc`), `SenseLearningControls` (`BackupSenseLearningControlV3`: `WordIdentity`, `SenseIndex`, `DecidedAtUtc`), `FsrsReviewHistoryEntries` (`BackupFsrsReviewHistoryEntryV3`: `StableId`, `CardIdentity`, `SequenceNumber`, `Rating`, `ReviewedAtUtc`), and `FsrsCardStates` (`BackupFsrsCardStateV3`: `CardIdentity`, `State`, `Stability`, `Difficulty`, `LastReviewedAtUtc`, `StepIndex`, `DueAtUtc`), with matching `BackupRecordCountsV3`.
- **Strict Invariant Validation:** `BackupArchiveWriterV3.ValidatePayloadGraphV3` enforces format version 3, non-null collections, strict UTC ISO-8601 timestamps, valid FSRS state constraints matching SQLite CHECK constraints, unique `StableId`s, gapless sequence numbers per card, valid ratings ($0 \dots 3$), and exact record counts before export or restore.

**2. Deterministic Archive V3 Export (`BackupModelMapperV3`, `BackupArchiveWriterV3`)**
- **Schema-13 Target Support:** Gated by `CanExportArchiveV3Async` ensuring target database is valid Schema 13 before export.
- **Deterministic Portable Order:** Sorts controls, history entries, and card states by stable portable identities (`WordIdentity`, `SenseIndex`, `CardIdentity`, `SequenceNumber`, `StableId`). Assigns deterministic 1-based portable card IDs for cross-installation reference.

**3. Empty-Target Restore (`BackupService.ImportIntoEmptySchema13Async`)**
- **Native V3 Restore:** Deserializes and restores native Archive V3 into empty Schema-13 databases with transactional rollback, restoring base entities, controls, history entries, and FSRS card states, finalized by `Schema13MigrationIntegrityValidator` validation.
- **Legacy V1/V2 Adaptation:** Adapts legacy Schema 7–12 archives deterministically into empty Schema-13 targets using `Schema13LearningBootstrap` to derive FSRS states, deterministic `StableId`s, and Word learning controls.

**4. Populated-Target Merge Preflight & Safety Copy (`Schema13MergePreflightPlanner`, `BackupService`)**
- **Pure Preflight Planning:** Read-only planner computing explicit action plan (`AddWordLearningControl`, `ReconcileWordLearningControlTimestamp`, `AddSenseLearningControl`, `ReconcileSenseLearningControlTimestamp`, `AppendFsrsReviewHistory`, `InsertFsrsCardState`, `UpdateFsrsCardState`, `PreserveTargetOnly`, `NoChange`).
- **Complete Fingerprinting:** Captures target state fingerprint with exact binary64 IEEE-754 `"G17"` double precision for stability and difficulty.
- **Pre-Merge Safety Copy:** Populated mutating merges write a validated Archive V3 safety copy of the pre-mutation target database before applying any changes. Read-only previews and executable `NoChanges` merges create no safety copy.

**5. Transactional Populated-Target Merge Writer (`Schema13MergeWriterExecutor`, `MergeWriterService`)**
- **Atomic Transaction Boundary:** `ApplySchema13Async` wraps target state re-capture, plan comparison, base-graph merge execution, Schema-13 extension execution, and final integrity validation in a single atomic transaction.
- **Stale-Plan Rejection:** Recomputes the write plan inside the transaction and compares it structurally with the preflight plan via `MergeWritePlanComparer`. Any target divergence aborts before the first mutation.
- **Exact Data Preservation:** Preserves exact binary64 `Stability` and `Difficulty` without precision loss. Exact `StableId`s, gapless sequences, and equal review timestamps are preserved. Word and Sense controls converge to the earliest timestamp (`Min(target, source)`).
- **Post-Write Validation:** Validates `pragma_foreign_key_check`, snapshot integrity, `ValidateExactReplay`, exact planned action counts, and preflight re-evaluation converging to `MergeNoChange`.
- **Fail-Closed Conflict Policies:** Divergent causal histories, `StableId` collisions, missing legacy review history, and ambiguous sibling senses without semantic discriminators fail closed.

**6. Backward Compatibility & Production Boundaries**
- Schema 7–12 databases continue using existing V1/V2 restore and merge paths. V3 archives reject legacy targets with `Schema13ArchiveIncompatibleWithLegacyTarget`.
- This historical candidate-era statement is superseded: current production is Schema 13 with Archive V3 and FSRS-6 runtime authority; `KF-FSRS-003` is merged.

## Evidence Boundaries & Release Limitations

- **Release Packaging & Distribution Boundary:** Source merge is not a packaging or distribution event. A signed Android Release APK (`KnownFirst-1.0.0-beta.13-android-release.apk`, SHA-256 `53bbcb18b62927dae0af0a63d0e6a3cda6a8420c1c9517d354c638504b9ac6b6`) was produced and verified on physical hardware for manual Android validation. Final Release AAB creation is explicitly authorized for Google Play Internal Testing once the mandatory Pre-AAB Gate is certified on merged `master` (AAB is not yet created). Subsequent Google Play Internal Testing upload/distribution is separately authorized (not yet performed). A real self-contained Windows Portable Release ZIP (`KnownFirst-1.0.0-beta.13-build13-win-x64-9e455d0.zip`) and matching SHA-256 sidecar were successfully produced and structurally verified from synchronized `master` commit `9e455d0e03494cac8e713cd4d16c66946124f852` on 2026-08-16. No real MSIX package has yet been produced; the Portable package has not been launched or installed on a clean/secondary PC, and no external distribution has occurred.
- **Pre-AAB Release-Readiness Gate:** Mandatory pre-AAB release-readiness verification ([docs/BUILD_AND_RELEASE.md](BUILD_AND_RELEASE.md) §7) remains strictly required on the live candidate HEAD before any future release package creation.
- **Store Identity:** Partner Center Store identity inputs remain template placeholders (`devidentity`).
- **Support KnownFirst:** Unimplemented planned feature; completely absent from production rendering without placeholders.
- **Cloud & Accounts:** No cloud synchronization, accounts, analytics, advertising, or payments exist. All persistence and backups are local-first.
