# KnownFirst Prompt and Task Routing Guide

This document governs ChatGPT-first coordination, dynamic agent routing, and the conversion of user requests into KnownFirst coding-agent prompts. Reading this guide does not authorize repository modification by itself; it enforces strict, low-context operation boundaries and prevents the automatic bundling of unrelated operations.

## A. Governance Classification

Governance policies in KnownFirst are categorized into four exact classes:

1. `DURABLE_REPOSITORY_GOVERNANCE`
   - Repository safety, Git authorization, operation isolation, data protection, single canonical repository and single writing agent rules, test boundaries, and manual user merge policy.
   - These rules belong in tracked repository documentation (`AGENTS.md`, `docs/`) and remain independently usable for any contributor without access to private governance repositories.

2. `DURABLE_PROMPT_AUTHORING_GOVERNANCE`
   - ChatGPT-first coordination, required user-facing explanation language (German, 1–3 concise sentences), prompt language (English), requested technical report language (English), dynamic three-agent comparison-table format, prompt-block framing (beginning with `TASK:` on the first line and ending with `HARD STOP` on the last line), and one-task-per-prompt rules.
   - These rules belong in tracked repository documentation.

3. `KNOWNFIRST_ORCHESTRATION_PREFERENCE`
   - Alignment with the central handoff contract (`Tachiguro/project-governance/UNIVERSAL_PROJECT_HANDOFF_BLUEPRINT.md`, V3.12 when accessible to the coordinator), evaluation across the three approved agent interfaces (`Anti-Gravity`, `ChatGPT Codex`, and `Standalone Claude Code`), dynamic task-to-model/effort selection, usage/cost discipline, and independent-review routing.
   - Do not copy the private model catalog, personal subscription timelines, or usage/quota figures into this public repository.

4. `TRANSIENT_RUNTIME_AVAILABILITY`
   - Current provider access, current model access, subscription tier status, quota percentages, temporary tool availability, host attachment, and similar session-specific runtime facts.
   - These facts must be discovered in the current session when relevant.
   - They must never be persisted as authoritative repository state or treated as durable availability guarantees. Do not record any current quota percentage or provider availability claim.

## B. ChatGPT-First Coordination and Required Prompt Presentation

### 1. ChatGPT-First Division of Labor
Before delegating to any coding agent, the coordinating ChatGPT session performs all steps it can reliably complete directly with its own tools:
- verifying GitHub repository identity, default branch, commits, branches, open/closed pull requests, and active GitHub Issues;
- reading and analyzing repository source code, architecture contracts, diffs, and logs;
- falsifying hypotheses and isolating root causes, file boundaries, and focused regression scopes;
- performing routine scoped GitHub Issue triage, deduplication, creation, comments, checklist updates, and verified status transitions under standing delegation (see Section D).

Delegate to a local coding agent **only** when the step requires the local Windows worktree (`C:\Dev\KnownFirst`), native `.NET 10` / MAUI build or test execution, inspection of uncommitted/unpushed local state, or repository file edits. When no coding-agent execution is needed, answer or complete the coordinator step directly without outputting an artificial agent comparison table.

### 2. Required Prompt Presentation (When an Agent Prompt Is Needed)
Before every coding-agent prompt:

1. Provide a brief user-facing explanation in German (1–3 concise sentences covering the decision, why this step is next, and any relevant boundary).
2. Include exactly one compact Markdown comparison table with these columns:
   - `Agent`
   - `Modell`
   - `Effort`
   - `Empfehlung`
   The table must list the three approved agent interfaces — `Anti-Gravity`, `ChatGPT Codex`, and `Standalone Claude Code` — exactly once each, sorted dynamically by suitability and current availability for the concrete task:
   1. `**Bevorzugt**` (with a 2–6 word task-specific rationale)
   2. `**Alternative**` (with a 2–6 word task-specific rationale)
   3. `**Nicht verwenden**` (task-specific, with a 2–6 word rationale; or `Nicht verfügbar` when quota or required host access is absent)
   No agent interface is permanently prohibited in project-local rules; the `Nicht verwenden` row is strictly task-specific.
