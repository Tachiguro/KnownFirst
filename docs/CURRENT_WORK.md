# KnownFirst Current Work

## Last updated

2026-07-27

## Repository

- Repository: https://github.com/Tachiguro/KnownFirst.git
- Only local project folder: C:\Dev\KnownFirst (see [ADR-0007](decisions/ADR-0007-single-canonical-working-directory.md))
- Active rule: use the single folder; create no worktree without explicit user approval.
- Only one writing agent may operate at a time.

## Stable baseline

- Stable master baseline: `7076aa4ac0617bd55ff97821ea69dc6b0e1228b0` (PR #22 merge commit)
- App version on master: `1.0.0-beta.11` (build 11) — master already contains Russian UI/translation-target support (PR #20), the learning repeat/direction clarity fix (PR #21), and the Beta 11 identity bump and What's New content (PR #22)
- Database schema: SQLite `PRAGMA user_version` 7 (unchanged)
- Supported platforms: Android (Google Play Internal Testing) and Windows development/verification. iOS and Mac Catalyst remain removed.
- Solution: KnownFirst.slnx

## Current branch

- Branch: `hotfix/beta-12-russian-translation`
- Base: `7076aa4ac0617bd55ff97821ea69dc6b0e1228b0`
- HEAD (committed): `c0ff28ecb189f5496182f3489af5de721d97add7` ("fix: enable Russian translation targets in Beta 12")
- PR #23 is open against this branch and must not be merged as part of this task.
- Uncommitted on top of HEAD: the four Beta 12 Android smoke-test corrections below (export verification, import confirmation layout, workflow-state refresh, online dictionary activation). Not staged or committed.

## Completed recently (this branch)

- **Root cause fixed (KF-LANG-001):** `TextReviewService.ValidateImport` incorrectly re-validated `ExplanationLanguage`/`TargetLanguage` against a local English/German-only set, rejecting valid German-to-Russian and English-to-Russian imports before the lexical lookup started, even though `LexicalLookupLanguagePolicy` already supported Russian as a translation target. The duplicated, incorrect check was removed; `LexicalLookupLanguagePolicy.Validate` is now the sole authority.
- Removed the combined Definition-and-Translation choice from the Import Text selector; only Definition and Translation remain user-selectable. The compatibility `ImportTextRequest` and `LexicalLookupRequest` constructors now produce `Translation` (not `DefinitionAndTranslation`) when source and explanation languages differ. New imports that explicitly request `DefinitionAndTranslation` are rejected at the `TextReviewService.ValidateImport` boundary.
- `DefinitionAndTranslation` remains a valid enum member for reading/processing existing database rows, preparation state, and portable archives; `BackupLexicalLookupMode` numeric/string mappings are unchanged.
- Product/display version raised to `1.0.0-beta.12`; Android build/version code raised to `12`. Package ID, database schema (`7`), signing configuration, and portable archive format are unchanged.
- Added a localized Beta 12 What's New catalog entry (English, German, Russian) covering the Russian-translation-target fix, the simplified Definition/Translation import choice, and the continued absence of Russian source-text analysis.

### Uncommitted Android smoke-test corrections (this session)

- **Root cause fixed (KF-EXPORT-001):** `AndroidPortableArchiveFileService.ExportAsync` verified the saved destination with `verifyStream.Length`, which non-seekable Android `ContentResolver` streams can throw `NotSupportedException` on even after a fully successful write. This was a verification defect, not archive corruption. Replaced with `PortableArchiveExportGuard.VerifySavedArchiveAsync`, which opens the destination and reads a single byte without touching `Length`/`Position`; works on seekable and non-seekable streams alike. Archive creation, checksums, validation, and import checks are unchanged.
- **KF-UX-001 implemented:** `Settings.razor` now hides the Data Export/Data Import action row entirely while the portable-import confirmation panel is visible, and restores it on Cancel, on validation failure, or after the import completes (success or failure).
- **KF-STATE-001 implemented:** added `IWorkflowChangeNotifier`/`WorkflowChangeNotifier` (registered as a singleton in `MauiProgram.cs`). `NavMenu` and `Home` subscribe in `OnInitializedAsync`, reload `WorkflowState`/statistics and call `StateHasChanged` on notification, and unsubscribe via `IDisposable`. `Settings.razor` publishes only after a successfully completed portable import (`PortableImportStatus.Success`) and after a successful full data reset; cancelled or failed operations never publish.
- **KF-CONSENT-001 implemented:** Settings now shows the binding online-lookup disclosure (`Prepare_OnlineDisclosureTitle`/`Prepare_OnlineDisclosure`) and an explicit "Activate online dictionary" action when `IAppSettingsService.HasOnlineLookupConsent` is false; activation calls `GrantOnlineLookupConsent` directly. The existing revoke action remains available after activation. Portable archives still never capture or restore online-lookup consent or preferences (`BackupSnapshotRepository`/`BackupImportRepository` never reference `Preferences.Default` or consent). Restoring or resetting does not grant consent; users must activate it independently.

## Completed on master (prior branches, already merged)

- Russian application-interface localization (PR #20): `Resources/Localization/SharedResource.ru.resx` with full key parity against English and German; explicit persisted System language preference that re-resolves the device culture on every start; Russian accepted as a translation target for English/German source texts; Russian explicitly rejected as a source language.
- Learning repeat/direction clarity (PR #21, [KF-LEARN-001](BACKLOG.md)): the Learn card view exposes card direction and `IsAgainRepeat`; `Learn.razor` renders a secondary direction label and a "Repeat" badge so a legitimate Again-repeat or opposite-direction card is no longer visually indistinguishable from a first-time card.
- Beta 11 release-candidate identity and What's New content (PR #22): version/build raised to `1.0.0-beta.11`/`11`; localized Beta 11 What's New entry added.

## Explicitly deferred (not in this branch)

- Russian **source**-text import, Cyrillic tokenization/normalization, Russian Wiktionary language-section parsing, and Russian Wikipedia fallback remain out of scope. Only Russian-as-target is implemented.

## Current known risks

- "Support KnownFirst" and "Report a bug" in Settings are placeholders (identical no-op handler); not gated out of Release builds.
- Deterministic GUI automation (Appium/UiAutomator2 or equivalent) is not yet implemented; GUI verification remains manual per [GUI_TEST_MATRIX.md](GUI_TEST_MATRIX.md).
- Public Google Play release is intentionally blocked; current distribution is Internal Testing only. Beta 12 is intended for Internal Testing, including testing by the user's father, once validated.
- Native-speaker wording review of the Russian resource (including the new Beta 11 and Beta 12 bullets) has not been performed; translations are complete but unreviewed by a native speaker.

## Active planned sequence

1. Validate (tests, builds) the Beta 12 hotfix branch `hotfix/beta-12-russian-translation`, then commit/push/PR once approved (no AAB yet).
2. Functional "Support KnownFirst" and "Report a bug" controls (or explicit removal/gating decision).
3. Reopenable release notes / release-note history access from Settings.
4. Deterministic Android GUI automation feasibility and first implementation.
5. Public-release legal, privacy, and support readiness review.
6. Russian source-text support decision (separately planned; not started).

## Explicit exclusions

- No merge-import or overwrite semantics for portable recovery.
- No `ReplaceAll`-style restore into a populated installation.
- No Russian source-text import yet.
- No public Google Play promotion yet.
- No AAB, signed package, or Play upload created in this branch.
- No database migration and no schema change; schema remains 7.

## Relevant files for the next task

- [BACKLOG.md](BACKLOG.md)
- `Services/DataSafety/PortableArchiveExportGuard.cs`
- `Platforms/Android/AndroidPortableArchiveFileService.cs`
- `Components/Pages/Settings.razor`
- `Services/Study/IWorkflowChangeNotifier.cs`, `Services/Study/WorkflowChangeNotifier.cs`
- `Components/Layout/NavMenu.razor`, `Components/Pages/Home.razor`
- `MauiProgram.cs`
- `Resources/Localization/SharedResource*.resx`
- `KnownFirst.Tests/PortableArchiveExportGuardTests.cs`
- `KnownFirst.Tests/WorkflowChangeNotifierTests.cs`
- `KnownFirst.Tests/UiWorkflowContractTests.cs`
- `KnownFirst.Tests/LocalizationResourceTests.cs`

## Next exact action

Run local validation (`scripts\knownfirst.ps1`, or the targeted `dotnet test` filters in this task's report) on the uncommitted Android smoke-test corrections (export verification, import confirmation layout, workflow-state refresh, online dictionary activation); everything remains unstaged and uncommitted pending explicit authorization to commit/push. PR #23 stays open and unmerged. Once validated, continue with planned sequence item 2 (Support/Report-a-bug controls).

## Design work (not implemented)

- **KF-BACKUP-002** (P1, blocks public release readiness, does not block Beta 12 Internal Testing): non-destructive portable archive merge design, revision 2, recorded in [architecture/backup-merge-v1-design.md](architecture/backup-merge-v1-design.md) and [BACKLOG.md](BACKLOG.md). Revision 2 corrected source-material/Meaning/LearningCard identity against verified code, added a mandatory pre-merge safety copy, an explicit clock-skew/event-fingerprint policy, and idempotency proofs; four open decisions remain for product/engineering sign-off (design doc §13).
  - **KF-BACKUP-002 Slice 1 — pure merge contracts** (branch `feature/backup-merge-contracts-v1`, uncommitted): implemented design §12 slice 1 — a database-independent library under `Services/DataSafety/Merge/` (`KnownFirst.Services.DataSafety.Merge` namespace) providing the canonical SHA-256 byte-encoding contract, every §4 stable-identity function (SourceMaterial, Vocabulary, Meaning, LearningCard matching, ReviewSession/Candidate, PreparationSession/Candidate, LearningSession/Card), the §6 LearningReview event fingerprint, and every §5 conflict-resolution matrix (KnowledgeState, PreparationState, LearningCard-state classification, workflow-session status), plus pure fixture/set utilities used to prove the §11 idempotency/convergence scenarios. No SQLite access, no service orchestration, no import writer, no UI — see design doc's new "Product contract" section for the resulting single-Import-action behavior this and later slices implement. Tests added under `KnownFirst.Tests/Merge*Tests.cs`.
  - **KF-BACKUP-002 Slice 2 — validated merge safety copies** (branch `feature/backup-merge-safety-copy-v1`, uncommitted): implemented design §12 slice 2 — `MergeSafetyCopyService` (`Services/DataSafety/Merge/MergeSafetyCopyService.cs`) creates a private, validated pre-merge recovery archive. A new `BackupSnapshotRepository.CapturePortableSnapshotForMergeSafetyCopy` performs the active-workflow check (Review/Preparation/Learning) and the portable snapshot capture inside one `ExecuteSnapshotAsync` transaction, so a safety copy can never omit an active workflow to a time-of-check/time-of-use gap; Slice 2 guarantees only that the safety-copy snapshot itself was captured with no active workflow — a future merge writer must re-check the condition again immediately before mutation. Storage root is derived solely from `IKnownFirstDatabase.DatabasePath`'s parent directory (`<database-directory>/merge-safety-copies/`), never from `FileSystem.AppDataDirectory`/Documents/Downloads, so a GUI-test-profile database keeps its safety copies inside that isolated profile automatically. Archive creation reuses the existing `BackupSnapshotRepository`/`BackupModelMapper`/`BackupArchiveWriter`/`BackupArchiveReader.ValidateAsync` pipeline unchanged (no second format); the archive is staged, validated, moved to a unique final `.kfarchive` path, and re-validated from that final path before a private `.metadata.json` sidecar (source-generated JSON) is staged, finalized, and re-read-validated — only after both are valid does an older recognized safety-copy pair get removed (best-effort, never for unrecognized files). Failure/cancellation always retains the previously valid pair and cleans up only the current attempt, returning `BlockedByActiveWorkflow`/`Cancelled`/`SafetyCopyFailed` with the existing stable codes (`active-workflow-unsupported`/`operation-cancelled`/`safety-backup-failed`). No merge matcher, merge writer, Import routing, UI, schema, or archive-format change. Tests added in `KnownFirst.Tests/MergeSafetyCopyServiceTests.cs`.
  - **KF-BACKUP-002 Slice 3 — read-only merge preflight** (branch `feature/backup-merge-preflight-v1`, uncommitted): implemented design §12 slice 3 — a pure `MergePreflightPlanner` (`Services/DataSafety/Merge/MergePreflightPlanner.cs`) computes the complete, deterministic merge preflight plan from a target and an archive `BackupPayload` plus the archive's `BackupManifest`, using only Slice 1's stable-identity/conflict policies (no SQLite, filesystem, network, environment, current-time, random, Preferences, or MAUI dependency, no write transaction). A read-only `MergePreflightService` (`Services/DataSafety/Merge/MergePreflightService.cs`) validates the archive via the existing `BackupArchiveReader`, captures the target with the same race-free `BackupSnapshotRepository.CapturePortableSnapshotForMergeSafetyCopy` call Slice 2 already uses (fail-closed active-workflow classification reused unchanged), maps the snapshot through the existing `BackupModelMapper`, and invokes the planner — it creates no safety copy and opens no write transaction. The plan (`Services/DataSafety/Merge/MergePreflightModels.cs`) exposes seven statuses (`Ready`/`NoChanges`/`RequiresUserDecision`/`BlockedByActiveWorkflow`/`ValidationFailed`/`Cancelled`/`Failed`), per-entity-kind counts across six classifications (`New`/`ExactDuplicateSkipped`/`Enriched`/`PreservedVariant`/`UnresolvedConflict`/`DeduplicatedEvent`) for all 16 portable entity kinds, exact itemized plan actions keyed only by stable identities and archive-local ids (never target-local numeric ids), same-tier `KnowledgeState` conflict decisions, required `PreferredMeaningConflict` decisions (target/archive Meaning summaries plus `KeepTargetMeaning`/`UseArchiveMeaning`/`KeepBothSelectTarget`/`KeepBothSelectArchive` choices, never auto-applied), a `RequiresSchedulerReplay` flag, sample details bounded to 20 per classification, and stable warning/error codes. Tests added in `KnownFirst.Tests/MergePreflightPlannerTests.cs` and `KnownFirst.Tests/MergePreflightServiceTests.cs`; `KnownFirst.Tests/MergePreflightFixtures.cs` holds the shared fixture builders. No safety-copy creation, SQLite mutation, scheduler replay, merge writer, Import routing, UI, or archive-format change.
    - **Revised** (same branch) for the approved meaning-centric model (design doc §16, backlog [KF-MEANING-001](BACKLOG.md)): added `SemanticMeaningIdentity`/`ExactMeaningVariantIdentity`/`AnswerVariantIdentity`/`FutureCardIdentity` (`Services/DataSafety/Merge/SemanticMeaningIdentities.cs`); the planner now classifies `PreparedMeaning` by SemanticMeaning/exact-variant identity instead of the old row-level `MeaningIdentity`, and gives SentenceRange/Occurrence/EncounteredForm/LegacyReviewSummary/ContextSnapshot their own stable-parent-based identities (no more parent-lockstep classification). Tests added/revised in `KnownFirst.Tests/MergeSemanticMeaningIdentityTests.cs` and extended `MergePreflightPlannerTests.cs`.
    - **Focused correction pass** (same branch, design doc §17): `SemanticMeaningIdentity` no longer hashes `Translation` (it was incorrectly splitting synonyms into separate meanings); `ExactMeaningVariantIdentity` gained the `Translation` field it was missing entirely; the planner now actually matches `LearningCardEntity` rows by `FutureCardIdentity` (not `(VocabularyIdentity, Direction)`), with the physical identity retained only to detect a same-slot collision between two distinct SemanticMeanings, which is now a plain `New` card (never `PreservedVariant`) plus the new blocking prerequisites `meaning-card-schema-migration-required` and `archive-format-migration-required` (the latter verified directly against `BackupArchiveWriter.ValidatePayloadGraph`'s card-uniqueness check). `PreferredVariantSelectionDecision` is now blocking. `AnswerVariant` was removed as a `MergeEntityKind`; answer-variant plans are now exposed separately via `MergePreflightPlan.DerivedAnswerVariantPlans` and never affect primary per-entity counts. Added a blocking `SemanticMeaningGroupingDecision` for ambiguous same-Word/no-discriminator/differing-Translation cases. Added a new `BlockedByPrerequisite` status and `IsExecutable`/`BlockingPrerequisites` fields on the plan. `VocabularyReviewItem`/`PreparationItem`/`LearningQueueItem` now compare full historical content (preserving divergent history as an additional row) instead of collapsing on identity alone; `VocabularyReviewWorkflow` content divergence for the same document is a new blocking `workflow-history-schema-migration-required` case. The review-event fingerprint used inside the planner is now meaning-aware (`FutureCardIdentity`-keyed), distinct from Slice 1's unchanged persisted `LearningReviewFingerprintPolicy`.
    - **Final focused review correction** (same branch, design doc §18): `SemanticMeaningIdentity`/`ExactMeaningVariantIdentity` bumped to `.v3` — `Definition` had the identical defect §17 already fixed for `Translation`, one field over: it was still hashed unconditionally into `SemanticMeaningIdentity` and treated as a reliable discriminator on its own, so a same-provider-sense-id pair with merely differently-worded definitions was silently misclassified as two distinct SemanticMeanings, and a no-discriminator pair with differing Definition wording was silently auto-split instead of raising a `SemanticMeaningGroupingDecision`. `Definition` is now excluded from `SemanticMeaningIdentity`/`HasReliableSenseDiscriminator` (symmetric with `Translation`) and hashed directly into `ExactMeaningVariantIdentity`. Fixed the four affected `MergePreflightPlannerTests`/`MergeSemanticMeaningIdentityTests` fixtures that had relied on the old (incorrect) "Definition presence alone is reliable" behavior, and added coverage for same-provider-sense-id/differing-Definition, differing-provider-sense-id, no-discriminator/differing-Definition, matching-Definition-without-a-real-discriminator, and alias-reordering-never-creates-a-preferred-variant-decision. 8 tests added net (1003 → 1011 complete-suite total; 337/337 focused Slice 1–3 merge/portable-archive tests). Also corrected this document's stale top-level "Not implemented" status line (Slices 1–2 are merged; Slice 3 is implemented only on this unmerged branch).

## New-chat handoff

"Read AGENTS.md, docs/PROMPT_AND_TASK_ROUTING.md, docs/CURRENT_WORK.md, and docs/INDEX.md completely. Verify branch, HEAD, Git status, and registered worktrees. Follow the task-specific reading route from docs/INDEX.md. Then perform only the task under 'Next exact action'."
