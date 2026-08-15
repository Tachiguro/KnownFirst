# KnownFirst Prompt and Task Routing Guide

This document governs the conversion of user requests into KnownFirst agent prompts. It is read before preparing any KnownFirst coding-agent prompt. Reading this guide does not authorize repository modification by itself; it enforces strict, low-context operation boundaries and prevents the automatic bundling of unrelated operations.

## A. Governance Classification

Governance policies in KnownFirst are categorized into four exact classes:

1. `DURABLE_REPOSITORY_GOVERNANCE`
   - Repository safety, Git authorization, operation isolation, data protection, single worktree (`C:\Dev\KnownFirst`) and single writing agent rules, test boundaries, and manual user merge policy.
   - These rules belong in tracked repository documentation (`AGENTS.md`, `docs/`).

2. `DURABLE_PROMPT_AUTHORING_GOVERNANCE`
   - Required user-facing explanation language (German), prompt language (English), requested technical report language (English), comparison-table format, prompt-block framing (`PROMPT START` / `PROMPT ENDE`), and one-task-per-prompt rules.
   - These rules belong in tracked repository documentation.

3. `KNOWNFIRST_ORCHESTRATION_PREFERENCE`
   - Preferred provider rows, task-to-model routing recommendations, model-cost discipline, and independent-review preferences.
   - The fixed provider rows `Anti-Gravity`, `Claude`, and `Codex` are orchestration comparison preferences; they are not declarations that those providers or models are currently connected or available.

4. `TRANSIENT_RUNTIME_AVAILABILITY`
   - Current provider access, current model access, quota percentages, temporary tool availability, session-specific capability, and similar runtime facts.
   - These facts must be discovered in the current session when relevant.
   - They must never be persisted as authoritative repository state or treated as durable availability guarantees. Do not record any current quota percentage or provider availability claim.

## B. Required Prompt Presentation

Before every coding-agent prompt:

1. Provide a brief user-facing explanation in German.
2. Include exactly one Markdown comparison table with these exact columns:
   - `Agent`
   - `Modell`
   - `Effort`
   - `Präferenz/Bewertung`
   The table must contain rows for `Anti-Gravity`, `Claude`, and `Codex` in that exact order, with the recommended choice visibly marked as the best choice.
3. Provide exactly one contiguous copyable fenced code block per prompt block.
4. Begin every agent prompt exactly with `PROMPT START`.
5. End every agent prompt exactly with `PROMPT ENDE`.
6. Place no prose or explanations after the prompt block.
7. Do not place a fenced code block inside an agent prompt.
8. Request technical reports from programming agents in English.
9. Contain only one next agent task per prompt.

## C. Model Routing

Prompts must select the least expensive capable model:

- **Mechanical (file moves, lint fixes, link audits):** `Anti-Gravity` with Gemini 3.6 Flash Low
- **Routine (scoped single-file features, documentation updates):** `Anti-Gravity` with Gemini 3.6 Flash Medium
- **Substantial (multi-file features, structured refactoring):** `Anti-Gravity` with Gemini 3.6 Flash High
- **Difficult multi-file coding (complex domain logic, intricate UI/state):** Claude Sonnet 4.6 Thinking
- **Complex migration, data integrity, concurrency, difficult AOT/trimming, or core architecture:** `Anti-Gravity` with Gemini 3.1 Pro High
- **Emergency (only after strong models failed on a verified bug):** Claude Opus 4.6 Thinking
- **Independent read-only review:** `Anti-Gravity` with GPT-OSS 120B Medium or an appropriate local model

### Escalation and User Override Rules
- Gemini 3.1 Pro is **not** the default model.
- Task importance, prompt length, number of files, tests, documentation, or PR creation alone do **not** justify using Pro.
- Escalation occurs only after a concrete failure or newly discovered technical risk.
- Ignore visible quota percentages when selecting the technically appropriate model, unless the user explicitly asks for quota-aware routing.
- **Advisory nature:** Model routing is a recommendation based on technical scope and risk. The user may explicitly override the recommended model. A user override does not expand task scope or authorize additional operation modes.
- **Transient vs Durable:** Do not claim that Anti-Gravity, Claude, Codex, or a named model is currently available merely because it appears in the routing table. Current provider access, current model access, and quotas are transient runtime facts. Do not persist quota percentages or current availability statements.

### Delegation and Evidence Verification Rules
- Subagents, delegated writers, background processes, task trackers, or parallel execution require explicit user authorization.
- One physical repository (`C:\Dev\KnownFirst`) and one writing agent remain the default.
- An agent report is evidence to evaluate, not authoritative proof; verify GitHub, commits, branches, PR metadata, changed files, and deterministic validation directly before claiming completion when those sources are available.

