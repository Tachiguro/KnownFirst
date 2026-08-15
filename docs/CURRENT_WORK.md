# KnownFirst Current Work

## Last updated

2026-08-16 (Durable documentation reconciliation active on `docs/windows-portable-package-evidence-v1`; PR #114 packaging isolation hardening & logging fix merged to `master` with `POST_MERGE_SYNC_ONLY` completed exactly once; first real self-contained Windows Portable Release package `KnownFirst-1.0.0-beta.13-build13-win-x64-9e455d0.zip` successfully produced and verified on `master` at commit `9e455d0e03494cac8e713cd4d16c66946124f852`; no real MSIX created; no clean-PC launch/install verified; Beta-13 Android Final Release AAB remains NOT AUTHORIZED / NOT CREATED; Pre-AAB gate remains pending).

## Repository and Worktree Governance

- Repository: https://github.com/Tachiguro/KnownFirst.git
- Canonical local working directory: Exactly one canonical local checkout and one normal worktree per environment (defaulting to `C:\Dev\KnownFirst`, see [ADR-0007](decisions/ADR-0007-single-canonical-working-directory.md)).
- Single writer: Only one writing agent operates at a time in the repository.
- Single worktree: Additional worktrees or repository copies require explicit user authorization.

## Active Work Package

- **Active branch:** `docs/windows-portable-package-evidence-v1`
- **Work package:** Durable documentation reconciliation of the first verified Windows Portable package.
- **Previous merged packages:**
  - PR #113 (`fix: preserve git exit codes in windows packaging`): Corrected native Git exit-code capture in packaging scripts under Windows PowerShell 5.1. `POST_MERGE_SYNC_ONLY` completed.
  - PR #114 (`fix: harden windows packaging isolation and logs`): `<DefaultItemExcludes>` ordinary `bin\**;obj\**` protection against `BLAZOR102` during redirected builds and synchronous launcher output capture surviving terminating child exceptions. `POST_MERGE_SYNC_ONLY` completed.

## Verified Baseline & Release Boundaries

- **Authoritative `master` baseline:** Synchronized `master` at `9e455d0e03494cac8e713cd4d16c66946124f852`. Prior PR merge commits (through PR #114) and their `POST_MERGE_SYNC_ONLY` completions are historical facts.
- **Source identity:** `1.0.0-beta.13` (build 13) merged on `master` via PR #92. Merging is not a packaging, signing, or distribution event.
- **Confirmed external distribution:** `1.0.0-beta.12` / build 12 (Google Play Internal Testing, confirmed 2026-07-30). No newer Android external distribution has occurred.
- **Database schema:** SQLite `PRAGMA user_version` 10 on `master`.
- **Supported platforms:** Android (Google Play Internal Testing) and Windows development/verification. iOS and Mac Catalyst remain removed.
- **Windows distribution packaging:** A real self-contained Windows Portable Release ZIP (`KnownFirst-1.0.0-beta.13-build13-win-x64-9e455d0.zip`) and matching SHA-256 sidecar were produced and verified on `master` on 2026-08-16. Clean-PC runtime execution, MSIX packaging, and external distribution remain separate unexecuted milestones.
- **Mandatory Pre-AAB Release-Readiness Gate:** Not yet established on a post-merge candidate HEAD; remains strictly required before any future AAB packaging.
- **Final Release AAB:** **NOT AUTHORIZED / NOT CREATED**.

## Exact Next Action

- Complete documentation reconciliation through the isolated lifecycle (`DOCUMENT_ONLY` → `REVIEW_ONLY` → `COMMIT_ONLY` → mandatory exact-HEAD `FULL_VALIDATION` → `PUSH_ONLY` → `PR_ONLY` → manual user merge → `POST_MERGE_SYNC_ONLY`).
- Following documentation reconciliation, repository returns to an idle, clean state on `master` awaiting subsequent authorized tasks.
