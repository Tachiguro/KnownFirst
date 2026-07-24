# KnownFirst Agent Workflow

Git is the source of truth for KnownFirst development.

An agent receives the current branch and one concrete work package. It must consult [docs/INDEX.md](INDEX.md) to read only the documentation relevant to the current task before modifying code or documentation.

## Work Package Boundaries

1. **One coherent vertical slice per package:** Deliver complete, focused functionality within a single bounded package. Do not create speculative abstractions or unrequested infrastructure.
2. **Task-specific reading:** Do not reconstruct full repository context or read unrelated architecture docs, audits, or handoffs.
3. **No routine extra artifacts:** Do not create new handoffs, audits, implementation plans, or extra guide documents unless explicitly instructed.

## Development and Testing Loop

1. **Establish existing behavior:** Verify behavior from code and unit tests.
2. **Focused test execution:** Select approximately 5 to 8 decisive test groups matching modified code. Run focused tests during development to verify affected behavior quickly.
3. **No repeated full-suite runs:** Do not run full test suites repeatedly during active code editing when relevant inputs have not changed.
4. **Final validation gate:** Execute the complete relevant test suite once against the stable final commit before opening a PR or finalizing the package.
5. **Targeted builds:** Run only the platform builds required by the affected target or explicit task. For build procedures and AAB/packaging instructions, see [docs/BUILD_AND_RELEASE.md](BUILD_AND_RELEASE.md).
6. **Automated test boundaries:** Automated tests must use temporary isolated SQLite databases, fake clocks, offline fixtures, and mock HTTP handlers. Never execute live Wikimedia network requests or touch a real user database in tests.

## Documentation and Release State Updates

Update documentation in one bundled pass after stable code implementation:

- **docs/CURRENT_WORK.md:** Update operational task state for normal implementation packages.
- **CHANGELOG.md:** Update only for user-visible behavioral changes.
- **Contract / Architecture documents:** Update only when an underlying contract or durable principle changes.
- **docs/PROJECT_STATE.md:** Update only for verified global state or completed milestone changes.
- **docs/ROADMAP.md:** Update only for material sequencing or milestone status changes.

## Review, Staging, and Publication

1. **Review:** Audit diffs for scope, safety, localization, performance, AOT/trimming safety, and secrets.
2. **External review limit:** Normal packages undergo one external review pass and at most one consolidated correction pass for routine findings. Additional review passes occur only for explicit high-risk findings.
3. **Explicit staging:** Use explicit staging (`git add <file1> <file2>`). Do not use `git add .` or stage untracked scratch files.
4. **Clean commits:** Create clear commits using standard conventional prefixes (`feature/`, `fix/`, `docs/`, `build/`, `test/`, `chore/`).
5. **Authorized push and PR:** Push branches and create pull requests only when explicitly authorized. Auto-merge is strictly prohibited.
6. **Accurate claims:** Never claim physical device validation, visual acceptance, or manual verification without concrete empirical evidence.