3. Present the prompt with its exact `TASK: ...` first line visibly shown above the copy action (using a compact copyable prompt card when reliably supported by the UI, or a single contiguous copyable fenced code block as fallback).
4. Begin every agent prompt on the first line with `TASK:`.
5. End every agent prompt on the last line with `HARD STOP` (do not use decorative start or end wrapper markers).
6. Place no prose, summaries, or follow-up questions after the prompt block.
7. Do not place a nested fenced code block inside an agent prompt.
8. Request verifiable technical reports from programming agents in English (decision code, findings, exact changed-file list and diff summary, tests run and not run with exit codes, local Git status, and the single next recommended lifecycle step).
9. Contain only one next isolated agent task per prompt.

## C. Agent Interfaces, Dynamic Model Routing, and Host Preflight

When a coding-agent prompt is required, select the least usage-intensive approved agent interface, model, and effort level capable of completing the concrete task reliably:

### 1. Three Approved Agent Interfaces
- **`Anti-Gravity` (Single Agent Interface with Multiple Model Routes):** Supports approved Gemini models (fast standard model for routine implementation, documentation reconciliation, and focused testing; higher-reasoning model when available and justified for complex multi-step engineering) as well as embedded Claude models for bounded `MICRO_TASK_ONLY` tasks (single script, small diff, or isolated high-difficulty review/root-cause question requiring few tool calls) when embedded quota is available. All model routes within Anti-Gravity share the single `Anti-Gravity` row in the comparison table.
- **`ChatGPT Codex` (Independent Coding Agent):** Separate agent interface with its own model selection and independent usage budget, used for implementation, focused fixes, deep architecture/persistence/schema/concurrency analysis, and independent read-only reviews.
- **`Standalone Claude Code` (Independent Coding Agent):** Separate agent interface (distinct from embedded Claude inside Anti-Gravity, with its own session, local host binding, tools, and independent usage budget), used for implementation, debugging, and independent reviews when attached to the required local Windows host.

### 2. Effort, Cost, and Escalation Discipline
- Technical correctness and data safety come first; among capable approved options, select the lowest-usage model and effort tier (`Low`, `Medium`, `High`, and selectively `Extra High` on standalone agents only for proven exceptional blockers).
- **Effort proportionality:**
  - `Low`: simple read-only inspections, triage, or straightforward verification checks.
  - `Medium`: standard feature slices, bounded bug fixes, documentation reconciliation, and focused automated test runs.
  - `High`: complex state or concurrency logic, persistence/schema/archive safety, intricate AOT/trimming issues, or high-sensitivity independent reviews.
- Stronger models or higher effort tiers are **not** the default. Prompt length, file count, or routine PR preparation alone do **not** justify escalation.
- Keep separate usage pools distinct (Anti-Gravity Gemini budget, Anti-Gravity embedded Claude/GPT budget, Standalone Claude Code budget, and ChatGPT Codex budget). Never infer one surface's quota or host access from another surface sharing a model name.
- **Advisory nature:** Routing recommendations reflect technical scope, host access, and usage efficiency. The user may override the selected agent or model; a user override does not expand task scope or authorize additional operation modes.
- **Transient vs Durable:** Current provider access, model catalog versions, subscription tiers, and remaining quotas are transient runtime facts governed by the central handoff blueprint and active session inspection. Never persist personal quota numbers or full private model tables in tracked repository files.

