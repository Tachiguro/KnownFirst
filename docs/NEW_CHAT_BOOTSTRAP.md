# KnownFirst New-Chat Bootstrap Protocol

## 1. Purpose and Authority

This protocol governs the initialization sequence for new ChatGPT and prompt-authoring sessions for the KnownFirst repository.

Live GitHub repository, branch, pull-request, and GitHub Issue states — together with verified local worktree state — are authoritative over static or pasted prompt text. Prompt authoring and session initialization must discover current repository and issue state dynamically rather than relying on stale prompt fragments, old chat history, or hardcoded commit hashes.

ChatGPT operates as a **ChatGPT-first coordinator**: it directly performs all reliable GitHub, issue, branch, commit, pull-request, source-code, diff, and log analysis before delegating only work that genuinely requires the local Windows worktree, native toolchain execution, or repository edits to a local coding agent.

When the coordinator has authorized access to the central governance contract (`Tachiguro/project-governance/UNIVERSAL_PROJECT_HANDOFF_BLUEPRINT.md`, verified at V3.12), it verifies the central version at session start (or reports `GOVERNANCE_SOURCE_UNVERIFIED` if inaccessible or stale). Public repository rules in `AGENTS.md` and `docs/` remain self-contained and binding without copying private model catalogs or personal quota data into the public repository.

Standing orchestration delegation, defined precisely in [docs/PROMPT_AND_TASK_ROUTING.md](PROMPT_AND_TASK_ROUTING.md), authorizes ChatGPT to perform routine scoped GitHub Issue maintenance (`ISSUE_TRIAGE_ONLY`, `ISSUE_DRAFT_ONLY`, `ISSUE_UPDATE_ONLY`) and to issue the next correctly isolated coding-agent prompt (`PLAN_ONLY`, `IMPLEMENT_SLICE`, `CHECKPOINT_COMMIT_ONLY`, `REVIEW_ONLY`, `DOCUMENT_ONLY`, `COMMIT_ONLY`, candidate-HEAD `FULL_VALIDATION`, `PUSH_ONLY`, `PR_ONLY`, `POST_MERGE_SYNC_ONLY`) without requiring the user to separately request each routine phase, provided live repository/GitHub state has been verified per this protocol and no unresolved material decision remains. It never authorizes merge, auto-merge, or any operation on that document's non-delegable-operations list; those always require explicit user action.

## 2. Repository-Access Capability Gate

When initializing a new session, distinguish repository and GitHub Issue access capability immediately:

1. **Session WITH direct GitHub / repository access:**
   - Follow the dynamic discovery sequence below.
   - Read configuration, active GitHub Issues, pull requests, and status documents directly from the repository.
2. **Session WITHOUT direct GitHub / repository access:**
   - Do not claim or imply that repository files, GitHub Issues, or pull-request states were read. Mark issue status as `ISSUE_STATUS_UNVERIFIED`.
   - Ask the user to grant repository access or paste/upload the relevant current file and issue contents.
   - Do not generate an implementation or execution prompt until current rules and state are verified.

## 3. Dynamic GitHub Discovery

When repository access is available, execute read-only discovery in this order at session start:

1. Target the repository `Tachiguro/KnownFirst`.
2. Read `AGENTS.md` and `docs/NEW_CHAT_BOOTSTRAP.md` from the current default branch.
3. Read `docs/PROMPT_AND_TASK_ROUTING.md` and `docs/INDEX.md`.
4. Query the real current default branch name and latest commit SHA.
5. Inspect open and relevant closed **GitHub Issues** (active issue, parent epic/roadmap issue, latest substantive comments, checklists, and linked PRs/commits).
6. Inspect open, recently closed, and recently merged pull requests.
7. Inspect relevant pull-request metadata (base branch, head branch, head SHA, commits, changed files, mergeability status, check results, reviews, and review threads).

## 4. Selecting the Active Work Package & Multi-Slice Resume

