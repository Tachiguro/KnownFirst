# ADR-0007: Single canonical working directory

**Status:** Accepted
**Decision date:** 2026-07-24

## Context

ADR-0006 previously required creating a separate Git worktree for every work package. In practice, managing multiple worktrees across autonomous agent sessions introduced operational overhead, directory confusion, and orphaned worktree registrations. Standardizing on a single canonical repository folder with disciplined branch management provides a cleaner and safer operating model.

## Decision

1. `C:\Dev\KnownFirst` is the single canonical working directory for all KnownFirst development.
2. Creating additional Git worktrees or repository copies requires explicit user approval.
3. Only one writing agent may operate at a time in the repository.
4. Pre-existing local work and uncommitted changes are treated as protected.
5. Branch switching must occur only from a verified clean working tree state.
6. Destructive Git operations (such as `git clean`, destructive reset, stash, rebase, amend, history rewriting, or force-push) are strictly prohibited unless authorized by an explicit recovery task.
7. A separate worktree remains an exceptional, explicitly authorized tool rather than the default workflow.

## Consequences

- All agents operate in a predictable, single-folder environment (`C:\Dev\KnownFirst`).
- Working tree status, HEAD, and active branch inspection are centralized and unambiguous.
- Requires strict verification of clean working tree state before switching branches.
- Eliminates orphaned worktree clutter and disk fragmentation.

## Alternatives

- **Worktree per feature branch (ADR-0006):** Superseded. Caused operational complexity and state tracking issues across multiple agent runs.
- **Stashing as standard workflow:** Rejected because stashes are easy to lose or misapply across sessions.
