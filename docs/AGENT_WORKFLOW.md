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

## Review, Staging, and Git Operations

1. **Explicit staging:** Use explicit file paths (`git add <file1> <file2>`). Never use `git add .` or stage untracked scratch files.
2. **Conventional commits:** Use standard conventional commit prefixes (`feat:`, `fix:`, `docs:`, `test:`, `build:`, `chore:`).
3. **Authorized publication:** Push branches and create pull requests only when explicitly authorized. Auto-merge is strictly prohibited.
4. **Evidence-based claims:** Never claim physical device validation, visual acceptance, or manual verification without concrete empirical evidence.
