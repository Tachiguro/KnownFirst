# KnownFirst Prompt and Task Routing Guide

This document governs the conversion of user requests into KnownFirst agent prompts. It is read before preparing any KnownFirst coding-agent prompt. Reading this guide does not authorize repository modification by itself; it enforces strict, low-context operation boundaries and prevents the automatic bundling of unrelated operations.

## A. Required Prompt Header

Before every coding-agent prompt, the prompt author must state:

- **Task classification:** [e.g. PLAN_ONLY, IMPLEMENT, TEST_ONLY, etc.]
- **Agent:** [e.g. Primary Agent / Subagent]
- **Model:** [e.g. Gemini 3.6 Flash Medium, Claude Sonnet 4.6 Thinking, etc.]
- **Effort:** [Low / Medium / High]
- **Speed:** [Standard / Fast]
- **Project directory:** `C:\Dev\KnownFirst`

All agent prompts must:
- be written in English;
- use exactly one continuous fenced code block;
- begin exactly with `PROMPT START`;
- end exactly with `PROMPT ENDE`;
- never contain nested prompt blocks;
- request a final report in German;
- contain only one next agent task.

## B. Model Routing

Prompts must select the least expensive capable model:

- **Mechanical (file moves, lint fixes, link audits):** Gemini 3.6 Flash Low
- **Routine (scoped single-file features, documentation updates):** Gemini 3.6 Flash Medium
- **Substantial (multi-file features, structured refactoring):** Gemini 3.6 Flash High
- **Difficult multi-file coding (complex domain logic, intricate UI/state):** Claude Sonnet 4.6 Thinking
- **Complex migration, data integrity, concurrency, difficult AOT/trimming, or core architecture:** Gemini 3.1 Pro High
- **Emergency (only after strong models failed on a verified bug):** Claude Opus 4.6 Thinking
- **Independent read-only review:** GPT-OSS 120B Medium or an appropriate local model

### Escalation and User Override Rules
- Gemini 3.1 Pro is **not** the default model.
- Task importance, prompt length, number of files, tests, documentation, or PR creation alone do **not** justify using Pro.
- Escalation occurs only after a concrete failure or newly discovered technical risk.
- Ignore visible quota percentages when selecting the technically appropriate model, unless the user explicitly asks for quota-aware routing.
- **Advisory nature:** Model routing is a recommendation based on technical scope and risk. The user may explicitly override the recommended model. A user override does not expand task scope or authorize additional operation modes.

## C. Operation Modes

The repository enforces strict mutual isolation between task phases:

- `PLAN_ONLY`: Read-only research and design plan creation.
- `IMPLEMENT`: Scoped code implementation using TDD.
- `TEST_ONLY`: Execution of specified test scopes without editing code.
- `DOCUMENT_ONLY`: Updating documentation for verified implementation.
- `BUILD_ONLY`: Compiling specific target configurations.
- `PACKAGE_ONLY`: Generating APK or AAB artifacts.
- `COMMIT_ONLY`: Staging and committing already reviewed changes.
- `PUSH_ONLY`: Pushing approved commits to remote repository.
- `PR_ONLY`: Opening or updating a pull request.
- `REVIEW_ONLY`: Read-only diff and contract review.
- `SYNC_ONLY`: Fast-forward synchronizing local master with remote.

### Isolation Rules
- Select **exactly one** primary mode per prompt.
- Multiple modes may be combined **only** when the user explicitly requests the combination.
- Completion of one mode never authorizes the next mode.
- The agent must not infer permission from previous work packages.
- Every final report must explicitly state which operations were intentionally not performed.

## D. Planning Approval Gate

Every request that would modify repository files must first execute in `PLAN_ONLY` mode, unless the user explicitly states that a specific previously presented plan is approved.

`PLAN_ONLY` is strictly read-only and must report:
- task objective;
- acceptance criteria;
- explicit non-goals;
- exact documentation and code areas to read;
- files expected to change;
- focused tests to add or modify;
- expected initial red test result;
- proposed minimum implementation;
- documentation affected after verification;
- risks and unresolved product decisions;
- explicitly excluded operations.

`PLAN_ONLY` must stop without creating a branch, editing files, running builds, committing, pushing, or opening a PR. The subsequent `IMPLEMENT` prompt must reference the approved plan and may not silently expand its scope.

## E. Implementation and TDD

`IMPLEMENT` mode is authorized **only** after explicit user plan approval.

### Default TDD Sequence
1. Verify initial repository state.
2. Create or switch to the approved task branch.
3. Read only task-relevant contracts, code, and tests.
4. Add the minimum focused tests first.
5. Run only those focused tests.
6. Confirm that failure is caused by missing intended behavior (expected red result) rather than syntax, environment, or fixture errors.
7. Implement the minimum production change.
8. Run only the same focused tests.
9. Stop after the focused tests pass green.

