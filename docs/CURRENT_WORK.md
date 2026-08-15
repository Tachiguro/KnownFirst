# KnownFirst Current Work

## Last updated

2026-08-15 (Package 3: Documentation hygiene, status compaction, obsolete-document removal, and checkout-path governance reconciliation active on `docs/documentation-hygiene-and-path-governance-v1`; PR #110 artifact lifecycle and PR #111 script organization/path portability are merged to `master`; `POST_MERGE_SYNC_ONLY` completed exactly once for each; source identity on `master` is `1.0.0-beta.13` / build 13; Final Release AAB is NOT AUTHORIZED / NOT CREATED; Windows portable ZIP/MSIX packaging infrastructure is merged on `master`; no real ZIP/MSIX package has yet been produced; Partner Center Store identity inputs remain unresolved template placeholders).

## Repository and Worktree Governance

- Repository: https://github.com/Tachiguro/KnownFirst.git
- Canonical local working directory: Exactly one canonical local checkout and one normal worktree per environment (defaulting to `C:\Dev\KnownFirst`, see [ADR-0007](decisions/ADR-0007-single-canonical-working-directory.md)).
- Single writer: Only one writing agent operates at a time in the repository.
- Single worktree: Additional worktrees or repository copies require explicit user authorization.

## Active Work Package

- **Active branch:** `docs/documentation-hygiene-and-path-governance-v1`
- **Work package:** Repository cleanup — Package 3 (documentation hygiene, status compaction, obsolete-document removal, checkout-path governance reconciliation).
- **Previous merged packages:**
  - PR #110 (`chore: add safe artifact cleanup`): Launcher Clean action, log pruning, and artifact safety. `POST_MERGE_SYNC_ONLY` completed.
  - PR #111 (`chore: organize scripts and improve portability`): Organized scripts under `packaging/`, `validation/`, and `tools/`; runtime path portability via `$PSScriptRoot` / `__file__`. `POST_MERGE_SYNC_ONLY` completed.

## Verified Baseline & Release Boundaries

- **Authoritative `master` baseline:** Discover current `master` dynamically per [docs/NEW_CHAT_BOOTSTRAP.md](NEW_CHAT_BOOTSTRAP.md). Prior PR merge commits (PR #92 through PR #111) and their `POST_MERGE_SYNC_ONLY` completions are historical facts.
- **Source identity:** `1.0.0-beta.13` (build 13) merged on `master` via PR #92. Merging is not a packaging, signing, or distribution event.
- **Confirmed external distribution:** `1.0.0-beta.12` / build 12 (Google Play Internal Testing, confirmed 2026-07-30). No newer Android external distribution has occurred.
- **Database schema:** SQLite `PRAGMA user_version` 10 on `master`.
- **Supported platforms:** Android (Google Play Internal Testing) and Windows development/verification. iOS and Mac Catalyst remain removed.
- **Mandatory Pre-AAB Release-Readiness Gate:** Not yet established on a post-merge candidate HEAD; remains strictly required before any future AAB packaging.
- **Final Release AAB:** **NOT AUTHORIZED / NOT CREATED**.
- **Windows distribution packaging:** Infrastructure merged on `master` (`scripts/packaging/publish-windows-portable.ps1`, `scripts/packaging/publish-windows-msix.ps1`); real runtime packaging, signing, and distribution remain separate, unexecuted milestones.

## Exact Next Action

- Complete Package 3 documentation hygiene and path-governance reconciliation through the isolated lifecycle (`DOCUMENT_ONLY` → `COMMIT_ONLY` → mandatory candidate-HEAD `FULL_VALIDATION` → `PUSH_ONLY` → `PR_ONLY` → manual user merge → `POST_MERGE_SYNC_ONLY`).
- Following Package 3 completion, repository returns to an idle, clean state on `master` awaiting subsequent authorized tasks (e.g. external cleanup or Beta-13 release gate preflight).
