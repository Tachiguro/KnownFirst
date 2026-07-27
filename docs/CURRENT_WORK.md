# KnownFirst Current Work

## Last updated

2026-07-26

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

- **KF-BACKUP-002** (P1, blocks public release readiness, does not block Beta 12 Internal Testing): non-destructive portable archive merge design, revision 2, recorded in [architecture/backup-merge-v1-design.md](architecture/backup-merge-v1-design.md) and [BACKLOG.md](BACKLOG.md). No code, schema, or archive-format change was made. Revision 2 corrected source-material/Meaning/LearningCard identity against verified code, added a mandatory pre-merge safety copy, an explicit clock-skew/event-fingerprint policy, and idempotency proofs; four open decisions remain for product/engineering sign-off (design doc §13).

## New-chat handoff

"Read AGENTS.md, docs/PROMPT_AND_TASK_ROUTING.md, docs/CURRENT_WORK.md, and docs/INDEX.md completely. Verify branch, HEAD, Git status, and registered worktrees. Follow the task-specific reading route from docs/INDEX.md. Then perform only the task under 'Next exact action'."
