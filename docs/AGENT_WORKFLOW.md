# KnownFirst Agent Workflow

Git is the source of truth for KnownFirst development.

An agent receives the current branch and one concrete work package. Prompt formulation, model selection, mode isolation, and standing orchestration delegation are governed by [docs/PROMPT_AND_TASK_ROUTING.md](PROMPT_AND_TASK_ROUTING.md). Before modifying code or documentation, consult [docs/INDEX.md](INDEX.md) to read only the contracts relevant to the active task.

## Multi-Slice Feature Package Lifecycle

KnownFirst development organizes related, bounded implementation slices into coherent feature packages. Each package executes on a dedicated task branch.

### Normal Code/Behavior Package Lifecycle

```
PLAN_ONLY (declare coherent objective & ordered slices)
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

Documentation-only packages omit code implementation:

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

## Checkpoint Commit Contract

A **checkpoint commit** records one completed, focused-green implementation slice on the task branch:

1. **Prerequisites:**
   - The slice's minimum focused tests pass GREEN after genuine behavioral RED.
   - Any targeted extra tests required by the slice's risk classification pass.
   - `git diff --check` passes cleanly.
2. **Execution & Staging:**
   - Stage using explicit file paths only (`git add <file1> <file2>`). Never use `git add .` or wildcard staging.
   - Create exactly one commit with a conventional subject and the mandatory checkpoint trailer:
     `KnownFirst-Checkpoint: <package-id> <slice-index>/<slice-count> <short-scope>`
3. **Boundaries:**
   - A checkpoint commit does **not** trigger broad automated test suites, `ValidateAll`, package-level review, package-level documentation reconciliation, push, PR, or merge.
   - Mid-package documentation edits in a checkpoint commit are permitted **only** when a slice establishes a durable contract that subsequent slices in the same package must obey.
4. **Candidate Finalization Semantics:**
   - The presence of a checkpoint trailer does **not** disqualify a commit from becoming the final candidate.
   - If consolidated `REVIEW_ONLY` is approved and package-level `DOCUMENT_ONLY` reports `DOCUMENTATION_CURRENT_NO_CHANGES`, the latest checkpoint commit becomes the candidate HEAD and proceeds directly to `FULL_VALIDATION`.
   - If `DOCUMENT_ONLY` produces documentation changes, those changes are committed via `COMMIT_ONLY` and that resulting commit becomes the candidate HEAD.
   - Do **not** create an empty commit or use `git commit --allow-empty` merely to create an artificial candidate marker.

## Interruption and Resume Contract

When a session ends, resets, or is interrupted mid-package:

1. **Authoritative Sources:**
   - Git checkpoint commit history and trailers on the local task branch are the durable record of completed slices.
   - `docs/CURRENT_WORK.md` owns the active package objective and declared slice list while the package is active.
2. **Session Bootstrap & Discovery:**
   - A new session must inspect Git/GitHub state first per [docs/NEW_CHAT_BOOTSTRAP.md](NEW_CHAT_BOOTSTRAP.md).
   - The absence of an open PR on GitHub does **not** mean there is no active work; unpushed checkpoint commits on a local task branch represent an active local package.
   - The orchestrator reads `git log` on the task branch to match checkpoint trailers against the declared slice list in `docs/CURRENT_WORK.md`.
3. **Resumption Rules:**
   - Resume execution directly with the next incomplete declared slice.
   - If untracked/unstaged modifications exist, if checkpoint trailers have gaps or mismatches, or if `origin/master` has advanced, request a read-only `REVIEW_ONLY` prompt to reconcile state before writing new code.
   - Do **not** create a premature pull request merely to provide visibility across chat sessions.

## Development, Testing, and Risk Rules per Slice

### 1. Focused TDD Evidence Requirement
Every behavior-changing slice must produce and report this evidence sequence:
- Write or update the minimum focused test(s) first (`FOCUSED_AUTOMATED`, see [docs/TESTING.md](TESTING.md)).
- Run the exact focused scope to record genuine behavioral RED (missing intended behavior). Syntax errors, missing test files, fixture errors, environment failures, or broken compilation are **not** acceptable as RED evidence.
- Implement the minimum production change.
- Rerun the identical focused scope to a GREEN result.
- Defer broader validation (`ALL_AUTOMATED`, `UI_CONTRACT_AUTOMATED`, GUI tests, platform builds) to candidate `FULL_VALIDATION` or separate explicitly authorized `TEST_ONLY` operations.

*Characterization and test-hardening packages* (adding tests without changing behavior) are the sole exception and may pass immediately.

### 2. Risk and Escalation Categories
- **Irreversible / Persisted-State Risk:** Schema migrations, archive format/DTO compatibility changes, and destructive reset behaviors must be classified in `PLAN_ONLY` and backed by targeted contract tests. When practical, schedule them late in the package sequence. If multiple independent irreversible transitions make reasoning difficult or unsafe, split into separate packages.
- **Targeted Extra Testing:** Persistence/migration slices run relevant migration contracts; backup/archive slices run relevant round-trip contracts; destructive reset slices run relevant data-safety contracts. Broad `ValidateAll` is not run per slice.
- **AOT / Trimming Sensitivity:** Must be identified in `PLAN_ONLY`. Early Android Release compilation may be recommended when justified, but is not universally mandatory per slice since final `ValidateAll` remains fail-closed.
- **Package Coherence:** All slices in a package must support a single, coherent product or engineering objective. Unrelated work must not be bundled merely to amortize lifecycle overhead. Coherence is semantic; it is not evaluated by arbitrary title heuristics.
- **Validation-Gate Self-Modification:** Any change to `ValidateAll`, its required step list, or launcher scripts defining gate behavior must be an isolated package and never bundled with product features.
- **Security and Privacy:** Security, privacy, permission, key, or consent boundary changes must be classified in `PLAN_ONLY` and separately enumerated in the consolidated `REVIEW_ONLY` report.

## Review, Staging, Full-Validation, and Git Operations

1. **Consolidated Review:** Once all declared slices are completed, an independent `REVIEW_ONLY` inspects the full branch diff against `master`. Review findings requiring corrections are addressed via correction slices and re-reviewed.
2. **Package-Level Documentation Reconciliation:** Mandatory `DOCUMENT_ONLY` reconciles durable documentation (`docs/`, `CHANGELOG.md`) once for the entire package.
   - **Follow-Up Closure Audit:** Every package-level `DOCUMENT_ONLY` phase must perform a Follow-Up Closure Audit: every intentionally deferred, excluded, newly discovered, or downstream accepted item must either link to an existing open `KF-*` ID in `docs/BACKLOG.md` or be added to `docs/BACKLOG.md` before package reconciliation is declared complete. Unfinished accepted work must never exist solely in chat history, review comments, or transient prompt notes.
   - **Foundation Milestone Invariant:** Merging a foundation or sub-package (such as a core scheduling engine or domain model) must never cause its parent initiative or roadmap milestone to be marked complete while required downstream packages (persistence, archive, runtime cutover, UI) remain open. Downstream packages must be tracked as open work in `docs/BACKLOG.md`.
3. **Mandatory Pre-PR FULL_VALIDATION Gate:** Before pushing or opening a PR, the exact candidate HEAD must successfully pass:
   ```powershell
   .\scripts\knownfirst.ps1 -Action ValidateAll
   ```
   Any subsequent commit or file edit invalidates this evidence and requires `ValidateAll` to run again on the new candidate HEAD.
4. **Authorized Publication:** Pushing an approved branch (`PUSH_ONLY`) and opening/updating its pull request (`PR_ONLY`) are covered by standing orchestration delegation once exact-HEAD validation passes.
5. **Manual PR Merge Only:** Pull requests are merged exclusively by the repository owner manually through GitHub. Automated agents never merge PRs or enable auto-merge.
6. **Post-Merge Synchronization:** After verified manual merge, local `master` is synchronized using `POST_MERGE_SYNC_ONLY` (`git merge --ff-only origin/master`). No branch deletion or code modification occurs during sync.
7. **Direct Verification Outranks Claims:** Direct repository and GitHub verification outranks remembered chat text and pasted agent reports. Technical reports from programming agents are written in English.
