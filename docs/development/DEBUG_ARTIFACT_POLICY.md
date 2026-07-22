# Debug artifact policy

`docs` stores durable findings, architecture decisions, and handoffs. `bin`
stores disposable build and diagnostic evidence. `artifacts` stores only the
current and immediately previous release AABs and their matching checksum
sidecars.

## Canonical raw-diagnostic path

All future raw diagnostics belong only under:

`bin/<Configuration>/<TargetFramework>/diagnostics/<IssueSlug>/<Timestamp>/`

Examples:

- `bin/Debug/net10.0-android/diagnostics/wikipedia-fallback/20260722-153000/`
- `bin/Release/net10.0-android/diagnostics/wikipedia-aot/20260722-160000/`
- `bin/Debug/net10.0-windows10.0.19041.0/diagnostics/lookup-ui/20260722-170000/`

Permitted temporary names include `app.log`, `logcat.txt`, `exception.txt`,
`screenshot-*.png`, `bugreport.zip`, `request-summary.json`, `test-notes.md`,
and probe outputs.

## Rules

- Never create diagnostics directly under `C:\Dev`, beside the repository, at the repository root, or under `artifacts`.
- Never commit diagnostic output; use synthetic data only.
- Do not log complete imported texts or definitions/translations by default.
- Treat screenshots and bugreports as potentially sensitive.
- Routine installation, execution, ADB, logcat, `pm clear`, app uninstallation, and data resets on physical devices are prohibited without explicit user authorization. Physical device testing is restricted to separate, explicitly authorized work packages.
- After fixing an issue, document only cause, solution, tests, and result; the matching `bin` diagnostic folder may then be deleted.
- `bin`, `obj`, `.vs`, and `TestResults` are fully regenerable.
- `artifacts` may retain at most the current and previous release AAB.

The existing ignore rules already cover the generated `bin`, `obj`, `.vs`, and
`TestResults` trees. Do not add broad ignore patterns; extend them only for a
specific canonical diagnostic path if a future repository change requires it.
