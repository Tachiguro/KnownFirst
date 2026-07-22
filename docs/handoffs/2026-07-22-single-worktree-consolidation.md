# Single-worktree consolidation handoff

**Date:** 2026-07-22  
**Baseline:** `master` at `8dafcea161350432da47d97bfb5ac1397f5d3f5e`

## Outcome

The project was consolidated to the single canonical folder
`C:\Dev\KnownFirst`. Additional worktrees and the redundant repository copy
were removed after their release evidence was retained. External diagnostic
folders and root logs were removed, and generated `bin`, `obj`, `.vs`,
`TestResults`, APK, and obsolete AAB output was purged.

Approximately 2.153 GiB was released. Git remained clean and no tracked source
or documentation file was changed by the cleanup.

## Retained release evidence

- Beta 8 AAB: `artifacts/android/google-play-internal/KnownFirst-1.0.0-beta.8-code8-google-play-internal.aab` — 37,163,924 bytes — SHA-256 `66528BE580127AA715D09CB0F580FCD603A11451F401D11E0C0D73883053D737`.
- Beta 7 AAB: `artifacts/android/google-play-internal/KnownFirst-1.0.0-beta.7-code7.aab` — SHA-256 `8DD2CAC243F0C2B9FCE1088146799E209817EE9D30B40DBA9F60E66A956314C3`.

## New operating rules

Use only the canonical folder, do not create worktrees without explicit
approval, allow only one writing agent, and store raw diagnostics exclusively
under the path defined in [DEBUG_ARTIFACT_POLICY.md](../development/DEBUG_ARTIFACT_POLICY.md).

## Next technical task

On a separate feature branch, remove iOS and Mac Catalyst from the project,
platform code, build configuration, tests, and documentation. Validate Windows
Debug and Android Debug/Release afterward. Wikipedia fallback and backup work
remain separate follow-up packages.