### 3. Native Windows Host Preflight and Usage-Stop Rules
- KnownFirst is bound to a `Local Windows` development and verification environment (canonical checkout `C:\Dev\KnownFirst`, PowerShell, `.NET 10` / MAUI toolchain via `.\scripts\knownfirst.ps1` and `dotnet test ./KnownFirst.Tests/KnownFirst.Tests.csproj`).
- Before running local tests, builds, or environment-dependent work, the executing agent must perform a brief host preflight (target $\le 2$ minutes) verifying Windows OS, canonical repository top-level, branch/HEAD, and clean/protected worktree status.
- If the required `Local Windows` host or toolchain is unavailable, the agent must stop immediately and report `ENVIRONMENT_BLOCKED` (listing unexecuted tests) rather than attempting Linux, cloud, container, or WSL workarounds.
- An identical failing command may be retried at most once with a specific diagnostic reason before reporting the blocker. If a stuck diagnosis makes no verifiable progress within ~10 minutes (excluding authorized long-running gates such as `ValidateAll`), the agent must stop and report.

### 4. Delegation and Evidence Verification Rules
- Subagents, delegated writers, background processes, task trackers, or parallel execution require explicit user authorization.
- One physical repository and one writing agent remain the default.
- An agent report is evidence to evaluate, not authoritative proof; verify GitHub Issues, commits, branches, PR metadata, changed files, and deterministic validation directly before claiming completion when those sources are available.

## D. Operation Modes

The repository enforces strict mutual isolation between task phases:

- `ISSUE_TRIAGE_ONLY`: Read-only inventory, deduplication, and prioritization of GitHub Issues and transitional backlog items.
- `ISSUE_DRAFT_ONLY`: Preparing an issue or comment draft without publishing to GitHub.
- `ISSUE_UPDATE_ONLY`: Explicitly authorized or standing-delegated GitHub Issue creation, commenting, checklist updates, or status maintenance.
- `PLAN_ONLY`: Read-only research, architecture, and multi-slice design plan creation.
- `IMPLEMENT_SLICE`: Explicitly authorized composite operation executing focused TDD for exactly one declared slice, followed immediately by checkpoint staging/commit with a checkpoint trailer and a hard stop.
- `CHECKPOINT_COMMIT_ONLY`: Staging and committing a single completed slice that is already focused-green.
- `IMPLEMENT`: Scoped code implementation using TDD (used when checkpoint committing is separately invoked).
- `TEST_ONLY`: Execution of specified test scopes without editing code (including candidate-HEAD `FULL_VALIDATION`).
- `DOCUMENT_ONLY`: Updating documentation for verified implementation.
- `BUILD_ONLY`: Compiling specific target configurations.
- `PACKAGE_ONLY`: Generating APK or AAB artifacts.
- `COMMIT_ONLY`: Staging and committing already reviewed changes (e.g. package documentation or candidate finalization).
- `PUSH_ONLY`: Pushing approved commits to remote repository.
- `PR_ONLY`: Opening or updating a pull request.
- `REVIEW_ONLY`: Read-only diff and contract review.
- `POST_MERGE_SYNC_ONLY`: Fast-forward synchronizing local master with remote master after a manual user merge on GitHub.

### Isolation Rules
- Select **exactly one** primary mode per prompt (or the explicitly authorized `IMPLEMENT_SLICE` composite).
- Pull-request merges are performed exclusively by the user manually through GitHub. Automated agents never merge pull requests, enable auto-merge, or execute `MERGE_ONLY` operations.
- Multiple modes may be combined **only** when the user explicitly requests the combination or when executing `IMPLEMENT_SLICE` whose exact sub-operations and hard stop are defined by governance.
- **Agent-level isolation (absolute, never delegated):** the programming agent executing a prompt must never continue from its assigned mode into another mode on its own, and must not infer additional execution authority from completion of its current prompt or from prior work packages. A checkpoint commit does not imply package review or push; push does not imply PR; PR does not imply merge; merge does not imply build, package, or release work.
- **Orchestrator-level standing delegation** governs only whether ChatGPT may issue the *next* isolated prompt or perform routine scoped GitHub Issue maintenance without additional per-step user typing — see "Standing Orchestration Delegation" below. Standing delegation never authorizes an agent to self-advance within a single prompt beyond an authorized composite's defined hard stop.
- Every final report must explicitly state which operations were intentionally not performed.

