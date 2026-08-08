# KnownFirst project state

**Status date:** 2026-08-08
**State source:** most recent product-relevant milestone — `14138ccdab1e9b09a12ded002ff198d9b7312fcf` (PR #73 merge commit, Milestone 14B). This is historical milestone evidence, not the literal current `master` HEAD; the exact current `master` HEAD and PR state are live GitHub/Git facts discovered dynamically per [docs/NEW_CHAT_BOOTSTRAP.md](NEW_CHAT_BOOTSTRAP.md).
**Next repository action:** None automatically authorized; Milestone 14 is complete and awaits explicit user direction on the next work package. Milestone 14A (explicit removal of the unfinished Support KnownFirst and Report a bug controls and their placeholder behavior) passed final PR review and was merged via PR #71 (merge commit `39609ffffb39c69238882172d153f4bb795ddab8`) — `master` now carries the Milestone 14A change, and `POST_MERGE_SYNC_ONLY` completed successfully. Milestone 14B (reopenable release-note history) passed final PR review and was manually merged via PR #73 (merge commit `14138ccdab1e9b09a12ded002ff198d9b7312fcf`); `POST_MERGE_SYNC_ONLY` completed successfully. Milestone 14 is therefore complete. Package C (convergence hardening) was previously implemented, independently reviewed and corrected, `TEST_ONLY`-validated (`ALL_AUTOMATED` 1776/0/0), and merged via PR #68 (merge commit `db47de3bf48b49b5258ce16acc6e3e543d96143c`).

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

**Current Status (master):**
- The active database schema is **9** (`PRAGMA user_version = 9`).
- Schema 9 is active during normal application initialization on master.
- Package C was merged via PR #68. D1-D5 documentation reconciliation is complete.
- Package A, Package B, and Package C are merged to master. D1 through D5 are complete (see [CURRENT_WORK.md](CURRENT_WORK.md) and [ROADMAP.md](ROADMAP.md)).
- Package C (convergence hardening) is implemented, independently reviewed and corrected, `TEST_ONLY`-validated, passed final PR review, and merged via PR #68 (merge commit `db47de3bf48b49b5258ce16acc6e3e543d96143c`) — see "Active development" below for its technical evidence. It is part of this `master` snapshot.
- Milestone 14A and Milestone 14B are both merged (PR #71, merge commit `39609ffffb39c69238882172d153f4bb795ddab8`; PR #73, merge commit `14138ccdab1e9b09a12ded002ff198d9b7312fcf`). Milestone 14 as a whole is complete on `master`. `POST_MERGE_SYNC_ONLY` completed successfully for both.
- KF-BACKUP-003 Package D is merged via PR #76 (merge commit `17d3f1a031b9f319041ff1034a227d17b1029c4f`) and KF-BACKUP-004 is merged via PR #77 (merge commit `bec861fb8a054beb2804f1132b450da1e45dee90`); `POST_MERGE_SYNC_ONLY` completed successfully for both, and both are part of this `master` snapshot. Remaining portable-integrity residuals continue under active development (see [ROADMAP.md](ROADMAP.md) priority 15 and [CURRENT_WORK.md](CURRENT_WORK.md)).

## Confirmed verification

### Automated

- Automated tests cover Core policies, text analysis, temporary SQLite persistence, workflow logic, localization, diagnostics, lookup providers with offline fixtures, script contract invariants, and archive contracts. Automated tests do not make live network requests.
- Test execution and status are tied to explicit commit and scope boundaries (see `docs/TESTING.md`).

### Platform builds

- **Windows / Android Debug & Release:** Build readiness verified during Beta 10, Beta 11, and Beta 12 release preparation.

## Database status

- Storage is local SQLite in the application data directory (`knownfirst.db3`).
- On `master`, `DatabaseSchema.CurrentVersion` and `PRAGMA user_version` are **9**.
- Schema 9 is active in real application databases on master.
- Initialization is forward-oriented and preserves existing rows while adding supported tables or columns.
- The initialization sequence advances fresh or legacy baseline databases to Schema 7, applies the Schema 8 migration, and then applies the Schema 9 migration.
- Initialization reads `PRAGMA user_version` first and rejects any version greater than the current version before modifying tables or cache.
- Complete persisted-data rules are in [DATABASE_CONTRACT.md](DATABASE_CONTRACT.md).
- Portable recovery format v1 is documented in [architecture/backup-format-v1.md](architecture/backup-format-v1.md).

## Known limitations

- Exported `.kfarchive` archives are not encrypted and may contain personal imported text and learning history; users are warned before export.
- "Support KnownFirst" and "Report a bug" remain unimplemented planned features. Milestone 14A removed both controls, their "coming soon" placeholder UI, and the shared placeholder state and handlers from the production Settings source (`Components/Pages/Settings.razor`), so they are no longer represented by any production control; they remain documentation-only, tracked in [ROADMAP.md](ROADMAP.md). The localization keys `Settings_SupportKnownFirst`, `Settings_ReportBug`, and `Common_FeatureComingSoon` are intentionally retained as unreferenced resource strings; a resource string is not a rendered product control. This state is established by source-contract evidence (Razor/CSS/test source inspection, `UI_CONTRACT_AUTOMATED` `70 passed / 0 failed / 0 skipped`) — it is not rendered-Release or AAB evidence.
- Cloud synchronization, accounts, analytics, advertising, and payments are not implemented.
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

The most recent recorded product-relevant milestone on `master` is `14138ccdab1e9b09a12ded002ff198d9b7312fcf` (PR #73, Milestone 14B merged), carrying source version `1.0.0-beta.12` (build 12). This is historical milestone evidence, not a claim about the literal current `master` HEAD. `DatabaseSchema.CurrentVersion` is **9** and Schema 9 is active for real application databases on master.

D1-D5 documentation reconciliation is complete. Package A, Package B, and Package C are all merged and present on `master`. Milestone 14A (removal of the unfinished Support KnownFirst and Report a bug controls and their placeholder behavior from the production Settings source) was manually merged via PR #71 (merge commit `39609ffffb39c69238882172d153f4bb795ddab8`) and `POST_MERGE_SYNC_ONLY` completed successfully; its evidence is source-contract only. Milestone 14B (reopenable release-note history) was manually merged via PR #73 (merge commit `14138ccdab1e9b09a12ded002ff198d9b7312fcf`) and `POST_MERGE_SYNC_ONLY` completed successfully. Milestone 14 as a whole is therefore complete on `master`. The current active package state is recorded in [CURRENT_WORK.md](CURRENT_WORK.md).

**Milestone 14B — reopenable release-note history (merged via PR #73, merge commit `14138ccdab1e9b09a12ded002ff198d9b7312fcf`).** Settings → Help & Support offers one production-visible link to a dedicated `/release-notes` route, which lists every existing release-note catalog entry newest-first (`1.0.0-beta.12`, `1.0.0-beta.11`, `1.0.0-beta.10`) via the new `IReleaseNotesService.GetReleaseNoteHistory()` API. History access neither reads nor mutates the persisted seen-version state, so the automatic one-time What's New semantics remain intact: `GetUnseenReleaseNotes()`, `MarkSeen()`, `WhatsNewModal`, and the preference store are unchanged. Existing Beta 10/11/12 release-note content is unchanged, no localization key was added, and no Beta 13 entry, release identity, database, schema, archive, or network behavior changed. Evidence is automated service/unit/contract plus source/markup/Razor/CSS contract only (`110 passed / 0 failed / 0 skipped`); **no rendered-GUI, runtime, platform, Release-build, AAB, or physical-device validation has occurred.** `POST_MERGE_SYNC_ONLY` completed successfully; the change is part of this `master` snapshot. Package C (convergence hardening) was implemented, independently reviewed and corrected, `TEST_ONLY`-validated, passed final PR review, and manually merged via PR #68 — it is documented below for traceability. See [CURRENT_WORK.md](CURRENT_WORK.md) and [ROADMAP.md](ROADMAP.md).

**Schema-9 Completed-Review Convergence — Package B (merged via PR #65).** Implemented, independently reviewed, validated, and merged, on the former branch `feature/schema9-completed-review-writer-evidence-v1`, published as commit `d00144cd8789f5392c9fb695dac8856f992c2200` and merged via PR #65 (`fix: complete schema 9 completed-review package B`, merge commit `f560a6b7ff9109bbee6c46602a002ea8b591de49`):

- `PLAN_ONLY` was approved by the user; `IMPLEMENT` added a deterministic total ordering for Schema-9 `ReviewSessions` in `Services/DataSafety/BackupModelMapperV2.cs` and writer-evidence/canonical-output regression tests in `KnownFirst.Tests/MergeWriterServiceTests.cs` and `KnownFirst.Tests/BackupCreationTests.cs`.
- An independent `REVIEW_ONLY` pass found one MINOR XML-comment accuracy issue; the comment-only correction was made and independently re-reviewed. Final verdict: **`PACKAGE B IMPLEMENTATION REVIEW APPROVED`**.
- `TEST_ONLY` validation: focused writer/planner/identity scope 183/0/0, mapper/archive-contract scope 86/0/0, schema activation/compatibility scope 189/0/0, and the full `ALL_AUTOMATED` suite **1769 passed / 0 failed / 0 skipped**.
- A final pre-commit complete-diff review of the published commit returned **`PACKAGE B FINAL REVIEW APPROVED`**. A review of PR #65 identified exactly one documentation-currentness finding and no code/test finding; the branch documentation addressed that finding.
- No GUI, device, build, APK/AAB, packaging, signing, publishing, or release evidence exists for this work.
- **The repository owner merged PR #65 manually; merge commit `f560a6b7ff9109bbee6c46602a002ea8b591de49` is part of this `master` snapshot. `POST_MERGE_SYNC_ONLY` completed successfully.**
- Package C (convergence hardening, two-installation synchronization) is implemented, independently reviewed and corrected, `TEST_ONLY`-validated, passed final PR review, and merged via PR #68.

**Schema-9 Completed-Review Convergence — Package C (convergence hardening) — merged via PR #68.** Implemented, independently reviewed and corrected, `TEST_ONLY`-validated, and merged via PR #68 (merge commit `db47de3bf48b49b5258ce16acc6e3e543d96143c`):

- `PLAN_ONLY` was approved, scoping two proven canonical-output defects Package B left open. **C-1 (completed `ReviewSession` ordering):** `BackupModelMapperV2` reuses the existing Schema-9 full-history identity from `ReviewWorkflowIdentityPolicy.TryComputeSessionIdentityV2` — no second, competing completed-review identity was defined. Raw-row identity plumbing is shared through a new caller-neutral `Schema9ReviewSessionRowIdentities` helper; `MergeWriterTargetIndex` retains its existing fail-closed duplicate-identity behavior unchanged. Mapper ordering now continues past the session-level fields with the full identity and a content-derived candidate-content key (covering absolute candidate `Order` and every emitted item field, which the identity itself deliberately omits); a malformed duplicate-candidate session receives a deterministic content-derived fallback key rather than a shared sentinel. The local `ReviewSession.Id` comparison remains syntactically present as a final tie-break, but is proven output-neutral: it is reached only after every field two sessions could emit differently already compares equal, so which physical row is used cannot change any emitted content. Local SQLite row ids remain outside semantic identity.
- **C-2 (SourceMaterial ordering):** the review also found that the existing `(ContentFingerprint, Title)` v2 ordering was not total over distinct emitted `SourceMaterial` rows. Ordering now compares the retained scalar `SourceMaterial` output plus a deterministic content-derived child-subgraph key covering emitted `Sentence`/`Occurrence` content, including the emitted vocabulary reference and sentence-reference semantics; no Document/SentenceSpan/WordOccurrence/Word local SQLite id participates as ordering content. An independent review found this addition itself initially covered only the scalar fields (MINOR-1); a RED-first correction closed the child-subgraph gap and an independent re-review returned **`PACKAGE C MINOR-1 CORRECTION REVIEW APPROVED`** with no BLOCKER/MAJOR/MINOR findings remaining.
- **Two-installation convergence evidence:** focused tests exercise two installations exchanging divergent completed review histories A→B and B→A through the real archive write/validate/import/merge-writer path; both histories are preserved, candidates remain attached to their correct parent sessions, and after convergence the affected canonical export subgraph (documents, vocabulary, review workflows, and items) is installation-independent. A repeated exchange afterward is no-change/idempotent and preserves every completed history. This is distinct from, and does not by itself imply, universal byte-for-byte equality of complete independently created archives — Sense/Meaning/AnswerVariant `StableId` values remain installation-random by design and are unaffected by Package C.
- No archive DTO, `.kfarchive` format version, database schema, migration, or public error/status code changed. No executable `MergeWriterExecutor` redesign was required.
- `TEST_ONLY` validation on the branch: `BackupCreationTests` 50/0/0 (including the four C-1/C-2 mapper tests), merge planner/writer/identity scope 157/0/0, archive/restore/Schema-9 compatibility scope 117/0/0, `PortableImportEndToEndConvergenceTests` 6/0/0, and the full `ALL_AUTOMATED` suite **1776 passed / 0 failed / 0 skipped**.
- No GUI, device, platform, standalone-build, packaging, signing, publishing, or release evidence exists for this work.
- **The repository owner merged PR #68 manually; merge commit `db47de3bf48b49b5258ce16acc6e3e543d96143c` is part of this `master` snapshot. `POST_MERGE_SYNC_ONLY` completed successfully.** Final PR review was `PACKAGE C PR REVIEW APPROVED` with no BLOCKER, MAJOR, or MINOR findings.

**KF-MEANING-001 Slice 9 (merged PR #45)** — portable import preview UI, localized handling, and end-to-end convergence validation. Verified behavior on the merged commit:

- **Import preview UI** — read-only preview before confirmation; distinguishes restore (empty target), merge (populated Schema-8 target), and no-change (duplicate import) cases.
- **Preview safety** — no database mutation, safety copy, or writer invocation during preview; supports non-seekable caller streams.
- **Confirmation workflow** — distinct action labels for restore or merge; no-change presents success without a mutating action; re-validates and re-evaluates independently on confirmation.
- **Unified import operation** — single Import data operation; no separate Merge button or separate merge workflow.
- **Merge preview and results** — expose aggregate inserted, enriched, preserved-variant, and skipped counts; explain that local data is preserved and a validated private safety copy is created before mutation.
- **Disposition classification** — RestoredIntoEmpty, MergeApplied, MergeNoChange; workflow notifications occur only for RestoredIntoEmpty and MergeApplied.
- **Localization** — complete EN/DE/RU coverage for preview, result, and failure handling.
- **Corrected LearningSession identity** — distinct real sessions using the same card set no longer collapse; identity includes StartedAtUtc, CompletedAtUtc, ordered queue digest, and Rating per item; planner and target-index share the same implementation; reimport converges without duplicates.
- **End-to-end convergence validation** — real automated tests exercise archive creation → validation → preview → preflight → validated safety copy → transactional writer → deterministic scheduler replay → result summary → repeated-import no-change; bidirectional divergent Schema-8 databases converge semantically.
- **Archive-v1 upgrade and convergence** — Schema-8 populated-target Import upgrades archive-v1 in memory and converges on reimport.
- **Safety-copy validation** — safety copies are reopened and validated from final paths; represent the pre-merge target state; remain available after later writer failure.

**Subsequent correctness and data-safety fixes (merged PRs #46-#50)** — see "Production capabilities" and "Merged development foundations" above.

**Schema-9 Completed-Review Convergence (merged PRs #51-#52, #65, #68)** — Schema-9 review-session history storage activated (PR #51); Package A Schema-9 completed-review identity, planner, target-index parity, and characterization coverage merged (PR #52); Package B (writer evidence) merged via PR #65. Package C (convergence hardening) is implemented, independently reviewed and corrected, `TEST_ONLY`-validated, passed final PR review, and manually merged via PR #68 (merge commit `db47de3bf48b49b5258ce16acc6e3e543d96143c`).

**D1 Authoritative State and Database Truth (merged PR #53)** — reconciled repository baseline, roadmap, and changelog context.

**D1 Closure and D2 Activation (merged PR #54)** — recorded D1 closure and activated D2 governance package.

**D2 Agent Communication and Operation Governance (merged PR #55)** — established agent communication governance and operation isolation rules.

**D2 Closure and D3 Activation (merged PR #56)** — recorded D2 closure and activated D3 backup and import contracts package.

**D3 Backup and Import Contracts (merged PR #57)** — reconciled backup-format, populated-target import/merge contracts, historical restore-plan status, and documentation routing.

**D3 Closure and D4 Activation (merged PR #58)** — recorded D3 closure and activated D4 product, workflow, and release-facing documentation package.

**D4 Product, Workflow, and Release-Facing Documentation (merged PR #59)** — reconciled `README.md`, `docs/KNOWNFIRST_ARCHITECTURE.md`, `docs/MVP_WORKFLOW.md`, `docs/VERSIONING.md`, and `docs/BETA_TESTING.md`.

**D4 Closure and D5 Activation (merged PR #60)** — recorded D4 closure and activated D5 Testing, GUI Status, Historical Banners, and Markdown Hygiene package.

**D5 Testing, GUI Status, Historical Banners, and Markdown Hygiene (merged PRs #61-#63)** — PR #61 reconciled testing and GUI contract documentation; PR #62 corrected historical status banners and routing references; PR #63 fixed mechanical Markdown hygiene defects. D1-D5 documentation reconciliation is complete.