1. **GitHub Issues, PR State, and Local Task Branches:**
   - Inspect the active GitHub Issue (or epic) on `Tachiguro/KnownFirst` to determine the current planning status (`BACKLOG`, `TRIAGE`, `READY`, `IN_PROGRESS`, `REVIEW`, `BLOCKED`, `DONE`), acceptance checklist, and latest verified comment.
   - If an open pull request exists, inspect its head branch and read `docs/CURRENT_WORK.md` from that branch.
   - The absence of an open pull request on GitHub does **not** imply that no active local work package exists. A local task branch with unpushed checkpoint commits represents an active in-progress package.
   - Check local branch history (`git log`) for checkpoint trailers (`KnownFirst-Checkpoint:`) and correlate them with the declared slice list in `docs/CURRENT_WORK.md` per the resume contract in [docs/AGENT_WORKFLOW.md](AGENT_WORKFLOW.md).
2. **Default Branch Baseline:**
   - Read `docs/CURRENT_WORK.md` from the current default branch only when no active open pull request or in-progress task branch exists.
3. **Issue-First Tracking & Transitional Backlog Discovery:**
   - **GitHub Issues** on `Tachiguro/KnownFirst` are the primary tracking system for newly accepted tasks, bugs, governance packages, and their live status.
   - [docs/BACKLOG.md](BACKLOG.md) is preserved as the transitional registry for pre-existing `KF-*` items, open product decisions (`REQUIRES_PRODUCT_DECISION`), and historical provenance until open `KF-*` items are incrementally verified and migrated to GitHub Issues.
   - An agent must not select an open issue or backlog item or initiate an unapproved sequence on its own; task selection and prioritization require user or orchestrator confirmation.
4. Treat live Git/GitHub state as authoritative over stale operational prose in status files. Neither an Issue nor `docs/BACKLOG.md` is a substitute for verifying the real local worktree and Git history.
5. Never trust pasted SHAs, pull-request numbers, issue states, branch names, task names, or historical chat text without live verification.
6. Never repeat work that is already merged into the default branch.
7. Never create a duplicate pull request for a branch that already has an open or merged pull request, or duplicate an existing GitHub Issue without checking open and closed issues first.
8. Avoid selecting arbitrarily when several open issues or pull requests exist; ask the user for clarification if the primary active package remains ambiguous after inspection.

## 5. Local-State Verification

When the next operation depends on unpushed local commits, untracked files, local-only branches, worktrees, or local merge states:

- Request one short local `REVIEW_ONLY` prompt to verify local Git state, branch HEAD, checkpoint trailers, and working tree cleanliness before formulating the next action.
- Do not perform any GitHub or repository write operation merely because a new chat session was started.

## 6. Prompt-Generation Contract

When a local coding-agent prompt is required (after ChatGPT has completed all analysis it can perform directly), generated agent prompts must adhere to these rules:

- **Language:** User-facing orchestration explanations before a prompt are written in German (1–3 concise sentences). Programming-agent prompts and requested technical reports are written in English.
- **Dynamic Model & Effort Selection:** Select the least usage-intensive capable approved agent interface, model, and effort (`Low`, `Medium`, `High`, or justified `Extra High` on standalone agents) specified by [docs/PROMPT_AND_TASK_ROUTING.md](PROMPT_AND_TASK_ROUTING.md).
- **Three-Agent Comparison Table:** Before every programming-agent prompt or recommendation, include exactly one compact Markdown comparison table with these columns:
  - `Agent`
  - `Modell`
  - `Effort`
  - `Empfehlung`
  The table must list the three approved agent interfaces — `Anti-Gravity`, `ChatGPT Codex`, and `Standalone Claude Code` — once each, dynamically sorted by suitability and current availability for the concrete task:
  1. `**Bevorzugt**` (with a 2–6 word rationale)
  2. `**Alternative**` (with a 2–6 word rationale)
  3. `**Nicht verwenden**` (task-specific, with a 2–6 word rationale; or `Nicht verfügbar` if quota/host access is absent)
  Do not hardcode fixed provider exclusions or copy private model catalogs/quotas into tracked repository files.