## D. Operation Modes

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
- `POST_MERGE_SYNC_ONLY`: Fast-forward synchronizing local master with remote master after a manual user merge on GitHub.

### Isolation Rules
- Select **exactly one** primary mode per prompt.
- Pull-request merges are performed exclusively by the user manually through GitHub. Automated agents never merge pull requests, enable auto-merge, or execute `MERGE_ONLY` operations.
- Multiple modes may be combined **only** when the user explicitly requests the combination.
- **Agent-level isolation (absolute, never delegated):** the programming agent executing a prompt must never continue from its assigned mode into another mode on its own, and must not infer additional execution authority from completion of its current prompt or from prior work packages. Commit does not imply push; push does not imply PR; PR does not imply merge; merge does not imply build, package, or release work.
- **Orchestrator-level standing delegation** governs only whether ChatGPT may issue the *next* isolated prompt without additional per-phase user typing — see "Standing Orchestration Delegation" below. Standing delegation never authorizes an agent to self-advance within a single prompt.
- Every final report must explicitly state which operations were intentionally not performed.

### Standing Orchestration Delegation

Once (1) a concrete KnownFirst work package is established, (2) its scope is bounded, (3) repository/GitHub state has been directly verified, and (4) no unresolved material product, architecture, data-integrity, or scope decision remains, the user's standing delegation authorizes ChatGPT — the orchestrator, not the executing agent — to evaluate each phase's report and issue the next correctly isolated, single-mode prompt without requiring the user to type the phase name or separately approve each routine transition.

Standing delegation covers exactly these modes, applied one at a time in isolated prompts:
- `PLAN_ONLY`
- `IMPLEMENT`
- `TEST_ONLY` (including the mandatory candidate-HEAD `FULL_VALIDATION` pre-PR gate)
- `DOCUMENT_ONLY`
- `REVIEW_ONLY`
- `COMMIT_ONLY`
- `PUSH_ONLY`
- `PR_ONLY`

Standing delegation covers mandatory pre-PR validation without requiring fresh per-PR user build authorization. For example: an approved, decision-free `PLAN_ONLY` may progress to `IMPLEMENT` without a ceremonial "approve plan" message; a reviewed change progresses through `COMMIT_ONLY`, mandatory candidate-HEAD `FULL_VALIDATION` (`TEST_ONLY`), `PUSH_ONLY`, and `PR_ONLY` in separate isolated prompts without fresh per-phase user authorization, provided the package remains within its established, bounded scope.

If `PLAN_ONLY`, or any later phase, exposes a genuine unresolved material decision (see Section E), ChatGPT must stop and ask the user regardless of standing delegation.

### Mandatory Pre-PR Full-Validation Gate

Every future pull request without exception (including documentation-only and trivial packages; no docs-only exemption) is strictly blocked until the exact final candidate commit (HEAD) has successfully passed the complete documented `FULL_VALIDATION` composite:

```powershell
.\scripts\knownfirst.ps1 -Action ValidateAll
```

This matrix encompasses:
1. `ALL_AUTOMATED` test suite execution (`KnownFirst.Tests`);
2. Windows Debug build (`net10.0-windows10.0.19041.0`);
3. Windows Release build (`net10.0-windows10.0.19041.0`);
4. Android Debug build validation (`net10.0-android`);
5. Android Release build validation (`net10.0-android`).

**Gate Invariants & Semantics:**
- **Exact-HEAD Evidence:** Validation evidence belongs to the exact final candidate commit SHA intended for the PR.
- **Placement:** Validation must execute after `COMMIT_ONLY` and before `PUSH_ONLY`.
- **Fail-Closed Blocking:** `PUSH_ONLY` and `PR_ONLY` are strictly blocked unless that exact HEAD has a recorded successful full-validation result.
- **Stale-Evidence Invalidation:** Any subsequent commit or repository-file modification invalidates the validation evidence and renders it stale; `ValidateAll` must run again on the new candidate HEAD before progression.
- **Fail-Closed Stop Policy:** Any test or build failure blocks progression immediately. The validation operation must never attempt automatic code fixes.
- **PR Evidence:** The final validation report and subsequent PR body must record the exact HEAD SHA, execution command, exit status, actual automated test totals, and each required build result.

### Non-Delegable Operations

This is the canonical, detailed list. Other tracked documents reference this list rather than duplicating it. Standing delegation does **not** authorize:
- pull-request merge or enabling auto-merge;
- destructive Git/history operations: reset, rebase, stash, amend, history rewriting, or force-push;
- deleting branches or worktrees;
- tags, releases, deployment, or publishing;
- Ad-hoc `BUILD_ONLY` unless the user explicitly requested that build (standing delegation authorizes only the mandatory pre-PR `FULL_VALIDATION` composite on the exact candidate HEAD);
- `PACKAGE_ONLY` unless the user explicitly requested that package;
- APK or AAB creation without explicit user request;
- ADB, emulator, device, or manual-device operations without explicit user request;
- dangerous user-data/database/schema operations outside an already explicitly scoped and approved package;
- subagents, delegated writers, background tasks/processes, task trackers, or parallel writers;
- material scope expansion or a genuinely ambiguous product-direction decision.

