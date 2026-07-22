# ADR-0006: Git worktrees isolate feature development

**Status:** Accepted
**Decision date:** 2026-07-22

## Context

KnownFirst development may contain valuable uncommitted local assets,
diagnostics, release work, or test experiments. Switching a shared working
directory between unrelated branches risks mixing changes, overwriting files,
and making cleanup unsafe.

## Decision

Use a purpose-specific Git branch in a separate worktree for each work package.
Treat the main repository and every pre-existing worktree as protected unless a
task explicitly places it in scope.

Inventory branch, HEAD, status, untracked files, and worktree registration
before edits. Remove a worktree or branch only in a separately authorized
cleanup after confirming that it is clean and no unique work remains.

## Consequences

- Feature changes remain isolated and easier to review.
- Dirty worktrees can coexist without branch switching.
- Disk usage and branch count increase until deliberate maintenance occurs.
- A branch being merged is insufficient evidence that its attached worktree is
  safe to remove.
- Documentation and handoffs must identify the intended worktree clearly
  without committing machine-specific absolute paths.

## Alternatives

- Reusing one checkout with frequent branch switching was rejected because it
  exposes unrelated local changes to checkout and cleanup operations.
- Stashing as the normal workflow was rejected because stashes are easy to
  forget and provide weak task ownership.
- Copying directories outside Git worktree management was rejected because
  branch/HEAD relationships and cleanup state become ambiguous.
