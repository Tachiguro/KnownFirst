# KnownFirst project state

**Status date:** 2026-08-12
**State source:** current `master` baseline — `9a3480678168414e4b8060d0673ec41c8f74767a` (PR #90 merge commit). The exact current `master` HEAD and PR state remain live GitHub/Git facts discovered dynamically per [docs/NEW_CHAT_BOOTSTRAP.md](NEW_CHAT_BOOTSTRAP.md).
**Current merged lifecycle:** PR #90 merged the Priority-15 post-merge documentation closure and local `POST_MERGE_SYNC_ONLY` completed. PR #89's Occurrence action-key correction remains binding `master` behavior.
**Priority 15 completed:** the correction replaces the production-representable `SentenceId:VocabularyId` Occurrence action lookup collision with the shared lookup-only `SourceMaterialArchiveId:Occurrence.Order` key. The V1 planner, V2 planner, and writer share the invariant-culture helper; occurrence `Order` is unique within validated source material graphs and archive IDs reject `:`. Semantic `ComputeOccurrenceIdentity`, classifications, preview counts, multiplicity, ordering, convergence, archive format/version, DTOs, Schema 10, migrations, LearningReview semantics, scheduler, UI, transport, synchronization, persistence, and public error/status-code contracts remain unchanged. Focused TDD: **0 passed / 2 failed / 0 skipped** → **2 passed / 0 failed / 0 skipped**; bounded TEST_ONLY: **257 passed / 0 failed / 0 skipped**; independent review: **0 BLOCKER / 0 MAJOR / 0 MINOR / 0 NIT**. Evidence is isolated synthetic-SQLite automated component/integration/contract evidence only, not ALL_AUTOMATED, ValidateAll, GitHub CI, platform/build, GUI/device, package, or release evidence. Priority 15 is Committed.

**Active development candidate — P16-A:** `feature/p16a-android-gui-foundation-v1` is an unmerged, non-binding Priority-16 candidate based on `9a3480678168414e4b8060d0673ec41c8f74767a`. The initial implementation commit is `e3a92fa83366bc1cbfd33dd718c3d430d79ab037`, and the duplicate-configuration correction commit is `112091226f5286b6239db926c78d012f0978edf1`; the exact live feature-branch HEAD is dynamic Git/GitHub state discovered according to [docs/NEW_CHAT_BOOTSTRAP.md](NEW_CHAT_BOOTSTRAP.md). PR #91 is open. Its initial independent review found **0 BLOCKER / 2 MAJOR / 3 MINOR / 0 NIT** and required correction; the resulting correction implementation is local and not yet committed or pushed. The corrected contracts cover owned Appium listener/PID fail-closed readiness with normal free-port handling that does not swallow true inspection errors, rendered clean full-SHA build-identity agreement, a strict repository-owned run-directory boundary, and exact dependency/lockfile version assertions. Final local correction review cleared the package for TEST_ONLY; bounded local TEST_ONLY returned **159 passed / 0 failed / 0 skipped** MSTest and **5 passed / 0 failed / 0 cancelled / 0 skipped / 0 todo** Node. The XML-aware `AndroidGuiTestVariant_IsDeclaredExactlyOnce` regression still guards the verified removal of the duplicate P16-A Android GUI-test PropertyGroup; exactly one matching `KnownFirstAndroidGuiTest=true` / Debug / Android PropertyGroup remains. It is not a merged production capability and does not make `com.tachiguro.knownfirst.guitest` a distributed, production, beta, or merged-master package identity. No runtime/device/platform/package evidence exists: Android platform build, package, installation, Appium session, UiAutomator2 runtime, Chromedriver/WebView compatibility, emulator/device behavior, rendered interaction, screenshots, and matrix coverage remain unverified.

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
- responsive Windows and Android layouts with localized workflow gating;
- Windows portable export stages the archive to a same-directory temporary file, validates it through the production `BackupArchiveReader.ValidateVersionedAsync` path, and only then atomically finalizes (`File.Replace` for an existing destination, `File.Move` for a nonexistent one), so a failure at any stage before finalization leaves an existing backup byte-for-byte unchanged (PR #48).
- Android portable export stages and strictly validates the archive before opening the destination picker; invalid or failed staging never acquires or writes the destination (PR #50).
- Schema-9 review-session history storage capability (PR #51).
- Package A (Schema-9 completed-review convergence): identity, planner, target-index parity, and characterization coverage (PR #52).
- Package B (Schema-9 completed-review writer evidence): genuine Schema-9 writer evidence and deterministic mapper reordering (PR #65).
- Package C (Schema-9 completed-review convergence hardening): cross-installation canonical ordering for completed `ReviewSession` and `SourceMaterial` subgraphs, and two-installation synchronization (PR #68).
- Milestone 14A (unfinished support/report control removal): the unfinished `Support KnownFirst` and `Report a bug` controls and their shared placeholder behavior removed from the production Settings source (PR #71).
- Milestone 14B (reopenable release-note history): Settings → Help & Support link and new `/release-notes` route exposing the complete existing release-note catalog newest-first (PR #73).
- KF-BACKUP-004 (Schema-9 populated-target LearningReview merge integrity): collision-free positional action keys (`lr#<archiveRowIndex>`), meaning-aware review-event identity with stable nullable Target/Matched AnswerVariant identities, and scheduler-replay alignment; `LearningSessionId` deliberately excluded from event identity (PR #77).
- KF-BACKUP-005A (Schema-10 stable learning-workflow identity foundation): immutable `StableId` columns on `LearningSessions` and `LearningSessionCards`, deterministic Completed bootstrap (SHA-256, 64 chars), one-time Active GUID bootstrap (32 chars), archive V2 DTO evolution, source ≤9 Completed compatibility, source ≤9 Active rejection, source ≥10 StableId validation; `LearningSessionId` excluded from `LearningReview` merge identity; Active workflow portable continuation excluded (deferred to 005B/005C) (PR #79).
- KF-BACKUP-005B (portable Active learning-workflow restore into an empty target): ordinary Schema-10 export carries an Active `LearningSession`, persisted queue state, committed `LearningReview` history, and preserved workflow/queue `StableId` values; empty-target restore resumes through normal `LearningService` from the last durably committed state. Completed Schema-10 workflow compatibility remains supported (PR #81, feature commit `e8236bba3d23e942014e6979b661e0c77a2a3bdd`, merge commit `dc56e8412966ac32531c4b0358526582702d6d24`).
- KF-BACKUP-005C (populated-target Active learning-workflow convergence and conflict safety): a learning-quiescent populated Schema-10 target can additively import an Active workflow; exact same-`StableId` durable equivalence is `NoChanges`; non-exact state is a non-executable, zero-mutation deterministic decision. Workflow/queue StableIds are preserved and LearningReview multiplicity is significant (PR #83, merge commit `bed54d01624e80ca6dd5adf8af097e64fe33e588`).
- `LegacyReviewSummaries` canonical ordering correction (PR #85, feature head `baf5fcda0a017c1492a08dac730d683c1554784d`, merge commit `8eeaea58d87f9cfeb28cc4fc2520e5b277bb2526`): `BackupModelMapperV2` now orders each vocabulary item's V2 `LegacyReviewSummaries` by typed emitted content — `ReviewCount`, `ForgotCount`, `PartialCount`, `KnownCount`, nullable `LastReviewedAt` presence (null first), then normalized UTC ticks for a present timestamp. Null is explicitly distinguished from present UTC `DateTime.MinValue`; local `ReviewStateEntity.Id` is not ordering material; multiplicity is preserved. Historical V1 mapper/writer behavior, Schema 10, archive V2, DTO shape, and merge policies are unchanged.
- `Learning.Cards` canonical ordering correction (PR #87, feature head `2cab8042887bed1004e7c26573a52fd59cc3b380`, merge commit `e97c83ac0cf7decf2915162e0e3a4abf24ee30d8`): valid/resolved V2 Cards now order semantic-first through existing `FutureCardIdentity`, preferred-Meaning `ExactMeaningVariantIdentity`, and typed emitted Card state, with Sense StableId only as late/final non-local ordering material. Malformed/unresolved-reference fallback remains deterministic and includes Direction; the mapper remains non-validating; multiplicity is preserved with no grouping or deduplication. Schema 10, archive V2, DTO shape, migration/validation, V1 compatibility, merge, scheduler, UI, transport, synchronization, public status/error-code, and persistence boundaries are unchanged.

KF-BACKUP-005B remains the historical empty-target capability. `LegacyReviewSummaries` canonical ordering is binding current-`master` behavior through PR #85 (merge commit `8eeaea58d87f9cfeb28cc4fc2520e5b277bb2526`); `POST_MERGE_SYNC_ONLY` completed successfully.

## Merged development foundations

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
- **Meaning Slice 8 (PR #44):** transactional Schema-8 populated-target merge writer and Import routing; verified with 1593 passed, 0 failed, 0 skipped.
- **Meaning Slice 9 (PR #45):** portable import preview UI, localized EN/DE/RU handling, corrected `LearningSession` identity, and end-to-end convergence validation; checkpoint result 1626 passed, 0 failed, 0 skipped on the feature branch prior to merge.
- **Windows GUI StartupSmoke Launcher (PR #35):** `-Action GuiTest` launcher entry point and profile isolation under `artifacts/`.
- **New-Chat Bootstrap Protocol (PR #36):** permanent dynamic bootstrap governance in `docs/NEW_CHAT_BOOTSTRAP.md`.
- **Google Play Packaging Safeguards (PR #37):** hardened `scripts/publish-google-play-bundle.ps1` with cross-process lock, warning escalation, candidate ownership, and sidecar verification.
- **Preparation selected-meaning acceptance fix (PR #46):** an invalid preparation context is now hidden rather than silently accepted.
- **Diagnostics/export stale lexical-reader fix (PR #47):** `PreparationCandidates.ResultJson` is now read via the payload codec in diagnostics and export paths.
- **Windows portable-export atomic-replacement fix (PR #48):** see "Production capabilities" above.
- **Documentation governance and release-readiness rules (PR #49).**
- **Android portable export staging (PR #50):** strict validation before destination acquisition.
- **Schema-9 review-session history storage activation (PR #51).**
- **Package A (Schema-9 completed-review convergence) (PR #52):** identity, planner, target-index parity, and characterization coverage.
- **Package B (Schema-9 completed-review writer evidence) (PR #65):** genuine Schema-9 writer evidence and a narrow deterministic `BackupModelMapperV2` `ReviewSession` ordering correction; no executable `MergeWriterExecutor` rewrite; no archive DTO/format, schema/migration, or public error-code expansion.
- **Package C (Schema-9 completed-review convergence hardening) (PR #68):** completed-ReviewSession cross-installation canonical ordering for the affected Schema-9 subgraph; SourceMaterial scalar + child-subgraph canonical ordering; focused two-installation convergence and repeated-exchange evidence.
- **D1 authoritative documentation reconciliation (PR #53).**
- **D1 closure and D2 activation (PR #54).**
- **D2 Agent Communication and Operation Governance (PR #55).**
- **D2 closure and D3 activation (PR #56).**
- **D3 Backup and Import Contracts (PR #57).**
- **D3 closure and D4 activation (PR #58).**
- **D4 Product, Workflow, and Release-Facing Documentation (PR #59).**
- **D4 closure and D5 activation (PR #60).**
- **D5 Testing and GUI Contract Reconciliation (PR #61).**
- **D5 Historical Banners and Routing Corrections (PR #62).**
- **D5 Mechanical Markdown Hygiene (PR #63).**
- **D5 closure and Package B revalidation queued (PR #64).**
- **KF-BACKUP-003 Package D — Schema-9 portable workflow canonical ordering (PR #76, merge commit `17d3f1a031b9f319041ff1034a227d17b1029c4f`):** `BackupModelMapperV2`'s v2 export ordering for completed `PreparationSessions`/`PreparationCandidates`, `LearningSessions`/`LearningSessionCards`, and `LearningReviews` is now total over emitted content, so archive-local `pb-*`/`pi-*`/`ls-*`/`lq-*` assignment and review emission order no longer fall through to installation-local SQLite row order. Archive-emission canonical ordering only — no database schema, migration, archive DTO, `.kfarchive` format version, or merge-identity change. `POST_MERGE_SYNC_ONLY` completed successfully.
- **KF-BACKUP-004 — Schema-9 LearningReview merge integrity (PR #77, merge commit `bec861fb8a054beb2804f1132b450da1e45dee90`):** every physical archive `LearningReview` receives a collision-free positional lookup key (`lr#<archiveRowIndex>`), Schema-9 meaning-aware review identity incorporates stable nullable `TargetAnswerVariant` and `MatchedAnswerVariant` identities, and scheduler replay is aligned to the same event semantics. `LearningSessionId` is deliberately excluded from event identity and preserved as referential attachment. `POST_MERGE_SYNC_ONLY` completed successfully.
- **KF-BACKUP-005A — Schema-10 stable learning-workflow identity foundation (PR #79, merge commit `e56b8bfa27dfe1d630fbacfed24e6d56ea876026`):** advances `DatabaseSchema.CurrentVersion` to **10** on `master`; adds immutable `StableId` columns to `LearningSessions` and `LearningSessionCards`; assigns deterministic SHA-256 64-char StableIds for Completed sessions and queue rows; assigns one-time GUID 32-char StableIds for Active sessions and queue rows; evolves V2 DTOs with trailing nullable StableIds; enforces source ≤9 Completed compatibility, source ≤9 Active rejection, and source ≥10 StableId validation; preserves KF-BACKUP-004 `LearningSessionId` exclusion from `LearningReview` merge identity; excludes Active portable workflow continuation (deferred to 005B/005C). Inherited baseline compiler blocker in `ReleaseNotes.razor` resolved. Validated via candidate `ValidateAll` checkpoint `551399df22131e0214e87b43a3eeaea9ae40ddf9` FULL GREEN (`1812 passed / 0 failed / 0 skipped`, Windows Debug/Release passed, Android Debug/Release passed, 0 build errors, 0 AOT/trimming/source-gen warnings, 8 non-blocking XML-doc warnings). `POST_MERGE_SYNC_ONLY` completed successfully.
- **KF-BACKUP-005B — portable Active learning-workflow restore into an empty target (PR #81, merge commit `dc56e8412966ac32531c4b0358526582702d6d24`):** feature commit `e8236bba3d23e942014e6979b661e0c77a2a3bdd` makes Schema-10 Active workflow export, durable queue/review transport, and empty-target resume binding `master` behavior. Final focused `TEST_ONLY`: `135 passed / 0 failed / 0 skipped`; final independent PR review: `0 BLOCKER / 0 MAJOR / 0 MINOR`; `POST_MERGE_SYNC_ONLY` completed successfully. No GitHub CI evidence existed for the 005B head.

**Current Status (master):**
- The active database schema is **10** (`PRAGMA user_version = 10`).
- Schema 10 is active during normal application initialization on master.
- Package C was merged via PR #68. D1-D5 documentation reconciliation is complete.
- Package A, Package B, and Package C are merged to master. D1 through D5 are complete (see [CURRENT_WORK.md](CURRENT_WORK.md) and [ROADMAP.md](ROADMAP.md)).
- Milestone 14A and Milestone 14B are both merged (PR #71, merge commit `39609ffffb39c69238882172d153f4bb795ddab8`; PR #73, merge commit `14138ccdab1e9b09a12ded002ff198d9b7312fcf`). Milestone 14 as a whole is complete on `master`. `POST_MERGE_SYNC_ONLY` completed successfully for both.
- KF-BACKUP-003 Package D is merged via PR #76, KF-BACKUP-004 via PR #77, KF-BACKUP-005A via PR #79, KF-BACKUP-005B via PR #81 (merge commit `dc56e8412966ac32531c4b0358526582702d6d24`), KF-BACKUP-005C via PR #83 (merge commit `bed54d01624e80ca6dd5adf8af097e64fe33e588`), `LegacyReviewSummaries` canonical ordering via PR #85 (merge commit `8eeaea58d87f9cfeb28cc4fc2520e5b277bb2526`), and `Learning.Cards` canonical ordering via PR #87 (feature head `2cab8042887bed1004e7c26573a52fd59cc3b380`, merge commit `e97c83ac0cf7decf2915162e0e3a4abf24ee30d8`); `POST_MERGE_SYNC_ONLY` completed successfully for all. The current master baseline is `e97c83ac0cf7decf2915162e0e3a4abf24ee30d8`.
- Historical KF-BACKUP-005B package baseline was `dc56e8412966ac32531c4b0358526582702d6d24` (PR #81 merge commit).

**Latest merged Priority-15 correction:**

- The V2 `Learning.Cards` correction replaces installation-random Sense-`StableId`-first ordering with semantic-first ordering for valid/resolved cards through existing `FutureCardIdentity`, preferred-Meaning `ExactMeaningVariantIdentity`, and typed emitted Card state; Sense StableId remains only late/final non-local material. It preserves mapper non-validation through a deterministic content-based fallback for malformed/unresolved-reference snapshots including Direction, preserves multiplicity, and introduces no grouping/deduplication.
- The defect is archive-local `c-*` binding instability across equivalent installations; `ReviewEvents`, `AnswerVariantProgress`, and workflow queue references consume those bindings. No corruption, semantic merge divergence, or data loss has been demonstrated.
- Final bounded affected/regression `TEST_ONLY` evidence is **119 passed / 0 failed / 0 skipped** across `BackupCreationTests`, `BackupArchiveV2Tests`, `BackupModelContractTests`, and `PortableImportEndToEndConvergenceTests`; this is not ALL_AUTOMATED, ValidateAll, CI, build, GUI/device, package, signing, publishing, or distribution evidence.
- Independent review found **0 BLOCKER / 0 MAJOR / 0 MINOR / 0 NIT**. Schema 10, outer archive V2, DTOs, migrations/validators, V1 compatibility, merge and scheduler semantics, UI, transport, synchronization, public status/error codes, and persistence contracts remain unchanged. PR #88 subsequently completed the PR #87 documentation closure; PR #89 completed Priority 15. Priority 16 is now Current through active non-binding P16-A.

**Historical KF-BACKUP-005B package status:**

- PR #81 merged feature commit `e8236bba3d23e942014e6979b661e0c77a2a3bdd` through merge commit `dc56e8412966ac32531c4b0358526582702d6d24`; `POST_MERGE_SYNC_ONLY` completed successfully.
- Database schema remains **10** and archive format remains **V2**; no Schema 11 or archive V3 exists.
- Active Schema-10 learning-workflow export and empty-target restore were the 005B package capability; final focused `TEST_ONLY` is **135 passed / 0 failed / 0 skipped**.
- At the 005B package boundary, populated-target preview was `Blocked` and import was `Failed` with `ActiveWorkflowUnsupported`, with no target mutation or executable merge/writer behavior.
- No application version/build increment or external distribution is associated with 005B.

Current master behavior is extended by merged KF-BACKUP-005C: a valid Schema-10/V2 Active learning workflow may be additively imported into a populated learning-quiescent target; exact same-`StableId` state is `NoChanges`, while non-exact state remains a non-executable, zero-mutation deterministic user-decision.

## Confirmed verification

### Automated

- Automated tests cover Core policies, text analysis, temporary SQLite persistence, workflow logic, localization, diagnostics, lookup providers with offline fixtures, script contract invariants, and archive contracts. Automated tests do not make live network requests.
- Test execution and status are tied to explicit commit and scope boundaries (see `docs/TESTING.md`).
- KF-BACKUP-005B final exact-tree focused `TEST_ONLY` covered `BackupArchiveV2Tests`, `BackupModelContractTests`, `Schema8BackupRestoreTests`, `BackupServiceImportRoutingTests`, and `MergePreflightServiceTests`: **135 passed / 0 failed / 0 skipped**, normal process completion, 0 build warnings, 0 build errors, with pre/post `git diff --check` passing.
- An earlier test-project run returned **1820 passed / 0 failed / 0 skipped** against the same unchanged 005B production implementation, but before the final acceptance-test additions. It is supplementary production-regression evidence, not exact-final-test-tree evidence.

### Platform builds

- **Windows / Android Debug & Release:** Build readiness verified during Beta 10, Beta 11, Beta 12 release preparation, and candidate `ValidateAll` validation.
- **KF-BACKUP-005B boundary:** `ValidateAll`, Windows platform build, Android platform build, Release-build behavior, rendered GUI, physical device/emulator behavior, APK/AAB, signing, publishing, and Google Play distribution were not validated. The earlier KF-BACKUP-005A `ValidateAll` result must not be reused as 005B executable-tree evidence.

## Database status

- Storage is local SQLite in the application data directory (`knownfirst.db3`).
- On `master`, `DatabaseSchema.CurrentVersion` and `PRAGMA user_version` are **10**.
- Schema 10 is active in real application databases on master.
- Initialization is forward-oriented and preserves existing rows while adding supported tables or columns.
- The initialization sequence on master advances fresh or legacy baseline databases to Schema 7, applies the Schema 8 migration, applies the Schema 9 migration, and then applies the Schema 10 migration.
- Initialization reads `PRAGMA user_version` first and rejects any version greater than the current version before modifying tables or cache.
- Complete persisted-data rules are in [DATABASE_CONTRACT.md](DATABASE_CONTRACT.md).
- Portable recovery format v1 is documented in [architecture/backup-format-v1.md](architecture/backup-format-v1.md).

## Known limitations

- Exported `.kfarchive` archives are not encrypted and may contain personal imported text and learning history; users are warned before export.
- "Support KnownFirst" and "Report a bug" remain unimplemented planned features. Milestone 14A removed both controls, their "coming soon" placeholder UI, and the shared placeholder state and handlers from the production Settings source (`Components/Pages/Settings.razor`), so they are no longer represented by any production control; they remain documentation-only, tracked in [ROADMAP.md](ROADMAP.md). The localization keys `Settings_SupportKnownFirst`, `Settings_ReportBug`, and `Common_FeatureComingSoon` are intentionally retained as unreferenced resource strings; a resource string is not a rendered product control. This state is established by source-contract evidence (Razor/CSS/test source inspection, `UI_CONTRACT_AUTOMATED` `70 passed / 0 failed / 0 skipped`) — it is not rendered-Release or AAB evidence.
- Cloud synchronization, accounts, analytics, advertising, and payments are not implemented.
- On `master`, KF-BACKUP-005B's Schema-10 empty-target Active workflow restore is extended by KF-BACKUP-005C's bounded populated-target convergence contract. Active `VocabularyReview` and Active `PreparationBatch` portability remain unsupported; source schema ≤9 Active workflows remain unsupported.
- Offline dictionary packages and FSRS scheduling are deferred.
- Online lookup requires explicit consent and network access on cache misses.
- Public Google Play release is intentionally not yet pursued.
- Tooling-only improvements (such as PR #37) do not create a new Beta 13 product release.

### Production-control and debug-UI policy

- A planned but unimplemented feature must remain documented only in [ROADMAP.md](ROADMAP.md) or other planning documentation; it must not appear in Release rendering as an enabled button, a disabled button, a link, a menu entry, a card, a placeholder label, a "coming soon" control, or an inaccessible/visually hidden interactive element.
- An unfinished control must be absent from the rendered Release component tree and accessibility tree, not merely hidden with CSS.
- Debug-only exposure of a planned control is permitted only when it is explicitly gated by an approved diagnostic build condition, cannot be activated in a normal Release build, is clearly marked as diagnostic and unfinished, and is excluded from the Google Play Release AAB. The existing `DiagnosticsEnabled`-gated lexical-log actions in Settings are the current example of this pattern.
- Debug-only visual diagnostics (layout outlines, element borders, bounding boxes, diagnostic overlays, developer badges, or similar visual markers) must not appear in a Release build or Google Play AAB.
- Under this policy, Support KnownFirst and Report a bug took the explicit-removal path in Milestone 14A: they are absent from the production Settings source rather than implemented. See [ROADMAP.md](ROADMAP.md) for the milestone record.
- Milestone 14B adds a **finished** production control, not a placeholder: the Settings → Help & Support link and the `/release-notes` page are implemented in all intended builds (Debug, BetaDiagnostic, Release) and produce a real implemented outcome. This work was committed (`940f54d59697b4d5744355634f6ae52b6cb40692`) on branch `feature/milestone14b-release-note-history-v1` and manually merged via PR #73 (merge commit `14138ccdab1e9b09a12ded002ff198d9b7312fcf`); `POST_MERGE_SYNC_ONLY` completed successfully.
- Source-contract evidence and rendered evidence are distinct. Source or markup inspection establishes that an unfinished control is absent from the component source; it does not by itself prove absence from a rendered Release build or from a Google Play AAB. The mandatory pre-AAB validation gate in [BUILD_AND_RELEASE.md](BUILD_AND_RELEASE.md) is still required before any package- or AAB-level absence claim is made.

This document does not claim public-release readiness or draw legal conclusions about license/attribution compliance; those remain open review items tracked in [ROADMAP.md](ROADMAP.md).

## Active development

The most recent recorded product-relevant milestone on `master` is `14138ccdab1e9b09a12ded002ff198d9b7312fcf` (PR #73, Milestone 14B merged), carrying source version `1.0.0-beta.12` (build 12). This is historical milestone evidence, not a claim about the literal current `master` HEAD. `DatabaseSchema.CurrentVersion` is **10** and Schema 10 is active for real application databases on master.

D1-D5 documentation reconciliation is complete. Package A, Package B, and Package C are all merged and present on `master`. Milestone 14A (removal of the unfinished Support KnownFirst and Report a bug controls and their placeholder behavior from the production Settings source) was manually merged via PR #71 (merge commit `39609ffffb39c69238882172d153f4bb795ddab8`) and `POST_MERGE_SYNC_ONLY` completed successfully; its evidence is source-contract only. Milestone 14B (reopenable release-note history) was manually merged via PR #73 (merge commit `14138ccdab1e9b09a12ded002ff198d9b7312fcf`) and `POST_MERGE_SYNC_ONLY` completed successfully. Milestone 14 as a whole is therefore complete on `master`.

**KF-BACKUP-005A — Schema-10 Stable Learning-Workflow Identity Foundation (merged via PR #79, merge commit `e56b8bfa27dfe1d630fbacfed24e6d56ea876026`).** Advances `DatabaseSchema.CurrentVersion` to **10** on `master`. Adds immutable `StableId` columns to `LearningSessions` and `LearningSessionCards`. Completed legacy sessions receive deterministic SHA-256 64-char StableIds; Active legacy sessions receive fresh GUID 32-char StableIds once on migration. V2 DTOs evolve with trailing nullable StableId properties for legacy compatibility. Source ≤9 Completed workflows receive bootstrap StableIds on import; source ≤9 Active workflows remain unsupported/rejected; source ≥10 workflows require valid canonical StableIds. KF-BACKUP-004 `LearningSessionId` exclusion from `LearningReview` merge identity is preserved. Active portable workflow continuation is excluded from 005A and deferred to KF-BACKUP-005B and 005C. Canonical candidate `ValidateAll` passed FULL GREEN on checkpoint `551399df22131e0214e87b43a3eeaea9ae40ddf9` (1812/1812 tests, Windows Debug/Release passed, Android Debug/Release passed, 0 build errors, 0 AOT/trimming/source-gen warnings, 8 non-blocking XML-doc warnings). `POST_MERGE_SYNC_ONLY` completed successfully; the package is part of this `master` snapshot.

**KF-BACKUP-005B — Portable Active Learning-Workflow Restore Into Empty Target (historical merged capability via PR #81).** For a Schema-10 source, ordinary portable export transports one Active learning workflow, its persisted queue, and committed mid-session `LearningReview` history. Empty-target restore preserves workflow and queue `StableId` values, remaps review references to the new local integer session ID, retains already-completed queue ratings and remaining ordered work, and resumes through `LearningService` from durable committed state without fabricating completion. Completed Schema-10 workflow portability remains supported and regression-tested. Schema-8/9 export remains Completed-only; source ≤9 Active workflows, Active `VocabularyReview`, and Active `PreparationBatch` remain unsupported. Its former populated-target guard is a historical 005B package boundary; current populated-target behavior is governed by KF-BACKUP-005C. Schema 10 and archive V2 are unchanged, and the transported identities remain intended for future synchronization reuse without implementing any network/cloud sync. Final focused `TEST_ONLY`: 135/0/0. Earlier supplementary test-project evidence: 1820/0/0 before final acceptance-test additions. PR #81 merged feature commit `e8236bba3d23e942014e6979b661e0c77a2a3bdd` through merge commit `dc56e8412966ac32531c4b0358526582702d6d24`; `POST_MERGE_SYNC_ONLY` completed.

**KF-BACKUP-005C — Populated-Target Active Learning-Workflow Convergence and Conflict Safety (binding master capability via PR #83).** A populated learning-quiescent Schema-10 target can additively merge a Schema-10/V2 Active workflow through the established routing, validated safety-copy, writer, and scheduler-replay contracts; workflow and queue StableIds survive and committed reviews are remapped to the new target-local integer session ID. Exact same-Active-`StableId` durable equivalence yields `NoChanges` with no safety copy, writer, or replay. Any divergent same-`StableId` Active state, including LearningReview multiplicity mismatch, is `RequiresUserDecision`, non-executable, and zero-mutation. The existing KF-BACKUP-004 semantic review identity is unchanged; `LearningSessionId` remains excluded from identity, while review multiplicity is significant for exact Active-workflow equivalence. The Active-aware target capture is preflight-only; existing safety-copy and writer stale-plan safeguards remain unchanged and fail closed. Schema 10 and archive V2 remain unchanged; no Schema 11, archive V3, DTO redesign, public status/error code, UI, synchronization transport, or new merge engine exists; `MergeWriterService` remains unchanged. Evidence: `Schema10ActiveArchive` **8 passed / 0 failed / 0 skipped**; controlled affected scope **254 passed / 0 failed / 0 skipped** with Workers=1 and **254 passed / 0 failed / 0 skipped** with Workers=8; final relevant reviews **0 BLOCKER / 0 MAJOR / 0 MINOR**. Two unexplained historical safety-copy-count observations were non-reproducible and are not passing runs or product-code fixes. This is not ALL_AUTOMATED, ValidateAll, GitHub CI, platform/runtime, Release-build, rendered-GUI, device/emulator, APK/AAB, signing, publishing, or distribution evidence. PR #83 merged at `bed54d01624e80ca6dd5adf8af097e64fe33e588`; `POST_MERGE_SYNC_ONLY` completed successfully.

**`LegacyReviewSummaries` Canonical Ordering Correction (merged via PR #85, merge commit `8eeaea58d87f9cfeb28cc4fc2520e5b277bb2526`).** `BackupModelMapperV2` now orders each vocabulary item's V2 `LegacyReviewSummaries` by typed ascending content: `ReviewCount`, `ForgotCount`, `PartialCount`, `KnownCount`, nullable `LastReviewedAt` presence (null first), then normalized UTC ticks for a present timestamp. `LastReviewedAt = null` is now explicitly distinct from present UTC `DateTime.MinValue`; local `ReviewStateEntity.Id` is not ordering material; multiplicity is preserved; exact byte-identical emitted summaries may tie only because their permutation is byte- and semantics-equivalent. A valid database can contain multiple `ReviewState` rows for one word; the old composite string key collapsed null and present UTC `DateTime.MinValue` onto the same ordering value, meaning logically equivalent installations could previously emit different `LegacyReviewSummaries` sequences, changing the serialized V2 `data.json` bytes and consequently the manifest checksum. This is non-canonical ordering of logically equivalent content, not archive corruption or semantic data loss. Historical V1 mapper/writer behavior, V1 reader compatibility, existing V1 archives, and v1-to-v2 upgrade remain unchanged. Database Schema 10 and outer `.kfarchive` V2 are unchanged. No DTO, migration, merge identity, classification, public error/status code, UI, network transport, or merge-engine change was introduced. Focused TDD RED **1 failed / 0 passed** → GREEN **1 passed / 0 failed / 0 skipped**; independent implementation review **0 BLOCKER / 0 MAJOR / 0 MINOR / 0 NIT**; bounded affected/regression TEST_ONLY **110 passed / 0 failed / 0 skipped**; `git diff --check` passed. This is automated unit/integration/contract evidence only — not ALL_AUTOMATED, ValidateAll, GitHub CI, platform/runtime, Release-build, rendered-GUI, device/emulator, APK/AAB, signing, publishing, or distribution evidence. `POST_MERGE_SYNC_ONLY` completed successfully. Its former residual wording was historical; PR #89 subsequently merged the distinct Occurrence action-key correction and completed Priority 15.

See [CURRENT_WORK.md](CURRENT_WORK.md) for the active task state.
