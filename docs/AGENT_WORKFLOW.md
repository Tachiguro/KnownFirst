# KnownFirst Agent Workflow

Git is the source of truth for KnownFirst development.

An agent receives the current branch and one concrete work package. Prompt formulation, model selection, and mode isolation are governed by [docs/PROMPT_AND_TASK_ROUTING.md](PROMPT_AND_TASK_ROUTING.md). Before modifying code or documentation, consult [docs/INDEX.md](INDEX.md) to read only the contracts relevant to the active task.

## Explicit Phase State Sequence

KnownFirst development follows a strict, user-authorized phase sequence. No phase starts automatically.

1. **PLAN_ONLY:** Read-only analysis and proposal.
2. **User Plan Approval:** Explicit user approval of the presented plan.
3. **IMPLEMENT:** Minimum production change using focused TDD red/green loop (see [docs/TESTING.md](TESTING.md)).
4. **TEST_ONLY:** Scoped test execution when explicitly requested.
5. **DOCUMENT_ONLY:** Updating documentation for verified implementation when explicitly requested.
6. **User Review:** Inspection of uncommitted changes.
7. **COMMIT_ONLY:** Staging explicit files and committing.
8. **PUSH_ONLY:** Pushing approved branch and commit to remote repository.
9. **PR_ONLY:** Opening or updating a pull request.
10. **REVIEW_ONLY:** Read-only review of PR or diff.
11. **Correction Package:** Approved fixes for review findings.
12. **Explicit User Merge Decision:** Separate user-driven merge.
13. **SYNC_ONLY:** Synchronizing local master to merged remote master.

### Phase Isolation Boundaries
- A prompt author may explicitly authorize a combination of modes, but the prompt must enumerate every included mode.
- Normal `IMPLEMENT` mode stops as soon as focused green tests pass. It does not automatically run full test suites, build platforms, update documentation, commit, push, or create PRs.
- Normal `TEST_ONLY` mode does not modify code or fix failures.
- Normal `DOCUMENT_ONLY` mode does not modify production or test code.
- Commit does not imply push. Push does not imply PR. PR does not imply merge. Merge does not imply build or package creation.

## Development and Testing Loop

1. **Focused TDD loop:** Write minimum focused tests first, confirm expected red failure, implement minimum code change, confirm focused tests pass green.
2. **No automatic broad validation:** Full test suite runs, Windows smoke tests, manual GUI tests, and platform builds are separate authorized operations. Refer to [docs/TESTING.md](TESTING.md) for test scopes and [docs/BUILD_AND_RELEASE.md](BUILD_AND_RELEASE.md) for build procedures.
3. **Single writing agent:** Only one writing agent may operate at a time in `C:\Dev\KnownFirst`.

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
3. **Authorized publication:** Push branches and create pull requests only when explicitly authorized. Auto-merge is strictly prohibited.
4. **Evidence-based claims:** Never claim physical device validation, visual acceptance, or manual verification without concrete empirical evidence.