### Standing Orchestration Delegation

Once (1) a concrete KnownFirst work package is established, (2) its scope is bounded, (3) repository/GitHub state has been directly verified, and (4) no unresolved material product, architecture, data-integrity, or scope decision remains, the user's standing delegation authorizes ChatGPT — the orchestrator, not the executing agent — to evaluate each phase's report and issue the next correctly isolated, single-mode prompt without requiring the user to type the phase name or separately approve each routine transition.

Standing delegation covers exactly these modes, applied one at a time in isolated prompts:
- `ISSUE_TRIAGE_ONLY` / `ISSUE_DRAFT_ONLY` / `ISSUE_UPDATE_ONLY` (performed directly by the ChatGPT coordinator for routine scoped issue maintenance)
- `PLAN_ONLY`
- `IMPLEMENT_SLICE`
- `CHECKPOINT_COMMIT_ONLY`
- `IMPLEMENT`
- `TEST_ONLY` (including the mandatory candidate-HEAD `FULL_VALIDATION` pre-PR gate)
- `DOCUMENT_ONLY` (package-level documentation reconciliation gate)
- `REVIEW_ONLY`
- `COMMIT_ONLY`
- `PUSH_ONLY`
- `PR_ONLY`
- `POST_MERGE_SYNC_ONLY` (after verified manual user merge on GitHub)

**Standing Delegation for Routine GitHub Issue Management:** Within an already authorized, bounded task scope on `Tachiguro/KnownFirst`, the ChatGPT coordinator may directly deduplicate, create, comment on, update checklists/status (`BACKLOG`, `TRIAGE`, `READY`, `IN_PROGRESS`, `REVIEW`, `BLOCKED`, `DONE`), and close GitHub Issues once acceptance criteria and verification evidence are met, without requiring a local coding agent or separate per-action user confirmation. ChatGPT must verify project identity and evidence before mutating an issue and re-read the GitHub Issue afterward to confirm the published state. Read-only issue modes (`ISSUE_TRIAGE_ONLY`, `ISSUE_DRAFT_ONLY`) perform no mutations. Issue delegation never authorizes unapproved product scope, publishing private data, commits, pushes, PRs, merges, releases, or data deletion.

Standing delegation covers mandatory pre-PR validation without requiring fresh per-PR user build authorization. For example: an approved, decision-free `PLAN_ONLY` may progress to `IMPLEMENT_SLICE` without a ceremonial "approve plan" message; completed slices loop through checkpoint commits; when all slices finish, the package progresses through consolidated `REVIEW_ONLY`, package-level `DOCUMENT_ONLY`, candidate `COMMIT_ONLY` (if documentation edited), mandatory candidate-HEAD `FULL_VALIDATION` (`TEST_ONLY`), `PUSH_ONLY`, and `PR_ONLY` in separate isolated prompts without fresh per-phase user authorization, provided the package remains within its established, bounded scope.

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
- **Documentation Completeness:** `FULL_VALIDATION` must execute only after the candidate HEAD reflects all required code, tests, and documentation. If `DOCUMENT_ONLY` produced documentation edits, those must be committed before `FULL_VALIDATION`. If `DOCUMENT_ONLY` reported `DOCUMENTATION_CURRENT_NO_CHANGES`, the latest checkpoint commit is the candidate HEAD.
- **Placement:** Validation must execute on the exact candidate HEAD before `PUSH_ONLY`.
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

The transition from an approved `PLAN_ONLY` result to implementation is satisfied either by explicit user approval of the presented plan, or by the standing orchestration delegation defined above when the plan is complete, bounded, and exposes no unresolved material product, architecture, data-integrity, or scope decision. If `PLAN_ONLY` exposes such a decision, ChatGPT must stop and ask the user before authorizing any repository-writing work, regardless of standing delegation.

