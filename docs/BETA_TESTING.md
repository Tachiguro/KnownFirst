# KnownFirst Android beta and diagnostic testing

> **Reviewed 2026-07-22:** The current release identity is
> `1.0.0-beta.8` / version code `8`. The direct-install APK helper still
> contains legacy Beta 6 filenames and installation metadata; it must not be
> used to claim a Beta 8 artifact until a separate code task corrects it. The
> verified Beta 8 release is documented in
> [releases/1.0.0-beta.8.md](releases/1.0.0-beta.8.md).

## Scope

The Android test packages are directly installable signed APKs for focused manual validation. Automated tests and builds do not prove runtime behavior on a physical device. GUI automation, emulator testing, and broad device testing are outside the package-publish command.

Current identities:

- normal Release: `com.tachiguro.knownfirst`, `1.0.0-beta.8`
- Release-equivalent diagnostic: `com.tachiguro.knownfirst.diagnostic`, `1.0.0-beta.8-diagnostic`
- standalone Debug: `com.tachiguro.knownfirst.debug`, `1.0.0-beta.8-debug`
- application version: `8`
- minimum Android version: Android 7.0 (API 24)

The diagnostic package keeps Release optimization, trimming, AOT, and embedded assemblies, while adding symbols and the bounded lexical diagnostic actions. The Debug package embeds assemblies and disables Fast Deployment so that its APK can run without Visual Studio. The three package IDs allow side-by-side installation.

## Signing identity

The beta signing identity lives outside the repository:

- `%USERPROFILE%\KnownFirst-Secrets\knownfirst-beta.keystore`
- `%USERPROFILE%\KnownFirst-Secrets\knownfirst-beta-signing-password.txt`

Never commit either file and never generate a replacement while the existing keystore is usable. Back up the keystore and password together in a secure location. Losing the keystore prevents an APK from updating an installed beta signed with that identity. Exposing the keystore or password compromises the update identity and requires an explicit incident response.

The password may instead be supplied through `KNOWNFIRST_ANDROID_SIGNING_PASSWORD`. The publishing script does not print it.

## Publishing workflows

### Google Play AAB

The current parameterized release path is:

```powershell
.\scripts\publish-android-google-play.ps1 -VersionCode 9 -DisplayVersion 1.0.0-beta.9
```

It publishes a signed AAB for `net10.0-android` in Release, verifies the
signature, and writes a SHA-256 sidecar below the ignored `artifacts`
directory. Run it only for an explicitly authorized release task with the
existing external signing identity.

#### Permanent AAB retention policy

- Retain exactly the two newest verified Google Play Internal Testing AABs and their matching SHA-256 sidecars.
- These represent the immediately previous release and the current release.
- Never delete the previous release before the new release is created, signed, independently hashed, and verified.
- After successful verification of the new release, delete only release AABs older than those two.
- Temporary generated AABs in `bin` or `obj` are build outputs, not retained release artifacts.
- Never delete APKs, source code, documentation, signing credentials, or unrelated files.
- A missing previous release may be regenerated only from its exact release tag and existing signing identity.
- Regenerated historical artifacts must be explicitly documented and must not be claimed byte-identical to the deleted original.

### Direct-install APK helper

`publish-android-test-packages.ps1` still publishes Release,
BetaDiagnostic, and Debug APKs and verifies them with `apksigner`, but its
artifact names are hard-coded to Beta 6 and its generated installation text
reports an older version code. `publish-android-beta.ps1` invokes that same
helper.

Until those values are parameterized and covered by corrected tests:

- do not describe its output as a Beta 8 package;
- do not distribute its ZIP instructions as current release metadata; and
- record any explicitly authorized run as a tooling investigation rather than
  release evidence.

## Diagnostic log

Debug and diagnostic builds add these localized actions to Settings:

- Copy diagnostic report / Diagnosebericht kopieren
- Export diagnostic log / Diagnoselog exportieren
- Clear diagnostic log / Diagnoselog löschen

The bounded log records timestamps, build and app version, lookup phases, the normalized lookup term, explicit language/mode/provider metadata, cache/HTTP/parser outcomes, and sanitized exception details. It excludes imported documents, contexts, definitions, credentials, HTTP headers, and secrets. The normal Release build does not expose the diagnostic actions or Android diagnostic log output.

## Manual checks

After current, correctly identified packages exist, record results on an
authorized Android device separately for normal Release, diagnostic, and Debug.
Verify at least:

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