`IMPLEMENT` must **not** automatically:
- run all automated tests;
- run UI-contract tests outside the affected scope;
- run smoke tests or manual GUI tests;
- build any platform;
- update documentation;
- commit, push, or create a PR.

### Required IMPLEMENT Report Content
The final `IMPLEMENT` report must explicitly state:
- which focused tests passed, identified by test name, class, or exact filter;
- which broader test scopes were intentionally not executed and remain available only through a separate `TEST_ONLY` operation;
- which documentation contracts or user-facing release-note entries may require a later `DOCUMENT_ONLY` operation, without updating them automatically;
- whether any implementation risk or unresolved decision remains.

The report must not claim or imply that unexecuted test scopes passed.

## F. Test-Only Behavior

`TEST_ONLY` mode:
- runs only the requested test scope;
- never modifies production or test code;
- never fixes a test failure automatically;
- reports test failure details and stops;
- does not build other targets unless the explicitly requested test is a documented composite test (e.g. Windows smoke test).

Refer to [docs/TESTING.md](TESTING.md) for exact test scope definitions. When the user specifies "all tests", the prompt author must clarify whether this means all automated unit tests or full validation including manual GUI/platform work.

## G. Build and Package Isolation

`BUILD_ONLY` and `PACKAGE_ONLY` modes must be exact and isolated.

### Recognized Build Intents
- `WINDOWS_DEBUG_BUILD`
- `WINDOWS_RELEASE_BUILD`
- `WINDOWS_BETADIAGNOSTIC_BUILD`
- `ANDROID_DEBUG_BUILD`
- `ANDROID_RELEASE_BUILD`
- `ANDROID_BETADIAGNOSTIC_BUILD`

### Recognized Package Intents
- `ANDROID_DEBUG_APK`
- `ANDROID_RELEASE_APK`
- `ANDROID_BETADIAGNOSTIC_APK`
- `ANDROID_GOOGLE_PLAY_AAB`
- `FULL_RELEASE_OUTPUT_PACKAGE`

### Rules
- One requested build runs only that build. No tests run as a side effect.
- An APK request without specified configuration requires clarification.
- An AAB request does not authorize Google Play Store upload.
- `FULL_RELEASE_OUTPUT_PACKAGE` is used only when the user explicitly requests the complete release output package.
- Normal feature completion never triggers a build automatically.
- Refer to [docs/BUILD_AND_RELEASE.md](BUILD_AND_RELEASE.md) for exact commands and safety boundaries.

## H. Documentation Phase

`DOCUMENT_ONLY` follows verified implementation only when explicitly requested.

Updates only:
- directly affected product, architecture, database, UI, or workflow contracts;
- `CHANGELOG.md` for verified user-visible behavior;
- concise user-facing release notes for the intended future release (see [docs/VERSIONING.md](VERSIONING.md));
- `docs/CURRENT_WORK.md` when operational task state changes.

Do not modify build documentation merely because a product feature changed. Build or package agents consume already approved release notes; they do not author feature descriptions.

## I. Git and PR Phases

Keep Git and PR operations strictly separated:
- `COMMIT_ONLY`: Inspect and commit only already reviewed changes.
- `PUSH_ONLY`: Push only the approved existing branch and commit.
- `PR_ONLY`: Create or update only the pull request.
- `REVIEW_ONLY`: Read-only diff and contract review.
- `SYNC_ONLY`: Fast-forward synchronize local master with remote master.
- **Merge:** Remains a separate explicit user decision and is never an automatic agent mode.

No Git mode may rewrite published history or force-push.

## J. New-Chat Bootstrap

When starting a new ChatGPT or prompt-authoring session for KnownFirst, distinguish repository access capability:

1. **Session WITH direct repository access:**
   - Read `AGENTS.md`, `docs/PROMPT_AND_TASK_ROUTING.md`, `docs/INDEX.md`, and only the selected task-specific documents directly from the synchronized intended branch.
2. **Session WITHOUT direct repository access:**
   - Do not claim or imply that repository files were read.
   - Ask the user to provide repository access or paste/upload the relevant current file contents.
   - Do not reconstruct contracts from memory.
   - Do not generate an implementation prompt until the required current rules are available.

Do not assume that every ChatGPT conversation automatically has GitHub or repository access.

### Minimal User Bootstrap Phrase
*(Valid when the session has repository access)*:
`KnownFirst: read AGENTS.md and docs/PROMPT_AND_TASK_ROUTING.md from master, then prepare only the next scoped agent prompt.`
