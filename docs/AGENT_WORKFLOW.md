# KnownFirst Agent Workflow

Git is the source of truth for KnownFirst development.

An agent receives the current branch and one concrete work package. Prompt formulation, model selection, and mode isolation are governed by [docs/PROMPT_AND_TASK_ROUTING.md](PROMPT_AND_TASK_ROUTING.md). Before modifying code or documentation, consult [docs/INDEX.md](INDEX.md) to read only the contracts relevant to the active task.

## Explicit Phase State Sequence

KnownFirst development follows a strict, isolated phase sequence. No programming agent self-advances between phases: the agent executing a given prompt performs only that prompt's mode. Whether ChatGPT may issue the *next* isolated prompt without separate per-phase user typing is governed by the standing orchestration delegation defined in [docs/PROMPT_AND_TASK_ROUTING.md](PROMPT_AND_TASK_ROUTING.md); the phase list below states the underlying gate each transition must satisfy.

1. **PLAN_ONLY:** Read-only analysis and proposal.
2. **Plan/Implement Transition:** Satisfied by explicit user approval of the presented plan, or by standing orchestration delegation when `PLAN_ONLY` is complete, bounded, and exposes no unresolved material decision (see [docs/PROMPT_AND_TASK_ROUTING.md](PROMPT_AND_TASK_ROUTING.md)).
3. **IMPLEMENT:** Minimum production change using focused TDD red/green loop (see [docs/TESTING.md](TESTING.md)).
4. **TEST_ONLY:** Scoped test execution when explicitly requested.
5. **DOCUMENT_ONLY:** Updating documentation for verified implementation when explicitly requested.
6. **Document/Commit Transition:** Inspection of uncommitted changes, satisfied by explicit user review or by standing orchestration delegation under the same conditions as step 2.
7. **COMMIT_ONLY:** Staging explicit files and committing to create the candidate commit (HEAD).
8. **Mandatory Pre-PR FULL_VALIDATION:** Running the complete `.\scripts\knownfirst.ps1 -Action ValidateAll` matrix on the exact candidate HEAD (`TEST_ONLY`). `PUSH_ONLY` and `PR_ONLY` are strictly blocked without this successful validation. Any subsequent repository-file modification or commit invalidates prior evidence and requires this gate to run again on the new HEAD.
9. **PUSH_ONLY:** Pushing approved branch and validated commit to remote repository.
10. **PR_ONLY:** Opening or updating a pull request containing exact-HEAD full-validation evidence.
11. **REVIEW_ONLY:** Read-only review of PR or diff.
12. **Correction Package:** Approved fixes for review findings (which start a new cycle and require full validation on the resulting HEAD).
13. **Explicit User Merge Decision:** Separate user-driven merge. Never delegable, regardless of standing orchestration delegation.
14. **POST_MERGE_SYNC_ONLY:** Fast-forward synchronizing local master after the user's verified manual GitHub merge. It does not authorize branch deletion, documentation changes, builds, tests, commits, pushes, or additional PR mutations.

### Phase Isolation Boundaries
- A prompt author may explicitly authorize a combination of modes, but the prompt must enumerate every included mode.
- **Agent-level isolation is absolute:** the agent executing a given prompt performs only that prompt's mode and never self-advances. Standing orchestration delegation (see [docs/PROMPT_AND_TASK_ROUTING.md](PROMPT_AND_TASK_ROUTING.md)) governs only whether ChatGPT may issue the next prompt; it never authorizes an agent to continue within one.
- Normal `IMPLEMENT` mode stops as soon as focused green tests pass. It does not automatically run full test suites, build platforms, update documentation, commit, push, or create PRs.
- Normal `TEST_ONLY` mode does not modify code or fix failures.
- Normal `DOCUMENT_ONLY` mode does not modify production or test code.
- Commit does not imply push. Push does not imply PR. PR does not imply merge. Merge does not imply build or package creation.

## Development and Testing Loop

