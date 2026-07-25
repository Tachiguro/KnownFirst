# KnownFirst Current Work

## Last updated

2026-07-25

## Repository

- Repository: https://github.com/Tachiguro/KnownFirst.git
- Only local project folder: C:\Dev\KnownFirst (see [ADR-0007](decisions/ADR-0007-single-canonical-working-directory.md))
- Active rule: use the single folder; create no worktree without explicit user approval.
- Only one writing agent may operate at a time.

## Stable baseline

- Stable master baseline: `f1d1c3047240ded2bdaae8eb026741fe140a6da3` (PR #19 merge commit)
- App version: 1.0.0-beta.10 (build 10) — unchanged by this feature branch
- Database schema: SQLite `PRAGMA user_version` 7 (unchanged)
- Supported platforms: Android (Google Play Internal Testing) and Windows development/verification. iOS and Mac Catalyst remain removed.
- Solution: KnownFirst.slnx

## Current branch

- Branch: `feature/russian-language-support-v1` (implementation complete, uncommitted)
- Base: `f1d1c3047240ded2bdaae8eb026741fe140a6da3`

## Completed recently (this branch)

- Russian application-interface localization: `Resources/Localization/SharedResource.ru.resx` with full 464-key parity against English and German.
- Explicit persisted System language preference (System / English / Deutsch / Русский), distinct from a manually chosen concrete language; System now re-resolves the device culture on every application start rather than freezing after first resolution.
- Russian device cultures (`ru-*`) resolve to Russian under System; unsupported device cultures fall back to English.
- Settings language control redesigned as a single accessible `<select>` (System, English, Deutsch, Русский) to avoid four buttons in one narrow row.
- Russian accepted as a translation target for English and German source texts (`ImportText.razor`, `LexicalLookupLanguagePolicy`); Russian is explicitly rejected as a source language.
- Wiktionary translation-target parsing recognizes `lang="ru"` and "Russian:"/"Русский:" text-prefix fallbacks; requests remain sent only to `en.wiktionary.org`/`de.wiktionary.org`, never `ru.wiktionary.org`.
- `docs/BACKLOG.md` created as the internal backlog for solo development (routed from `docs/INDEX.md`).

## Explicitly deferred (not in this branch)

- Russian **source**-text import, Cyrillic tokenization/normalization, Russian Wiktionary language-section parsing, and Russian Wikipedia fallback remain out of scope. Only Russian-as-target is implemented.

## Current known risks

- A learner with only a few available words may see what looks like the same word and question twice in one learning session. Investigation confirmed no accidental backend duplication: the cause is the legitimate one-time "Again" requeue and/or the two opposite card directions from a `Both`-direction word, neither previously distinguished in the Learn UI. Fixed on `feature/learning-repeat-direction-clarity` (uncommitted): the Learn card view now exposes direction and `IsAgainRepeat`, and `Learn.razor` renders a secondary direction label and a "Repeat" badge. Tracked as [KF-LEARN-001](BACKLOG.md).
- "Support KnownFirst" and "Report a bug" in Settings are placeholders (identical no-op handler); not gated out of Release builds.
- Deterministic GUI automation (Appium/UiAutomator2 or equivalent) is not yet implemented; GUI verification remains manual per [GUI_TEST_MATRIX.md](GUI_TEST_MATRIX.md).
- Public Google Play release is intentionally blocked; current distribution is Internal Testing only.
- Native-speaker wording review of the new Russian resource has not been performed; translations are complete but unreviewed by a native speaker.

## Active planned sequence

1. Review and merge the Learn UI repeat/direction clarity fix on `feature/learning-repeat-direction-clarity` ([KF-LEARN-001](BACKLOG.md); implemented and validated, not yet committed or merged).
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

## Relevant files for the next task

- [BACKLOG.md](BACKLOG.md)
- `Services/Study/LearningService.cs`
- `Components/Pages/Learn.razor`
- `Models/LearningModels.cs`
- `KnownFirst.Tests/StudyWorkflowServiceTests.cs`

## Next exact action

Review the uncommitted `feature/learning-repeat-direction-clarity` changes for [KF-LEARN-001](BACKLOG.md), decide whether to commit/merge, and continue with planned sequence item 2 (Support/Report-a-bug controls).

## New-chat handoff

"Read AGENTS.md, docs/PROMPT_AND_TASK_ROUTING.md, docs/CURRENT_WORK.md, and docs/INDEX.md completely. Verify branch, HEAD, Git status, and registered worktrees. Follow the task-specific reading route from docs/INDEX.md. Then perform only the task under 'Next exact action'."
