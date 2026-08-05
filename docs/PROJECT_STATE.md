# KnownFirst project state

**Status date:** 2026-08-05
**State source:** `master` (`a9c42f569b50831406ebd780b5c1c47376d4f5df`, PR #53 merge commit)
**Next repository action:** Review and complete the D2 Agent Communication and Operation Governance package; no product implementation is currently active.

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
- **D1 authoritative documentation reconciliation (PR #53).**

**Current Status (master):**
- The active database schema is **9** (`PRAGMA user_version = 9`).
- Schema 9 is active during normal application initialization on master.
- PR #53 is the most recent merge. Package B writer evidence is still pending; Package C convergence hardening is future work.
- No product implementation or release package is currently active. D1 is complete, and D2 is the active documentation package. D3–D5 remain future documentation work. The current work package is authoritative documentation reconciliation (see [CURRENT_WORK.md](CURRENT_WORK.md)).

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
- "Support KnownFirst" and "Report a bug" in Settings are currently nonfunctional planned features. Both controls presently render unconditionally, including in Release, bound to a shared placeholder handler (`ShowFeaturePlaceholder`); this is a recorded release blocker (see [ROADMAP.md](ROADMAP.md)), not an acceptable permanent state.
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
- Under this policy, Support KnownFirst and Report a bug must be implemented or removed from Release rendering before the next AAB; see the work-package sequence in [ROADMAP.md](ROADMAP.md) (P2-P4).

This document does not claim public-release readiness or draw legal conclusions about license/attribution compliance; those remain open review items tracked in [ROADMAP.md](ROADMAP.md).

## Active development

The stable master baseline is `a9c42f569b50831406ebd780b5c1c47376d4f5df` (PR #53 merged), carrying source version `1.0.0-beta.12` (build 12). `DatabaseSchema.CurrentVersion` is **9** and Schema 9 is active for real application databases on master.

No product implementation is currently active. The current work package is the D2 Agent Communication and Operation Governance package described in [CURRENT_WORK.md](CURRENT_WORK.md) and [ROADMAP.md](ROADMAP.md).

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

**Schema-9 Completed-Review Convergence (merged PRs #51-#52)** — Schema-9 review-session history storage activated (PR #51); Package A Schema-9 completed-review identity, planner, target-index parity, and characterization coverage merged (PR #52). Package B (writer evidence) is paused. Package C (convergence hardening) is future work.