1. **Focused TDD loop:** Write minimum focused tests first, confirm expected red failure, implement minimum code change, confirm focused tests pass green.
2. **No automatic broad validation:** Full test suite runs, Windows smoke tests, manual GUI tests, and platform builds are separate authorized operations. Refer to [docs/TESTING.md](TESTING.md) for test scopes and [docs/BUILD_AND_RELEASE.md](BUILD_AND_RELEASE.md) for build procedures.
3. **Single writing agent:** Only one writing agent may operate at a time in the repository. Subagents, delegated writers, background tasks, or task trackers require explicit user authorization.
4. **Direct verification outranks claims:** Direct repository and GitHub verification outranks remembered chat text and pasted agent reports. Technical reports from programming agents are written in English.

### TDD Evidence Requirement for Behavior-Changing IMPLEMENT Packages

Every `IMPLEMENT` package that changes product behavior must produce and report this exact evidence sequence:

1. Write the minimum focused test(s) first, identified by exact class/method/filter.
2. Run the exact focused scope (`FOCUSED_AUTOMATED`, see [docs/TESTING.md](TESTING.md)).
3. Record a genuine behavioral red result: the failure must be caused by missing intended behavior. **Not acceptable as red evidence:** a syntax error, a missing test file, a fixture mistake, an environment/tooling failure, or deliberately broken compilation. Any of these must be fixed and the red result re-obtained before proceeding.
4. Implement the minimum production change.
5. Rerun the identical focused scope to a green result.
6. Defer broader validation (`ALL_AUTOMATED`, `UI_CONTRACT_AUTOMATED`, GUI, platform builds) to separately authorized `TEST_ONLY` operations; do not run them automatically.

**Characterization and test-hardening packages** (adding tests when no production behavior is being changed, e.g. to protect a cleanup target) may add tests that pass immediately. Such a package must explicitly identify itself as characterization/hardening and must not claim a red/green implementation cycle occurred.

**Every `IMPLEMENT` report must state:**
- which user workflow is proven by the evidence produced;
- which runtime or platform behavior remains unproven by that evidence;
- whether the result is source-contract, component, rendered-GUI, platform, or manual evidence (source/markup inspection alone does not prove a runtime control is clickable or produces its intended effect — see [docs/TESTING.md](TESTING.md)).

**Every product/UI implementation plan must classify each new or changed control** as one of:
- implemented in all intended builds (Debug, BetaDiagnostic, Release);
- debug-diagnostic only (explicitly gated, absent from Release and from the Google Play AAB);
- documentation-only and absent from production rendering (a planned feature tracked in [docs/ROADMAP.md](ROADMAP.md) with no corresponding Release-visible element).

This section is additive to, and does not weaken, the existing operation-isolation model above.

## Review, Staging, and Git Operations

1. **Explicit staging:** Use explicit file paths (`git add <file1> <file2>`). Never use `git add .` or stage untracked scratch files.
2. **Conventional commits:** Use standard conventional commit prefixes (`feat:`, `fix:`, `docs:`, `test:`, `build:`, `chore:`).
3. **Authorized publication:** Pushing an approved branch (`PUSH_ONLY`) and creating or updating its pull request (`PR_ONLY`) for an established, bounded work package are covered by the standing orchestration delegation defined in [docs/PROMPT_AND_TASK_ROUTING.md](PROMPT_AND_TASK_ROUTING.md) and do not require a fresh per-operation user message. Both operations are strictly blocked until the exact candidate commit (HEAD) has successfully passed the mandatory pre-PR `FULL_VALIDATION` gate. However, prompt-level mode isolation remains absolute: an executing programming agent pushes only when its prompt is `PUSH_ONLY` and creates/updates a pull request only when its prompt is `PR_ONLY`. Auto-merge is strictly prohibited; PR merge is an explicit, non-delegable repository-owner action.
4. **Evidence-based claims:** Never claim physical device validation, visual acceptance, or manual verification without concrete empirical evidence.