`PLAN_ONLY` is strictly read-only and must report:
- single coherent package objective;
- explicit non-goals;
- declared ordered slice list;
- focused test scope for each slice;
- risk and escalation classification for each slice (persistence/schema migrations, archive compatibility, destructive reset, AOT sensitivity, security/privacy boundaries);
- exact documentation and code areas to read;
- files expected to change;
- proposed minimum implementation per slice;
- affected durable documentation;
- explicitly excluded operations.

`PLAN_ONLY` must stop without creating a branch, editing files, running builds, committing, pushing, or opening a PR. The subsequent implementation prompts must reference the approved plan and may not silently expand its scope.

## F. Implementation, Slices, and TDD

Implementation is authorized only after the planning gate in Section E is satisfied.

### Multi-Slice Feature Package Execution

A coherent feature package consists of one or more declared, ordered implementation slices executed on a dedicated task branch:

1. **Focused TDD loop per slice:**
   - Add or update the minimum focused test(s) first (`FOCUSED_AUTOMATED`).
   - Run only that focused scope to confirm genuine behavioral RED (missing intended behavior; syntax, fixture, or compiler breakage is not RED).
   - Implement the minimum production code change for that slice.
   - Rerun the identical focused scope to a GREEN result.
   - Run any targeted extra testing specifically required by the slice's risk classification (e.g. migration, archive, or data-safety contracts).
   - Run `git diff --check`.
   - Stage explicit file paths and create a checkpoint commit with the required trailer.
   - Stop after the checkpoint commit.

2. **Checkpoint commit contract:**
   - Checkpoint commits record completed, focused-green slices on the task branch.
   - Format: conventional subject plus trailer:
     `KnownFirst-Checkpoint: <package-id> <slice-index>/<slice-count> <short-scope>`
   - A checkpoint commit does **not** trigger broad test suites, `ValidateAll`, package-level review, package-level documentation reconciliation, push, PR, or merge.
   - The latest checkpoint commit **may** become the final candidate HEAD if package-level `REVIEW_ONLY` passes and package-level `DOCUMENT_ONLY` reports `DOCUMENTATION_CURRENT_NO_CHANGES`. The presence of a checkpoint trailer does not disqualify a commit from being validated and published. No empty commit or `git commit --allow-empty` is required or permitted as lifecycle machinery.

3. **Risk and Escalation Rules:**
   - **Irreversible / persisted-state risk:** Schema migrations, archive format/DTO compatibility changes, and destructive reset behaviors must be classified in `PLAN_ONLY` and backed by targeted contract tests. When practical, schedule them late in the package sequence. If multiple independent irreversible transitions make reasoning difficult or unsafe, split into separate packages.
   - **Focused testability:** Every behavior-changing slice requires genuine behavioral RED evidence. Characterization and test-hardening packages (adding tests without behavior changes) are the sole explicit exception and pass immediately.
   - **Targeted extra testing:** Persistence/migration slices run relevant migration contracts; backup/archive slices run relevant round-trip contracts; destructive reset slices run relevant data-safety contracts. Broad `ValidateAll` is not run per slice.
   - **AOT / trimming sensitivity:** Must be identified in `PLAN_ONLY`. Early Android Release compilation may be recommended when justified, but is not universally mandatory per slice since final `ValidateAll` remains fail-closed.
   - **Package coherence:** All slices in a package must support a single, coherent product or engineering objective. Unrelated work must not be bundled merely to amortize lifecycle overhead. Coherence is semantic; it is not evaluated by arbitrary title heuristics.
   - **Validation-gate self-modification:** Any change to `ValidateAll`, its required step list, or launcher scripts defining gate behavior must be an isolated package and never bundled with product features.
   - **Security and privacy:** Security, privacy, permission, key, or consent boundary changes must be classified in `PLAN_ONLY` and separately enumerated in the consolidated `REVIEW_ONLY` report.

### Required Slice Report Content
The final slice report must explicitly state:
- which focused tests passed (identified by test name, class, or filter);
- genuine RED and GREEN evidence for the slice;
- checkpoint commit SHA and trailer;
- which broader test scopes were intentionally not executed;
- any slice-level risk or observation for the consolidated package review.

