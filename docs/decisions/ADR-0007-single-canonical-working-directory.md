# ADR-0007: Single canonical working directory

**Status:** Accepted (Amended 2026-08-15)
**Decision date:** 2026-07-24
**Amendment date:** 2026-08-15

## Context

ADR-0006 previously required creating a separate Git worktree for every work package. In practice, managing multiple worktrees across autonomous agent sessions introduced operational overhead, directory confusion, and orphaned worktree registrations. Standardizing on a single canonical repository folder with disciplined branch management provides a cleaner and safer operating model.

## Decision (Original: 2026-07-24)

1. A single canonical working directory is used for all KnownFirst development (initially standardized as `C:\Dev\KnownFirst`).
2. Creating additional Git worktrees or repository copies requires explicit user approval.
3. Only one writing agent may operate at a time in the repository.
4. Pre-existing local work and uncommitted changes are treated as protected.
5. Branch switching must occur only from a verified clean working tree state.
6. Destructive Git operations (such as `git clean`, destructive reset, stash, rebase, amend, history rewriting, or force-push) are strictly prohibited unless authorized by an explicit recovery task.
7. A separate worktree remains an exceptional, explicitly authorized tool rather than the default workflow.

## Amendment: Runtime Path Portability & Configurable Checkout Location (2026-08-15)

Following PR #111 (script organization and runtime path portability), the repository tooling derives repository-relative paths dynamically using `$PSScriptRoot` and `__file__`, eliminating hardcoded path dependencies.

1. **Single canonical checkout:** The repository operates from exactly one canonical local checkout and one normal worktree per development environment.
2. **Configurable absolute location:** The single checkout may reside at any valid absolute filesystem path (e.g. `C:\Dev\KnownFirst` or another user-selected location) without source code alterations. The former fixed literal `C:\Dev\KnownFirst` requirement was a historical convention, not a permanent architectural constraint.
3. **Tooling independence:** Repository scripts and developer tools must not depend on fixed clone paths or caller working directories for repository-internal assets.
4. **Scope boundaries:** This amendment does not alter single-worktree governance, the single-writing-agent rule, application data paths, SQLite database locations, signing-secret storage, package output roots, or distribution contracts. Additional checkouts or worktrees remain strictly prohibited without explicit user authorization.

## Consequences

- All agents operate in a predictable, single-checkout environment.
- Working tree status, HEAD, and active branch inspection are centralized and unambiguous.
- Requires strict verification of clean working tree state before switching branches.
- Eliminates orphaned worktree clutter and disk fragmentation while supporting arbitrary local clone paths.

## Alternatives

- **Worktree per feature branch (ADR-0006):** Superseded. Caused operational complexity and state tracking issues across multiple agent runs.
- **Stashing as standard workflow:** Rejected because stashes are easy to lose or misapply across sessions.
- **Hardcoded absolute checkout path requirement:** Rejected/Superseded by PR #111 runtime portability; replaced with configurable single canonical checkout.
