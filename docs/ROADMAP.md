# KnownFirst Roadmap

**Prioritization date:** 2026-08-29

This roadmap records intended sequence and priority. Verified current implementation state belongs in [PROJECT_STATE.md](PROJECT_STATE.md); active operational task state belongs in [CURRENT_WORK.md](CURRENT_WORK.md); durable accepted open work and initiative dependencies belong in [BACKLOG.md](BACKLOG.md).

## Active Priority

The **First-Run Onboarding + Daily New-Word Budget UX** program is completed and merged to `master` (PRs #153, #155, #156, #158, #181, #182). Following onboarding completion, two foundational architecture packages were merged to `master`: the in-tree deterministic FSRS-6 core scheduling engine (`KF-FSRS6-CORE-001`, PR #184, ADR-0008) and the clean domain learning-control foundation with a separate, dormant `KnownFirst.Application` project (`KF-CLEAN-DOMAIN-013-001`, PR #186).

Durable backlog tracking and repository governance are established through [docs/BACKLOG.md](BACKLOG.md), which serves as the authoritative single source of truth for all accepted open work, product decisions, deferred follow-ups, and multi-package initiatives outside active implementation branches. Active operational task state is discovered dynamically per [docs/NEW_CHAT_BOOTSTRAP.md](NEW_CHAT_BOOTSTRAP.md) and tracked in [docs/CURRENT_WORK.md](CURRENT_WORK.md).

Accepted multi-package initiatives (FSRS Production Cutover & Clean Domain Persistence, Vocabulary Management Area, Learning Interaction & Direction UX, and Navigation/Settings Corrections) have their requirements, constraints, and dependencies established in [docs/BACKLOG.md](BACKLOG.md); their global execution sequence is recorded under Accepted Initiatives below, pending explicit product prioritization.

Beta-13 Build-14 AAB creation and packaging evidence are recorded as completed in repository history (PR #152); Google Play Internal Testing distribution (`KF-RELEASE-005`) and Public-Release Readiness (Priority 20) remain separately deferred until explicitly authorized.

## Status Definitions

- **Committed:** Merged and verified on `master`.
- **Current:** Active scoped work on a branch under review/verification.
- **Planned:** Accepted priority sequence, implementation not started.
- **Deferred:** Intentionally outside the current sequence. Resuming a deferred milestone (e.g. public release/AAB work) requires explicit user authorization; deferred does not mean cancelled.
- **Public-release blocker:** Must be resolved before any public Google Play promotion.

## Prioritized Milestones

| Priority | Milestone | Status | Required outcome |
| ---: | --- | --- | --- |
| 1 | Beta 10 Internal Testing | Committed | Portable `.kfarchive` export/import for empty targets, native file pickers, one-time What's New notice, Beta 10 identity (PR #18). |
| 2 | Russian UI & Translation Target | Committed | Russian UI localization (PR #20), Learn repeat/direction clarity (PR #21), Beta 11 identity (PR #22), Beta 12 Russian translation target fix (PR #23/PR #32); distributed via Google Play Internal Testing. |
| 3 | Meaning & Backup Foundations | Committed | Backup Merge Slices 1–3 (PRs #26–#28), Meaning Slices 0–3 (PRs #29–#33); dormant Schema-8 foundations merged to `master`. |
| 4 | Tooling Infrastructure & Safeguards | Committed | Windows StartupSmoke GUI launcher (PR #35), New-Chat Bootstrap Protocol (PR #36), Google Play packaging safeguards (PR #37). |
| 5 | Meaning Slices 4–5 | Committed | Slice 4 direction-specific progress replay (PR #40); Slice 5 Sense-addressed cards and queues (PR #41). |
| 6 | Schema 8 Activation | Committed | Schema 8 activation and live user-data migration engine (PR #42). |
| 7 | Schema-8 MergePreflight | Committed | Merge preflight planning and preview adaptation (PR #43). |
| 8 | Populated Target Merge Writer | Committed | Transactional populated-target merge writer and Import routing (PR #44). |
| 9 | Import UI & Localization | Committed | Import preview UI, EN/DE/RU localization, convergence validation (PR #45), Windows export atomic replacement (PR #48). |
| 10 | Documentation Governance & Export Staging | Committed | Release-readiness rules (PR #49), Android export staging/validation (PR #50). |
| 11 | Schema-9 History & Package A | Committed | Schema-9 review-session history storage (PR #51), Package A completed-review convergence (PR #52). |
| 12 | Authoritative Documentation Reconciliation | Committed | Documentation governance packages D1–D5 (PRs #53–#63). |
| 13 | Schema-9 Convergence (Packages B & C) | Committed | Package B writer evidence (PR #65), Package C convergence hardening (PR #68). |
| 14 | Public-Release Support Surface | Committed | 14A placeholder removal (PR #71), 14B reopenable release-note history (PR #73), functional Report a bug email composer (PR #98, PR #99). |
| 15 | Portable Merge Integrity Hardening | Committed | Schema-10 stable identity (PR #79), empty-target Active workflow restore (PR #81), populated-target Active convergence (PR #83), canonical export orderings (PR #85, PR #87), Occurrence action-key correction (PR #89). |
| 16 | Automated GUI Validation | Planned (P16-A & P16-B Committed) | P16-A Android automation foundation merged (PR #91). P16-B Android S36 source mapping merged (PR #164; contract tests only; runtime matrix evidence remains separate). P16-C Windows launcher integration tracked under `KF-GUI-001`; live runtime matrix evidence and critical scenario validation tracked under `KF-GUI-002`. Source Mapping != Runtime Execution Pass != Full Matrix-Row Completion. |
| 17 | German Enhanced Term Recognition — Package 1 (Production Morphology Lexicon) | Committed | Offline, deterministic `GeneratedGermanLexicon` / `IGermanLexicon` production implementation backed by `german-lexicon.v2.kfgl` (pinned `DuyguA/german-morph-dictionaries` commit `1780890c0fd25a989201c96000af323cd201fa5c`), plus generator/publisher maintenance tooling and focused tests. Merged via PR #134 (merge commit `6c7a89ed6b4b0fc7701fdca8ec85a38b91bbeeb5`). See [docs/CURRENT_WORK.md](CURRENT_WORK.md) and [docs/PROJECT_STATE.md](PROJECT_STATE.md). |
| 18 | German Enhanced Term Recognition — Packages 2–3 (Application Integration) | Committed | Packaged the `.kfgl` asset into the MAUI Windows/Android application; registered production `GeneratedGermanLexicon` as `IGermanLexicon`; connected `EnhancedTermRecognitionEnabled`; wired `TextReviewService` analysis and `DerivedTermEvidence` persistence (Schema 11) through the production lexicon; exposed the setting in Settings UI. Merged via PR #134. |
| 19 | German Enhanced Term Recognition — Package 4 (Multi-Component / Fugen / De-Inflection Decomposition) | Committed | Extended `ConservativeGermanCompoundDecomposer` from exactly two components to a bounded ordered decomposition of 2–4 lexicon-confirmed components (`GermanCompoundDecomposition.Components`); minimum component span length 2; final/head component must resolve as `Noun`; literal resolution always wins, falling back only to a single unified, closed suffix-stripping mechanism whose shipped set is exactly `s`, `es`, `e` (`n`/`en`/`er` evaluated but not shipped); zero/multiple valid partitions and multiple distinct fallback interpretations for one span fail closed; `TextAnalyzer` consumes every ordered component; Package-3 provenance/persistence contracts unchanged. Full contract: [docs/WORD_ANALYSIS.md](WORD_ANALYSIS.md). Includes the build-isolation correction (nested `GermanLexiconGenerator` project tree excluded from the root app's default Compile items). Merged via PR #134. |
| 19a | German Enhanced Term Recognition — Package 5A (Derived-Term Post-Review Lifecycle Integrity) | Committed | Retains a derived candidate's `ReviewCandidateEntity`/`DerivedTermEvidenceEntity` past review completion while the word stays Unknown, so Preparation can still recover real source-compound context; protects the retained evidence's Document/SentenceSpan from generic maintenance cleanup; cleans retained state up on MarkKnown/Exclude; filters the portable V2 export so a retained-evidence-owning candidate never leaks as a provenance-less item. No schema/archive-format change. Merged via PR #135 (merge commit `683f34473dd21417be9d8e1b60d04de539fb35a8`); exact-head `FULL_VALIDATION` 2227 passed / 0 failed / 0 skipped, all four Windows/Android Debug/Release build gates PASS, exit code 0. See [docs/CURRENT_WORK.md](CURRENT_WORK.md), [docs/PROJECT_STATE.md](PROJECT_STATE.md), [docs/DATABASE_CONTRACT.md](DATABASE_CONTRACT.md). |
| 19b | German Enhanced Term Recognition — Package 5A-2 (Portable/Cross-Installation Derived-Evidence Transport) | Committed | Cross-installation transport of retained `DerivedTermEvidence` through portable export, full backup, merge safety copy, empty-target restore, and populated-target merge, using an installation-independent semantic merge identity (no SQLite/archive-local ids). No schema/archive-format change. Merged via PR #137 (merge commit `5d1d3c05bae6ab9f1c56d8c5f9a227121f432f9a`); exact-head `FULL_VALIDATION` 2248 passed / 0 failed, all four Windows/Android Debug/Release build gates PASS, exit code 0. See [docs/CURRENT_WORK.md](CURRENT_WORK.md), [docs/PROJECT_STATE.md](PROJECT_STATE.md), and [docs/BACKLOG.md](BACKLOG.md). |
| 19c | German Enhanced Term Recognition — Package 5B (Review Words Derivation-Source Indication) | Committed | Minimal Review Words UI indication, using the already-existing `ReviewCandidateDetails.Provenance`/`DerivationEvidence` projection, so a user reviewing a derived component can understand it came from a source compound (`SourceSurfaceForm`), without turning the product into a complex morphology inspector. Closes the remaining approved German derived-term regression-coverage gaps through native Exclude cleanup and Preparation→Learning continuity characterization tests not already covered by Package 5A's review-added tests. Merged via PR #140 (merge commit `bd67393f81cece98c3d8c58c5ea26ef3e8920079`); exact-head `FULL_VALIDATION` 2251 passed / 0 failed, all four Windows/Android Debug/Release build gates PASS, strict warning/linking gate PASS, exit code 0. See [docs/CURRENT_WORK.md](CURRENT_WORK.md) and [docs/BACKLOG.md](BACKLOG.md). |
| 19d | Daily New-Word Limit & Learning-Day Infrastructure (Slice 1) | Committed | Non-visual daily new-word budget ($N \in \{5, 10, 20, 30, 50\}$, default 10), durable `ActiveBudgetDay` and `Bridge` state, timezone and cutoff infrastructure, Schema-12 persistence, and active-session rollover reconciliation. Merged via PR #142 (merge commit `34afed431711dd165b334d66b50b251a839faf02`). |
| 19e | Settings GUI & Learning-Day Defaults (Slice 2A) | Committed | Settings GUI exposing the Slice-1 learning-timezone (50-entry curated IANA catalog spanning inhabited UTC-11 through UTC+14, dynamic DST-aware `(UTC±HH:mm) City` labels) and learning-day-cutoff (deterministic 24-hour two-selector UI, `00..23` hours and `00..59` minutes, format `HH:mm`) infrastructure; non-destructive "Restore default settings" action distinct from destructive "Reset all application data" with exact online-dictionary-consent preservation/revocation contracts; Enhanced Term Recognition missing-preference default changed from OFF to ON; default-first option presentation for Card directions and Learning mode; Restore Defaults positioned directly preceding Reset all application data. No schema/archive-format change. Merged via PR #144 (merge commit `3c3b976b25a8e90da8c6f41ab8b9d667dead99cb`; validated PR head `40deec3be3b9672130804b42b1967922a07c1815`). See [docs/CURRENT_WORK.md](CURRENT_WORK.md) and [docs/PROJECT_STATE.md](PROJECT_STATE.md). |
| 19f | First-Run Onboarding & Daily-Budget UX — Slice 1 (Install-Origin Foundation & Reset Contracts) | Committed | Application-local preference-backed onboarding state (`Required`/`InProgress`/`Completed`), startup install-origin classification via legacy preference evidence without database-file dependency, grandfathered legacy budget pinning (10), destructive/non-destructive reset contracts (`Required` on full reset, unchanged on Restore Defaults). No schema/archive change (`DatabaseSchema.CurrentVersion` = 12, archive V2). Merged via PR #153 (merge commit `aef5662cf4c4ad07ad937a35cdd15b3a793e4e59`). |
| 19g | First-Run Onboarding & Daily-Budget UX Core (`onboarding-core-v1` & `visual-consistency-v1`) | Committed | 9-screen dedicated onboarding experience outside normal router/chrome, restart resume, optional local Display Name, daily new-word budget presets (`1 / 5 Recommended / 10 / Custom`, range 1..50, product default 5, >15 non-blocking warning), What's New completion coordination, and reset preservation contracts (PR #155), unified with shared Settings visual primitives, Step 1 System Language/Appearance selection, and accessible inline consent revocation (PR #156). |
| 19h | Home Personalization & Greeting | Committed | Home greeting consuming the optional local Display Name (`Home.razor`, localized EN/DE/RU: `Welcome, {0}.` / `Willkommen, {0}.` / `Добро пожаловать, {0}.`), prepended before the existing subtitle, preserving the subtitle-only layout when no name is configured. Merged via PR #158. |
| 19i | Online Lookup Consent Enforcement (KF-ONLINE-LOOKUP-CONSENT-001) | Committed | Fail-closed privacy architecture for post-onboarding online dictionary lookups: `IOnlineLookupAuthorizationGate` / `OnlineLookupAuthorizationHandler` transport gate, authorization-epoch-bound orchestration and prefetch safety with revocation cancellation, contextual consent disclosure removed from Prepare Words, dedicated blocked-candidate Prepare Words state with Settings navigation and manual fallback, Automatic Online method disabled when consent is OFF, lookup retry disabled while consent is absent. Settings is the sole post-onboarding authority. No schema/archive-format change. Merged via PR #181 (merge commit `be70ffcede1fb03f9083c21e148c963658459c8b`). See [docs/CURRENT_WORK.md](CURRENT_WORK.md) and [docs/PROJECT_STATE.md](PROJECT_STATE.md). |
| 19j | First-Run Onboarding — Transactional Settings & Recovery (KF-TRANSACTIONAL-ONBOARDING-001) | Committed | Transactional draft-and-commit onboarding model: versioned `OnboardingDraft` accumulating all choices (including nullable online consent), applied atomically at Finish Setup; persisted restart resume; language/theme preview non-persisting during the flow; `OnboardingCompletionJournal` with deterministic fingerprint, pre-write durability barrier, idempotent roll-forward, and startup recovery before database initialization; fail-closed on unsupported future journal data; crash-safe legacy migration via Capturing/Normalizing marker protocol; null consent cannot retain progress past Online Lookup step; draft true consent does not authorize transport (Package A gate remains closed until verified roll-forward). No schema/archive-format change (`DatabaseSchema.CurrentVersion` = 12, archive V2). Merged via PR #182 (merge commit `172587f4dc52bf3f5573bcfda53297de3216d3b6`; validated PR head `5d8cada0406c4243a9a4d2cea51c6c1491ab2c6d`). See [docs/CURRENT_WORK.md](CURRENT_WORK.md) and [docs/PROJECT_STATE.md](PROJECT_STATE.md). |
| 20 | Public-Release Readiness | Deferred — blocker when resumed | Privacy disclosures and website documentation (`KF-RELEASE-003`), Wikimedia and open-data attribution review (`KF-ATTR-001`), store listings and materials (`KF-RELEASE-004`), Windows packaging/distribution validation (`KF-RELEASE-002`), Google Play AAB revalidation (`KF-RELEASE-001`), and automated GUI matrix evidence (`KF-GUI-002`). Google Play upload and Internal Testing distribution remain a separately authorized distribution milestone (`KF-RELEASE-005`). Required for public Google Play Production promotion; resumes on explicit user authorization, not cancelled. |
| 21 | Russian Source-Text Support | Deferred | Cyrillic tokenization/normalization, Russian Wiktionary parsing, Wikipedia fallback (tracked under `KF-LANG-002`). |

## Accepted Initiatives (Sequencing Pending Product Prioritization)

The following initiatives represent accepted product directions whose individual packages, acceptance criteria, and dependencies are durably tracked in [docs/BACKLOG.md](BACKLOG.md). Their global sequence and priority ordering are subject to explicit user confirmation rather than assumed numeric priority:

### 1. FSRS-6 Production Cutover & Clean Domain Persistence
- **Foundation State:** In-tree deterministic FSRS-6 core scheduling engine (`KF-FSRS6-CORE-001`, PR #184, ADR-0008), clean domain learning controls with dormant `KnownFirst.Application` project (`KF-CLEAN-DOMAIN-013-001`, PR #186), and dormant Schema 13 physical persistence and migration foundation (`KF-PERSIST-013-001`, PR #189) are committed to `master`. Archive V3 transport foundation (`KF-BACKUP-006`) is merged to `master` via PR #190 (feature head `03a6eff513b50377adfdee8dbb340f439e7dd0ce`, exact-HEAD `FULL_VALIDATION` passed, `POST_MERGE_SYNC_ONLY` complete).
- **Current:**
  - `KF-FSRS-003`: The Schema-13 / FSRS-6 cutover implementation candidate is locally complete and review-approved. Documentation reconciliation, candidate finalization, `FULL_VALIDATION`, push, and PR lifecycle remain before source integration is complete.
- **Open Downstream Packages:**
  - `KF-CLEANUP-001`: Legacy scheduler deprecation, column removal (`IntervalDays`, `EaseFactor`), and replay policy cleanup.
- **Dependency Sequence:** Completed Core, Domain, Persistence & Transport Foundations (`KF-FSRS6-CORE-001`, `KF-CLEAN-DOMAIN-013-001`, `KF-PERSIST-013-001`, `KF-BACKUP-006`) $\to$ Production Cutover (`KF-FSRS-003`) $\to$ release-oriented blocker triage for functioning Web and Android/AAB paths after merge. `KF-CLEANUP-001` is downstream and begins only when it is an appropriate, genuine blocker.

### 2. Vocabulary Management Area
- **Objective:** Replace the `/dictionary` placeholder card (`KF-VOCAB-001`) with a real, production-grade Vocabulary area.
- **Open Packages:**
  - `KF-VOCAB-002`: Scalable database-backed, normalization-aware, paginated search.
  - `KF-VOCAB-003`: Vocabulary detail view for senses, definitions, contexts, and learning history.
  - `KF-VOCAB-004`: Post-acceptance correction/editing of definitions, translations, and answer variants.
  - `KF-PREP-002`: Re-preparation / fresh lexical lookup for existing vocabulary.
  - `KF-VOCAB-005`: AlreadyKnown reversal UI and persistence ("learn this after all"). Consumes Schema 13 persistence (`KF-PERSIST-013-001`).
  - `KF-VOCAB-006`: Sense-level Stop Learning / Resume Learning UI and persistence. Consumes Schema 13 persistence (`KF-PERSIST-013-001`).

### 3. Learning Interaction & Direction UX
- **Objective:** Align learning card presentation and progression with genuine direction semantics.
- **Open Packages:**
  - `KF-LEARN-003`: Genuinely distinct presentation semantics for `CardDirection.TermToMeaning` (term front, meaning reveal; no long typing) and `CardDirection.MeaningToTerm` (meaning prompt, term answer; typing supported).
  - `KF-LEARN-004`: Direction-aware Automatic 2+2 interaction progression for MeaningToTerm (Reading first $\to$ 2 recall successes $\to$ Typing $\to$ 2 typing successes $\to$ lapse on 2 failures).
  - `KF-LEARN-005`: Open product decision resolving recall rating threshold (Good/Easy only vs any non-Again rating).
  - `KF-LEARN-006`: Dynamic context target masking in `ContextView.razor` matching actual encountered target length.
  - `KF-LEARN-007`: User-facing session summary and next-due phrasing improvements.
  - `KF-LEARN-008`: Learn card edit/correction entry point into Vocabulary detail flow.
  - `KF-LEARN-009`: Clearing stale `_actionFailed` error banners after successful actions in `Learn.razor`.

### 4. Navigation, Settings & Feedback Corrections
- **Open Packages:**
  - `KF-NAV-001`: Onboarding completion navigation to Home (`/`) after reset from Settings.
  - `KF-PREP-001`: Prepare Words link to Settings auto-scrolling and focusing Online Dictionary section.
  - `KF-SETTINGS-001`: Visible failure feedback when saving learning timezone in `Settings.razor`.
  - `KF-HOME-002`: User-centric learning progress metrics on Home (depending on product decision `KF-METRIC-001`).

## Cleanup & Release Sequence

1. **Repository Cleanup Program:**
   - **Package 1 (Safe Artifact Cleanup):** Merged via PR #110 (`Clean` / `Clean -Deep` launcher actions, log retention pruning, artifact safety).
   - **Package 2 (Script Organization & Path Portability):** Merged via PR #111 (`scripts/packaging/`, `scripts/validation/`, `scripts/tools/` layout; `$PSScriptRoot` / `__file__` dynamic root resolution).
   - **Package 3 (Documentation Hygiene & Path Governance):** Completed and merged (status compaction, obsolete doc deletions, ADR-0007 configurable checkout path governance).
   - **Future Cleanup Steps:** External local checkout cleanup remains separately authorized.

2. **Windows Distribution Packaging Validation:**
   - Source-controlled infrastructure merged via PR #107 (`publish-windows-portable.ps1`, `publish-windows-msix.ps1`).
   - Execution of real portable ZIP packaging, clean-PC portable validation, MSIX signing, and Store onboarding follow cleanup milestones.

3. **Beta-13 Pre-AAB Release-Readiness Gate (Google Play Internal Testing Packaging Complete):**
   - Source version `1.0.0-beta.13` (build 14) merged on `master` via PR #149; Database schema 12 active on `master` (PR #142).
   - Exact-master Pre-AAB Release-Readiness Gate ([docs/BUILD_AND_RELEASE.md](BUILD_AND_RELEASE.md) §7) and technical validation passed on synchronized `master` commit `8cd98d27ff81d8134b4e3b9d4b32b9b85abe3cb2`. Signed Google Play bundle `KnownFirst-1.0.0-beta.13-code14.aab` (`48,002,097` bytes, SHA-256 `7a84da599ae7435614d95ff316707669d69e21b311fe252f5419ac9cb8ecbbcd`, `StrictVerified`) was created and verified locally; packaging evidence merged to `master` via PR #152.
   - Google Play upload and Internal Testing distribution have not occurred and remain separately authorized; Public-Release Readiness / Production promotion remains separately deferred.

## Public-Release Blockers

Before any public Google Play release (beyond Internal Testing):
1. **Unfinished Control and Debug UI Gating:** Verified absence of unfinished controls or debug overlays in Release builds (owned by Pre-AAB Gate).
2. **Attribution & Licenses:** Wikimedia attribution and license metadata verification (`KF-ATTR-001`).
3. **Automated GUI Matrix Coverage:** Deterministic automation of critical matrix scenarios (`KF-GUI-002`).
4. **Store & Privacy Documentation:** Privacy policy and distribution terms (`KF-RELEASE-003`), and store listing materials (`KF-RELEASE-004`).
