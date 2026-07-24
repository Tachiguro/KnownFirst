# KnownFirst Android beta and diagnostic testing

> **Current status (2026-07-24):** Current source identity is `1.0.0-beta.9` / version code `9`. Verified Beta 8 release evidence is preserved in [releases/1.0.0-beta.8.md](releases/1.0.0-beta.8.md). For build, packaging, signing, and AAB retention rules, see [BUILD_AND_RELEASE.md](BUILD_AND_RELEASE.md). For version policy, see [VERSIONING.md](VERSIONING.md).

## Scope

The Android test packages are directly installable signed APKs for focused manual validation. Automated tests and builds do not prove runtime behavior on a physical device. GUI automation, emulator testing, and broad device testing are outside routine automated checks.

Current package identities:

- normal Release: `com.tachiguro.knownfirst`
- Release-equivalent diagnostic: `com.tachiguro.knownfirst.diagnostic`
- standalone Debug: `com.tachiguro.knownfirst.debug`
- minimum Android version: Android 7.0 (API 24)

The diagnostic package keeps Release optimization, trimming, AOT, and embedded assemblies, while adding symbols and bounded lexical diagnostic actions. The Debug package embeds assemblies and disables Fast Deployment so that its APK can run without Visual Studio. The three package IDs allow side-by-side installation.

## Build, Packaging, and Signing

Build execution, APK/AAB packaging, keystore credentials, retention policy, and store publication boundaries are governed strictly by [docs/BUILD_AND_RELEASE.md](BUILD_AND_RELEASE.md).

## Diagnostic Log

Debug and diagnostic builds add these localized actions to Settings:

- Copy diagnostic report / Diagnosebericht kopieren
- Export diagnostic log / Diagnoselog exportieren
- Clear diagnostic log / Diagnoselog löschen

The bounded log records timestamps, build and app version, lookup phases, the normalized lookup term, explicit language/mode/provider metadata, cache/HTTP/parser outcomes, and sanitized exception details. It excludes imported documents, contexts, definitions, credentials, HTTP headers, and secrets. The normal Release build does not expose diagnostic actions or Android diagnostic log output.

## Manual Checks

After authorized, correctly identified packages exist, record results on an authorized Android device separately for normal Release, diagnostic, and Debug. Verify at least:

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
