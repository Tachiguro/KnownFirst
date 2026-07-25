# KnownFirst Current Work

## Last updated

2026-07-25

## Repository

- Repository: https://github.com/Tachiguro/KnownFirst.git
- Only local project folder: C:\Dev\KnownFirst (see [ADR-0007](decisions/ADR-0007-single-canonical-working-directory.md))
- Active rule: use the single folder; create no worktree without explicit user approval.
- Only one writing agent may operate at a time.

## Stable baseline

- Stable master baseline: `52e74f2aa4ec0f071d99232eca1d4dde5a1d5110` (PR #21 merge commit)
- App version on master: 1.0.0-beta.10 (build 10) — master already contains the Russian UI/translation-target work (PR #20) and the learning repeat/direction clarity fix (PR #21), but the product version metadata was not yet bumped
- Database schema: SQLite `PRAGMA user_version` 7 (unchanged)
- Supported platforms: Android (Google Play Internal Testing) and Windows development/verification. iOS and Mac Catalyst remain removed.
- Solution: KnownFirst.slnx

## Current branch

- Branch: `release/beta-11-russian-internal-test` (release-candidate identity bump and What's New content, uncommitted)
- Base: `52e74f2aa4ec0f071d99232eca1d4dde5a1d5110`

## Completed recently (this branch)

- Product/display version raised to `1.0.0-beta.11`; Android build/version code raised to `11`. Package ID, database schema (`7`), signing configuration, and portable archive format are unchanged.
- Added a localized Beta 11 What's New catalog entry (English, German, Russian) covering: Russian UI added; System language now follows the device; Russian as a translation target for English/German; learning-card direction display; the Again-repeat badge; Russian source-text analysis still unsupported.
- Extended `ReleaseNotesTests.cs` and `LocalizationResourceTests`-style resource checks for the Beta 11 identity and release-note content across all three locales.

## Completed on master (prior branches, already merged)

- Russian application-interface localization (PR #20): `Resources/Localization/SharedResource.ru.resx` with full key parity against English and German; explicit persisted System language preference that re-resolves the device culture on every start; Russian accepted as a translation target for English/German source texts; Russian explicitly rejected as a source language.
- Learning repeat/direction clarity (PR #21, [KF-LEARN-001](BACKLOG.md)): the Learn card view exposes card direction and `IsAgainRepeat`; `Learn.razor` renders a secondary direction label and a "Repeat" badge so a legitimate Again-repeat or opposite-direction card is no longer visually indistinguishable from a first-time card.

## Explicitly deferred (not in this branch)

- Russian **source**-text import, Cyrillic tokenization/normalization, Russian Wiktionary language-section parsing, and Russian Wikipedia fallback remain out of scope. Only Russian-as-target is implemented.

## Current known risks

- "Support KnownFirst" and "Report a bug" in Settings are placeholders (identical no-op handler); not gated out of Release builds.
- Deterministic GUI automation (Appium/UiAutomator2 or equivalent) is not yet implemented; GUI verification remains manual per [GUI_TEST_MATRIX.md](GUI_TEST_MATRIX.md).
- Public Google Play release is intentionally blocked; current distribution is Internal Testing only. Beta 11 is intended for Internal Testing, including testing by the user's father.
- Native-speaker wording review of the Russian resource (including the new Beta 11 bullets) has not been performed; translations are complete but unreviewed by a native speaker.

## Active planned sequence

1. Validate and, once approved, commit/push/PR the Beta 11 release-candidate branch `release/beta-11-russian-internal-test` (version/build bump and What's New content only; no AAB yet).
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

## Relevant files for the next task

- [BACKLOG.md](BACKLOG.md)
- `KnownFirst.csproj`
- `Services/ReleaseNotesService.cs`
- `Resources/Localization/SharedResource*.resx`
- `KnownFirst.Tests/ReleaseNotesTests.cs`

## Next exact action

Review the uncommitted `release/beta-11-russian-internal-test` identity bump and What's New content, decide whether to commit/push/open a PR, and continue with planned sequence item 2 (Support/Report-a-bug controls).

## New-chat handoff

"Read AGENTS.md, docs/PROMPT_AND_TASK_ROUTING.md, docs/CURRENT_WORK.md, and docs/INDEX.md completely. Verify branch, HEAD, Git status, and registered worktrees. Follow the task-specific reading route from docs/INDEX.md. Then perform only the task under 'Next exact action'."