Manual PR merge remains exclusively the repository owner's action through GitHub. After a verified manual merge, ChatGPT may issue exactly one `POST_MERGE_SYNC_ONLY` prompt per the existing lifecycle contract.

## E. Planning Approval Gate

Every request that would modify repository files must first execute in `PLAN_ONLY` mode.

The transition from an approved `PLAN_ONLY` result to `IMPLEMENT` is satisfied either by explicit user approval of the presented plan, or by the standing orchestration delegation defined above when the plan is complete, bounded, and exposes no unresolved material product, architecture, data-integrity, or scope decision. If `PLAN_ONLY` exposes such a decision, ChatGPT must stop and ask the user before authorizing any repository-writing work, regardless of standing delegation.

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

## F. Implementation and TDD

`IMPLEMENT` mode is authorized only after the planning gate in Section E is satisfied — either by explicit user plan approval or by standing orchestration delegation when the plan is complete, bounded, and decision-free.

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

## G. Test-Only Behavior

`TEST_ONLY` mode:
- runs only the requested test scope;
- never modifies production or test code;
- never fixes a test failure automatically;
- reports test failure details and stops;
- does not build other targets unless the explicitly requested test is a documented composite test (e.g. Windows smoke test).

Refer to [docs/TESTING.md](TESTING.md) for exact test scope definitions. When the user specifies "all tests", the prompt author must clarify whether this means all automated unit tests or full validation including manual GUI/platform work.

## H. Build and Package Isolation

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
- `WINDOWS_PORTABLE_PACKAGE`
- `WINDOWS_MSIX_PACKAGE`
- `FULL_RELEASE_OUTPUT_PACKAGE`

### Rules
- One requested build runs only that build. No tests run as a side effect.
- An APK request without specified configuration requires clarification.
- An AAB request does not authorize Google Play Store upload.
- `WINDOWS_PORTABLE_PACKAGE` creates only the self-contained Windows x64 Release ZIP archive and SHA-256 sidecar; it does not launch, install, or distribute the package.
- `WINDOWS_MSIX_PACKAGE` creates only the x64 Release MSIX package and SHA-256 sidecar (unsigned by default, or signed via external certificate thumbprint); it does not install, sideload, contact Partner Center, or upload/submit to the Microsoft Store.
- Artifact creation is strictly isolated from installation, deployment, and distribution; Store upload/submission and certificate operations remain separately authorized and are never covered by standing delegation.
- `FULL_RELEASE_OUTPUT_PACKAGE` is used only when the user explicitly requests the complete release output package.
- Normal feature completion never triggers a build automatically.
- Refer to [docs/BUILD_AND_RELEASE.md](BUILD_AND_RELEASE.md) for exact commands and safety boundaries.

## I. Documentation Phase

`DOCUMENT_ONLY` follows verified implementation only when explicitly requested.

Updates only:
- directly affected product, architecture, database, UI, or workflow contracts;
- `CHANGELOG.md` for verified user-visible behavior;
- concise user-facing release notes for the intended future release (see [docs/VERSIONING.md](VERSIONING.md));
- `docs/CURRENT_WORK.md` when operational task state changes.

Do not modify build documentation merely because a product feature changed. Build or package agents consume already approved release notes; they do not author feature descriptions.

## J. Git and PR Phases

Keep Git and PR operations strictly separated:
- `COMMIT_ONLY`: Inspect and commit only already reviewed changes.
- `PUSH_ONLY`: Push only the approved existing branch and commit.
- `PR_ONLY`: Create or update only the pull request.
- `REVIEW_ONLY`: Read-only diff and contract review.
- `POST_MERGE_SYNC_ONLY`: Fast-forward synchronize local master with remote master after a manual user merge on GitHub.
- **Merge:** Pull requests are merged exclusively by the user manually through GitHub. ChatGPT and programming agents never merge PRs, enable auto-merge, or execute `MERGE_ONLY` operations. After an approved final review, ChatGPT informs the user that the PR is ready for manual merge. Upon user confirmation of the merge, the next mode is `POST_MERGE_SYNC_ONLY`.

No Git mode may rewrite published history or force-push.

## K. New-Chat Bootstrap Protocol

Dynamic discovery for new ChatGPT or prompt-authoring sessions is governed by [docs/NEW_CHAT_BOOTSTRAP.md](NEW_CHAT_BOOTSTRAP.md). Refer to that document for the complete initialization sequence, repository access gate, pull-request inspection rules, and evergreen user bootstrap prompt.