## G. Test-Only Behavior

`TEST_ONLY` mode:
- runs only the requested test scope;
- never modifies production or test code;
- never fixes a test failure automatically;
- reports test failure details and stops;
- does not build other targets unless the explicitly requested test is a documented composite test (e.g. Windows smoke test or candidate-HEAD `FULL_VALIDATION`).

Refer to [docs/TESTING.md](TESTING.md) for exact test scope definitions.

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
- `WINDOWS_MSIX_PACKAGE` creates only the x64 Release MSIX package and SHA-256 sidecar; it does not install, sideload, contact Partner Center, or upload/submit to the Microsoft Store.
- Artifact creation is strictly isolated from installation, deployment, and distribution; Store upload/submission and certificate operations remain separately authorized and are never covered by standing delegation.
- `FULL_RELEASE_OUTPUT_PACKAGE` is used only when the user explicitly requests the complete release output package.
- Normal feature completion never triggers a build automatically.
- Refer to [docs/BUILD_AND_RELEASE.md](BUILD_AND_RELEASE.md) for exact commands and safety boundaries.

## I. Documentation Phase & Lifecycle

`DOCUMENT_ONLY` is a **mandatory** package-level reconciliation gate before candidate finalization and validation.

### Documentation Timings
1. **Package declaration:** `docs/CURRENT_WORK.md` may describe the active package objective and declared slice list for orchestration and session resume.
2. **Mid-package contract exception only:** An authoritative contract document may be edited in a slice's checkpoint commit only when that slice establishes a durable contract that subsequent slices in the same package must obey.
3. **Package-final reconciliation:** One consolidated `DOCUMENT_ONLY` phase executes over the entire package after all slices are completed and approved by consolidated `REVIEW_ONLY`.

### Mandatory Documentation Gate Invariant

Candidate finalization and `FULL_VALIDATION` require `DOCUMENT_ONLY` to report either:
- `DOCUMENTATION_RECONCILED_WITH_CHANGES` — documentation files required semantic edits. These edits are committed via `COMMIT_ONLY` before `FULL_VALIDATION`.
- `DOCUMENTATION_CURRENT_NO_CHANGES` — all relevant documentation was inspected and is already accurate. The latest checkpoint commit is the candidate HEAD and proceeds directly to `FULL_VALIDATION`.

**No-edit rule:** When documentation is already accurate, do NOT create artificial documentation churn. A genuine `DOCUMENTATION_CURRENT_NO_CHANGES` result satisfies the gate without a commit.

**Any repository-file modification after `FULL_VALIDATION` — including documentation edits — invalidates the exact-HEAD evidence and requires a new candidate commit and a new `FULL_VALIDATION` run before `PUSH_ONLY` or `PR_ONLY`.**

### Normal Code/Behavior Multi-Slice Package Lifecycle

```
PLAN_ONLY (declare coherent objective & bounded slices)
  → Slice Loop (for each declared slice):
      IMPLEMENT_SLICE (focused TDD RED → minimum implementation → focused GREEN → checkpoint commit)
  → Consolidated REVIEW_ONLY (entire package)
  → Mandatory Package-Level DOCUMENT_ONLY
  → Candidate Finalization / COMMIT_ONLY (if documentation edited)
  → Exact-Candidate-HEAD FULL_VALIDATION (TEST_ONLY)
  → PUSH_ONLY
  → PR_ONLY
  → Manual User Merge on GitHub
  → POST_MERGE_SYNC_ONLY
```

### Documentation-Only Package Lifecycle

Documentation-only packages do not invent an `IMPLEMENT` phase:

```
PLAN_ONLY
  → DOCUMENT_ONLY
  → REVIEW_ONLY
  → COMMIT_ONLY
  → FULL_VALIDATION
  → PUSH_ONLY
  → PR_ONLY
  → Manual User Merge on GitHub
  → POST_MERGE_SYNC_ONLY
```

