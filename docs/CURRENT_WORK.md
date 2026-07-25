# KnownFirst Current Work

## Last updated

2026-07-25

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

- Branch: `hotfix/beta-12-russian-translation` (Russian-translation-target hotfix and combined-lookup removal, uncommitted)
- Base: `7076aa4ac0617bd55ff97821ea69dc6b0e1228b0`

## Completed recently (this branch)

- **Root cause fixed (KF-LANG-001):** `TextReviewService.ValidateImport` incorrectly re-validated `ExplanationLanguage`/`TargetLanguage` against a local English/German-only set, rejecting valid German-to-Russian and English-to-Russian imports before the lexical lookup started, even though `LexicalLookupLanguagePolicy` already supported Russian as a translation target. The duplicated, incorrect check was removed; `LexicalLookupLanguagePolicy.Validate` is now the sole authority.
- Removed the combined Definition-and-Translation choice from the Import Text selector; only Definition and Translation remain user-selectable. The compatibility `ImportTextRequest` and `LexicalLookupRequest` constructors now produce `Translation` (not `DefinitionAndTranslation`) when source and explanation languages differ. New imports that explicitly request `DefinitionAndTranslation` are rejected at the `TextReviewService.ValidateImport` boundary.
- `DefinitionAndTranslation` remains a valid enum member for reading/processing existing database rows, preparation state, and portable archives; `BackupLexicalLookupMode` numeric/string mappings are unchanged.
- Product/display version raised to `1.0.0-beta.12`; Android build/version code raised to `12`. Package ID, database schema (`7`), signing configuration, and portable archive format are unchanged.
- Added a localized Beta 12 What's New catalog entry (English, German, Russian) covering the Russian-translation-target fix, the simplified Definition/Translation import choice, and the continued absence of Russian source-text analysis.

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
- `Services/TextReviewService.cs`
- `KnownFirst.Core/Preparation/LexicalModels.cs`
- `Models/TextReviewModels.cs`
- `Components/Pages/ImportText.razor`
- `KnownFirst.csproj`
- `Services/ReleaseNotesService.cs`
- `Resources/Localization/SharedResource*.resx`
- `KnownFirst.Tests/TextReviewServiceTests.cs`
- `KnownFirst.Tests/ReleaseNotesTests.cs`

## Next exact action

Run local validation (`scripts\knownfirst.ps1`) on the uncommitted `hotfix/beta-12-russian-translation` changes; once tests and builds pass, decide whether to commit/push/open a PR, then continue with planned sequence item 2 (Support/Report-a-bug controls).

## New-chat handoff

"Read AGENTS.md, docs/PROMPT_AND_TASK_ROUTING.md, docs/CURRENT_WORK.md, and docs/INDEX.md completely. Verify branch, HEAD, Git status, and registered worktrees. Follow the task-specific reading route from docs/INDEX.md. Then perform only the task under 'Next exact action'."
