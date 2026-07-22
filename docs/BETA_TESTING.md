# KnownFirst Android beta and diagnostic testing

## Scope

The Android test packages are directly installable signed APKs for focused manual validation. Automated tests and builds do not prove runtime behavior on a physical device. GUI automation, emulator testing, and broad device testing are outside the package-publish command.

Current identities:

- normal Release: `com.tachiguro.knownfirst`, `0.1.0-beta.3`
- Release-equivalent diagnostic: `com.tachiguro.knownfirst.diagnostic`, `0.1.0-beta.3-diagnostic`
- standalone Debug: `com.tachiguro.knownfirst.debug`, `0.1.0-beta.3-debug`
- application version: `3`
- minimum Android version: Android 7.0 (API 24)

The diagnostic package keeps Release optimization, trimming, AOT, and embedded assemblies, while adding symbols and the bounded lexical diagnostic actions. The Debug package embeds assemblies and disables Fast Deployment so that its APK can run without Visual Studio. The three package IDs allow side-by-side installation.

## Signing identity

The beta signing identity lives outside the repository:

- `%USERPROFILE%\KnownFirst-Secrets\knownfirst-beta.keystore`
- `%USERPROFILE%\KnownFirst-Secrets\knownfirst-beta-signing-password.txt`

Never commit either file and never generate a replacement while the existing keystore is usable. Back up the keystore and password together in a secure location. Losing the keystore prevents an APK from updating an installed beta signed with that identity. Exposing the keystore or password compromises the update identity and requires an explicit incident response.

The password may instead be supplied through `KNOWNFIRST_ANDROID_SIGNING_PASSWORD`. The publishing script does not print it.

## One-command publish

From the repository root run:

```powershell
.\scripts\publish-android-test-packages.ps1
```

The script publishes `KnownFirst.csproj` for `net10.0-android` in Release, BetaDiagnostic, and Debug. It signs every APK with the external beta keystore, verifies every signature with Android SDK `apksigner`, and writes an APK, SHA-256 file, and ZIP for each base name:

- `artifacts/android-beta/KnownFirst-0.1.0-beta.3-android-release.*`
- `artifacts/android-beta/KnownFirst-0.1.0-beta.3-android-diagnostic.*`
- `artifacts/android-beta/KnownFirst-0.1.0-beta.3-android-debug.*`

Each ZIP contains only its APK and `INSTALLATION.txt`. The `artifacts` directory and Android package/signing files are ignored by Git. `publish-android-beta.ps1` remains as a compatibility entry point and invokes the same three-package script.

## Diagnostic log

Debug and diagnostic builds add these localized actions to Settings:

- Copy diagnostic report / Diagnosebericht kopieren
- Export diagnostic log / Diagnoselog exportieren
- Clear diagnostic log / Diagnoselog löschen

The bounded log records timestamps, build and app version, lookup phases, the normalized lookup term, explicit language/mode/provider metadata, cache/HTTP/parser outcomes, and sanitized exception details. It excludes imported documents, contexts, definitions, credentials, HTTP headers, and secrets. The normal Release build does not expose the diagnostic actions or Android diagnostic log output.

## Manual checks

On an authorized Android device, record results separately for normal Release, diagnostic, and Debug. Verify at least:

1. The APK installs and KnownFirst launches without an immediate crash.
2. English/German UI switching remains immediate and persists after restart.
3. Import source language, lookup mode, and target language behave independently from UI language.
4. `Contact` and `Information` use lowercase dictionary lookup while their original context remains exact; `IT` remains case-sensitive.
5. Manual entry opens after NotFound and transient failure, accepts acronym-only, translation-only, or definition-only content, and advances once.
6. Back/Home pauses preparation and Home offers Continue preparation.
7. Cancelling a partially completed batch requires confirmation, retains accepted cards, and returns unresolved/skipped words to the backlog.
8. Android Back, safe areas, responsive Review/Preparation layouts, theme changes, and clipboard import still behave correctly.
9. The same automatic online dictionary lookup completes in all three packages without terminating the process.
10. In the diagnostic package, copy and export the report after a lookup and verify that phase, cache, HTTP, and parser metadata are present without imported document text, context text, definitions, credentials, or headers.
11. Clear the diagnostic log and verify that a later lookup starts a new report.

Record the device model, Android version, APK SHA-256, and each result. A successful build, signature check, or unit-test run must not be reported as physical-device validation.