- **Delegation:** Subagents, delegated writers, background tasks, or task trackers require explicit user authorization.
- **Prompt Framing (`TASK:` / `HARD STOP`):**
  - Produce at most one next scoped agent prompt per turn.
  - Display the exact `TASK: ...` first line visibly above the copy action (using a compact copyable prompt card when supported by the UI, or a single contiguous copyable fenced code block as fallback).
  - Begin every agent prompt on the first line with `TASK:`.
  - End every agent prompt on the last line with `HARD STOP` (do not use decorative start or end wrapper markers).
  - Never place prose, explanations, or follow-up questions after the prompt block.
  - Never place a nested fenced code block inside an agent prompt.
- **Accuracy:** Never claim repository or GitHub access when access is unavailable. Direct repository and GitHub validation outranks remembered chat text and pasted agent reports.

## 7. Pull-Request Lifecycle and Manual Merge Rule

- Pull requests are merged exclusively by the user manually through the GitHub interface.
- ChatGPT and automated agents never merge a pull request or enable auto-merge.
- Do not generate `MERGE_ONLY` prompts.
- After an approved final review, inform the user that the pull request is ready for manual merge on GitHub.
- After the user reports that the merge is complete, verify the merge status via GitHub.
- Once verified, generate exactly one `POST_MERGE_SYNC_ONLY` prompt to synchronize the local repository.
- Subagents, delegated writers, background processes, or task trackers require explicit user authorization.
- Never delete a merged branch automatically.

## 8. Distinguishing External Release and Testing Facts

- Live GitHub state is authoritative for repository, branch, commit, and pull-request status.
- External facts — including Google Play store availability, Internal Testing distribution, physical device installation, and user testing — cannot be inferred from GitHub repository state alone.
- Merging source code, compiling an AAB, uploading to Google Play, distributing to testers, installing on a device, and completing manual testing are distinct events. Never treat them as equivalent or assume one implies another.
- When the user corrects or confirms an external release or testing fact, durable project and release documentation (`docs/PROJECT_STATE.md`, `docs/BETA_TESTING.md`, `CHANGELOG.md`, `docs/releases/`) must be reconciled before unrelated feature work continues.
- At session bootstrap, distinguish three levels of evidence:
  1. GitHub and repository facts (authoritative for code, branches, and PRs);
  2. Durable external release records in `docs/releases/` and `docs/BETA_TESTING.md`;
  3. Unrecorded conversational claims or temporary local build artifacts.

## 9. Manual PowerShell Command Contract

Prefer GitHub or an authorized agent over asking the user to run manual PowerShell. Request a manual command only when no authorized automated path exists.

When manual execution is genuinely required:

- One simple command may be presented as one line.
- Two or more commands must be supplied as one self-contained PowerShell script.
- Scripts assume the repository path `C:\Dev\KnownFirst`.
- Scripts set `$ErrorActionPreference = 'Stop'`.
- Scripts validate preconditions before any mutation.
- Scripts abort on unexpected state.
- Scripts use exact paths and avoid wildcard or recursive deletion unless explicitly authorized.
- Scripts print a compact labeled summary.
- Scripts end with `Read-Host 'Press Enter to close'`.

## 10. Permanent Minimal User Prompt

Use this permanent, evergreen prompt fragment to start new sessions when GitHub access is available:

KnownFirst: Use GitHub and follow AGENTS.md plus docs/NEW_CHAT_BOOTSTRAP.md from the current default branch. Determine the real repository, active GitHub Issue, and pull-request state, perform ChatGPT-first analysis directly, and continue with only the next necessary step or scoped agent prompt. Do not merge anything.
