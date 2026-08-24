# KnownFirst New-Chat Bootstrap Protocol

## 1. Purpose and Authority

This protocol governs the initialization sequence for new ChatGPT and prompt-authoring sessions for the KnownFirst repository.

Live GitHub pull request and branch states are authoritative over static or pasted prompt text. Prompt authoring and session initialization must discover current repository state dynamically rather than relying on stale prompt fragments, old chat history, or hardcoded commit hashes.

Standing orchestration delegation, defined precisely in [docs/PROMPT_AND_TASK_ROUTING.md](PROMPT_AND_TASK_ROUTING.md), authorizes ChatGPT to issue the next correctly isolated prompt (`PLAN_ONLY`, `IMPLEMENT_SLICE`, `CHECKPOINT_COMMIT_ONLY`, `REVIEW_ONLY`, `DOCUMENT_ONLY`, `COMMIT_ONLY`, candidate-HEAD `FULL_VALIDATION`, `PUSH_ONLY`, `PR_ONLY`, `POST_MERGE_SYNC_ONLY`) without requiring the user to separately request each routine phase, provided live repository/GitHub state has been verified per this protocol and no unresolved material decision remains. It never authorizes merge, auto-merge, or any operation on that document's non-delegable-operations list; those always require explicit user action.

## 2. Repository-Access Capability Gate

When initializing a new session, distinguish repository access capability immediately:

1. **Session WITH direct GitHub / repository access:**
   - Follow the dynamic discovery sequence below.
   - Read configuration and status documents directly from the repository.
2. **Session WITHOUT direct GitHub / repository access:**
   - Do not claim or imply that repository files or pull-request states were read.
   - Ask the user to grant repository access or paste/upload the relevant current file contents.
   - Do not generate an implementation or execution prompt until current rules and state are verified.

## 3. Dynamic GitHub Discovery

When repository access is available, execute read-only discovery in this order at session start:

1. Target the repository `Tachiguro/KnownFirst`.
2. Read `AGENTS.md` and `docs/NEW_CHAT_BOOTSTRAP.md` from the current default branch.
3. Read `docs/PROMPT_AND_TASK_ROUTING.md` and `docs/INDEX.md`.
4. Query the real current default branch name and latest commit SHA.
5. Inspect open, recently closed, and recently merged pull requests.
6. Inspect relevant pull-request metadata (base branch, head branch, head SHA, commits, changed files, mergeability status, check results, reviews, and review threads).

## 4. Selecting the Active Work Package & Multi-Slice Resume

1. **GitHub PR State vs Local Task Branches:**
   - If an open pull request exists, inspect its head branch and read `docs/CURRENT_WORK.md` from that branch.
   - The absence of an open pull request on GitHub does **not** imply that no active work package exists. A local task branch with unpushed checkpoint commits represents an active in-progress package.
   - Check local branch history (`git log`) for checkpoint trailers (`KnownFirst-Checkpoint:`) and correlate them with the declared slice list in `docs/CURRENT_WORK.md` per the resume contract in [docs/AGENT_WORKFLOW.md](AGENT_WORKFLOW.md).
2. **Default Branch Baseline:**
   - Read `docs/CURRENT_WORK.md` from the current default branch only when no active open pull request or in-progress task branch exists.
3. Treat live Git/GitHub state as authoritative over stale operational prose in status files.
4. Never trust pasted SHAs, pull-request numbers, branch names, task names, or historical chat text without live verification.
5. Never repeat work that is already merged into the default branch.
6. Never create a duplicate pull request for a branch that already has an open or merged pull request.
7. Avoid selecting arbitrarily when several open pull requests exist; ask the user for clarification if the primary active package remains ambiguous after inspection.

## 5. Local-State Verification

When the next operation depends on unpushed local commits, untracked files, local-only branches, worktrees, or local merge states:

- Request one short local `REVIEW_ONLY` prompt to verify local Git state, branch HEAD, checkpoint trailers, and working tree cleanliness before formulating the next action.
- Do not perform any GitHub or repository write operation merely because a new chat session was started.

## 6. Prompt-Generation Contract

All generated agent prompts must adhere to these rules:

- **Language:** User-facing orchestration explanations before a prompt are written in German. Programming-agent prompts and requested technical reports are written in English.
- **Model Selection:** Use the least expensive capable model specified by `docs/PROMPT_AND_TASK_ROUTING.md`.
- **Speed:** Keep Speed at `Standard`. Omit Speed from the visible prompt header.
- **Recommendation Table:** Before every programming-agent prompt, require one Markdown comparison table with exactly these columns:
  - `Agent`
  - `Modell`
  - `Effort`
  - `Präferenz/Bewertung`
  The table must contain rows for `Anti-Gravity`, `Claude`, and `Codex` in that exact order. The recommended choice must be visibly marked as the best choice. These fixed rows are KnownFirst orchestration preferences, not runtime-availability guarantees; runtime availability and quota must be determined from the current session only when relevant.
- **Delegation:** Subagents, delegated writers, background tasks, or task trackers require explicit user authorization.
- **Prompt Framing:**
  - Produce exactly one next scoped agent prompt per turn.
  - Use exactly one continuous copyable fenced code block per agent prompt.
  - Begin every agent prompt exactly with `PROMPT START`.
  - End every agent prompt exactly with `PROMPT ENDE`.
  - Never place prose or explanations after the prompt block.
  - Never place a fenced code block inside an agent prompt.
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

KnownFirst: Use GitHub and follow AGENTS.md plus docs/NEW_CHAT_BOOTSTRAP.md from the current default branch. Determine the real repository and pull-request state, then continue with only the next scoped agent prompt. Do not modify or merge anything.
