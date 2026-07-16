# KnownFirst repository instructions

Before changing application behavior, read completely:

- `docs/KNOWNFIRST_ARCHITECTURE.md`
- `docs/MVP_WORKFLOW.md`

Treat both documents as binding product and architecture specifications.

General rules:

- Use the name `KnownFirst` exclusively.
- Preserve the stable Windows and Android foundation.
- Keep code, comments, logs, tests, documentation, and commit messages in English.
- Reuse existing entities and services before creating new representations.
- Do not add speculative features outside the current task.
- Do not commit, push, merge, or force-push unless the task explicitly requests it.
- Do not expose DEBUG diagnostics in Release.
- Do not run GUI automation, Release builds, Android builds, or emulator tests unless the task explicitly requests them.
- For normal implementation tasks, run automated unit/integration tests and the Windows Debug build.
- Never use live Wikimedia requests in automated tests.
- Preserve original imported text and exact coordinate invariants.
- No Wikimedia API key is required for normal read-only lookup.
