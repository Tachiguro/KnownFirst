# Generated-output and artifact policy

`docs` stores durable findings, architecture decisions, and task routing. Source code and tracked assets reside in standard project folders. Generated and transient files belong exclusively in git-ignored locations within the repository unless an explicit contract governs external paths (such as host signing secrets or platform application data).

## 1. Directory and Lifecycle Model

| Tree | Classification | Lifecycle & Retention Rules |
| :--- | :--- | :--- |
| `bin/`, `obj/` | Compiler outputs & intermediates | Fully regenerable; transient. Deleted by Standard Clean. |
| `KnownFirst.Core/bin/`, `obj/` | Core library build outputs | Fully regenerable; transient. Deleted by Standard Clean. |
| `KnownFirst.Tests/bin/`, `obj/` | Test project build outputs | Fully regenerable; transient. Deleted by Standard Clean. |
| `TestResults/` | Test runner output | Fully regenerable; transient. Deleted by Standard Clean. |
| `.vs/` | Visual Studio local IDE cache | Disposable IDE cache. Deleted by Deep Clean. |
| `artifacts/build/` | Packaging build staging | Disposable intermediate. Deleted by Standard Clean. |
| `artifacts/obj/` | Packaging MSBuild staging | Disposable intermediate. Deleted by Standard Clean. |
| `artifacts/gui-tests/windows/profiles/` | Synthetic GUI test profiles | Disposable synthetic databases & settings. Deleted by Standard Clean. |
| `artifacts/gui-tests/windows/runs/` | Windows GUI test evidence | Disposable run reports and captures. Deleted by Standard Clean. |
| `artifacts/gui-tests/android/runs/` | Android GUI test evidence | Disposable Appium run reports. Deleted by Standard Clean. |
| `artifacts/launcher-logs/` | Operational launcher logs | Bounded automatically to 10 newest completed logs on normal runs and Standard Clean. Active log is always preserved. Deep Clean removes all completed logs while preserving the active log. |
| `artifacts/launcher-state/` | Reusable launcher cache | Reusable task hash records. Preserved by Standard Clean; deleted by Deep Clean. |
| `artifacts/android-google-play/` | Release distributables | Distributable store bundles. Retains 2 newest verified release AABs + checksums. **Never deleted by Clean.** |
| `artifacts/windows-portable/` | Release distributables | Distributable unpackaged ZIPs + checksums. **Never deleted by Clean.** |
| `artifacts/windows-msix/` | Release distributables | Distributable MSIX packages + checksums. **Never deleted by Clean.** |

## 2. Canonical Raw-Diagnostic Path

All raw investigation diagnostics belong only under:

`bin/<Configuration>/<TargetFramework>/diagnostics/<IssueSlug>/<Timestamp>/`

Examples:
- `bin/Debug/net10.0-android/diagnostics/wikipedia-fallback/20260722-153000/`
- `bin/Release/net10.0-android/diagnostics/wikipedia-aot/20260722-160000/`
- `bin/Debug/net10.0-windows10.0.19041.0/diagnostics/lookup-ui/20260722-170000/`

Permitted temporary diagnostic filenames include `app.log`, `logcat.txt`, `exception.txt`, `screenshot-*.png`, `bugreport.zip`, `request-summary.json`, `test-notes.md`, and probe outputs.

After resolving an issue, document the cause, solution, tests, and result in the relevant documentation; the temporary diagnostic directory under `bin` may then be deleted.

## 3. Launcher Cleanup Action

The canonical launcher (`scripts/knownfirst.ps1`) provides fail-closed cleanup actions for regenerable outputs:

### A. Commands

- **Standard Clean:**
  ```powershell
  .\scripts\knownfirst.ps1 -Action Clean
  ```
- **Deep Clean:**
  ```powershell
  .\scripts\knownfirst.ps1 -Action Clean -Deep
  ```
- **Preview / Dry-run:**
  ```powershell
  .\scripts\knownfirst.ps1 -Action Clean -WhatIf
  .\scripts\knownfirst.ps1 -Action Clean -Deep -WhatIf
  ```

### B. Standard Clean Scope

Standard clean removes only explicit, regenerable relative paths derived from the repository root:
- `bin\`
- `obj\`
- `KnownFirst.Core\bin\`
- `KnownFirst.Core\obj\`
- `KnownFirst.Tests\bin\`
- `KnownFirst.Tests\obj\`
- `TestResults\`
- `artifacts\build\`
- `artifacts\obj\`
- `artifacts\gui-tests\windows\profiles\`
- `artifacts\gui-tests\windows\runs\`
- `artifacts\gui-tests\android\runs\`
- Prunes `artifacts\launcher-logs\` to the 10 newest completed logs, preserving the active log.

Missing directories are skipped without error. Standard Clean preserves `artifacts\launcher-state\`.

### C. Deep Clean Additions

Deep Clean adds:
- `.vs\`
- `artifacts\launcher-state\`
- Prunes all completed recognized launcher logs in `artifacts\launcher-logs\`, while safely preserving the currently active log.

### D. Excluded Historical Locations

Neither Standard nor Deep Clean automatically deletes unclassified or legacy historical folders:
- `artifacts\android\`
- `artifacts\android-beta\`
- `artifacts\diagnostics-export-import-audit\`
- `artifacts\gui-smoke\`
- `artifacts\recovery-verification\`

These locations require explicit review or separately authorized one-time cleanup.

## 4. Distributable Protection and Retention Contract

- `Clean` is not a package deletion command. There is no `-IncludeDistributables` or equivalent switch.
- Standard and Deep Clean do not target distributable roots (`artifacts\android-google-play\`, `artifacts\windows-portable\`, `artifacts\windows-msix\`) or release packages (`.aab`, `.apk`, `.zip`, `.msix`, `.sha256.txt`).
- **AAB Retention Semantics:** Retain strictly the two newest verified Google Play AABs (current and immediately preceding release) for release evidence and historical traceability. Old AABs are not kept for rollback purposes: Android/Google Play enforces monotonic version codes, and SQLite database schema migrations are forward-oriented, making binary downgrade an unsafe recovery strategy.

## 5. External Storage and Backup Policy

- **No Beside-Repository Clones or Archives:** KnownFirst development tooling must never create repository backup copies, recovery ZIPs, or loose diagnostic archives beside the repository in `C:\Dev` or sibling directories.
- **Diagnostics Isolation:** Diagnostics must not be dumped into `C:\Dev`, sibling folders, or the repository root.
- **External Boundaries:**
  - Host Signing Secrets: `%USERPROFILE%\KnownFirst-Secrets\` (keystores, passwords, thumbprints).
  - Platform User Data: `FileSystem.AppDataDirectory` (isolated OS application data).
  - Test Scratch Space: `%TEMP%` (isolated disposable test runners).

## 6. Privacy and Security Rules

- Never commit diagnostic outputs or databases; use synthetic data only in tests.
- Do not log complete imported texts or definitions/translations by default.
- Treat screenshots and bug reports as potentially sensitive.
- Routine installation, execution, ADB, logcat, `pm clear`, app uninstallation, and data resets on physical devices are prohibited without explicit user authorization.