### Scope of Documentation Updates & Anti-Treadmill Rules

Update only:
- directly affected product, architecture, database, UI, or workflow contracts;
- GitHub Issues on `Tachiguro/KnownFirst` (primarily through the ChatGPT coordinator's standing issue delegation) to track newly accepted defects, deferred follow-ups, or downstream packages, and `docs/BACKLOG.md` only where transitional `KF-*` records or open `REQUIRES_PRODUCT_DECISION` items are directly updated;
- `CHANGELOG.md` for verified user-visible or internal-foundation behavior changes;
- concise user-facing release notes for the intended future release (see [docs/VERSIONING.md](VERSIONING.md));
- `docs/CURRENT_WORK.md` when local branch/checkpoint operational task state changes.

**Issue-First Tracking, Follow-Up Closure Audit & Foundation Milestone Rule:**
- GitHub Issues in `Tachiguro/KnownFirst` are the primary tracking system for newly accepted tasks, bugs, governance items, and their live status. Existing `KF-*` records in `docs/BACKLOG.md` remain preserved as transitional and historical evidence until separately verified and migrated to GitHub Issues with their original `KF-*` provenance.
- An agent prompt cannot declare a package fully reconciled when accepted deferred follow-ups, defects, or downstream requirements remain untracked. Every deferred or downstream item must be tracked in a verified GitHub Issue (or reference an existing open transitional `KF-*` ID in `docs/BACKLOG.md` prior to migration) before reconciliation is declared complete.
- Merging a foundation package (such as a core scheduling engine or clean domain model) never marks a parent initiative or roadmap milestone complete while downstream packages (persistence, archive, runtime cutover, UI) remain open.

**Anti-Treadmill Discipline:**
- Do **not** duplicate changing task-status lists across multiple Markdown files. Live task status belongs in GitHub Issues; durable specifications and verified architecture belong in `docs/`.
- Do **not** record exact `FULL_VALIDATION` test totals, PR numbers, or merge commit SHAs in durable project status documents (`docs/PROJECT_STATE.md`, `docs/ROADMAP.md`). Those facts belong to Git history, validation logs, GitHub Issues, and the PR body.
- No mandatory post-merge documentation PR shall exist merely to record lifecycle metadata.
- Avoid transient lifecycle-phase prose (e.g. "DOCUMENT_ONLY in progress", "waiting for PUSH_ONLY") in durable documents.

## J. Git and PR Phases

Keep Git and PR operations strictly separated:
- `CHECKPOINT_COMMIT_ONLY`: Staging and committing a completed focused-green slice with its checkpoint trailer.
- `COMMIT_ONLY`: Inspecting and committing reviewed package-level documentation changes to finalize the candidate HEAD.
- `PUSH_ONLY`: Pushing only the approved existing branch and validated candidate commit.
- `PR_ONLY`: Creating or updating only the pull request.
- `REVIEW_ONLY`: Read-only diff and contract review.
- `POST_MERGE_SYNC_ONLY`: Fast-forward synchronizing local master with remote master after a manual user merge on GitHub.
- **Merge:** Pull requests are merged exclusively by the user manually through GitHub. ChatGPT and programming agents never merge PRs, enable auto-merge, or execute `MERGE_ONLY` operations. After an approved final review and validation, ChatGPT informs the user that the PR is ready for manual merge. Upon user confirmation of the merge, the next mode is `POST_MERGE_SYNC_ONLY`.

No Git mode may rewrite published history or force-push.

## K. New-Chat Bootstrap Protocol

Dynamic discovery for new ChatGPT or prompt-authoring sessions is governed by [docs/NEW_CHAT_BOOTSTRAP.md](NEW_CHAT_BOOTSTRAP.md). Refer to that document for the complete initialization sequence, central governance check, repository and issue access gate, GitHub Issue and pull-request inspection rules, and evergreen user bootstrap prompt.
