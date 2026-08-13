# KnownFirst Android beta and diagnostic testing

> **Current status (2026-08-13):** Current source identity on `master` is `1.0.0-beta.13` / build `13`, merged via PR #92 (final PR head `774b2245f64a986fe004f4ebd3288747642bdb0f`, merge commit `a5a9e734af0db0639a38471433748e334ca34d65`); local `POST_MERGE_SYNC_ONLY` for that merge completed exactly once and must not be repeated. Merging is not a build, package, or device-validation event: **no Beta-13 manual Android validation has yet been completed**, and the mandatory exact-candidate Pre-AAB Release-Readiness Gate ([BUILD_AND_RELEASE.md](BUILD_AND_RELEASE.md) §7) has not been executed. The latest confirmed **externally distributed and physically device-tested** release remains `1.0.0-beta.12` / build `12` (Google Play Internal Testing, user-confirmed 2026-07-30); the installed application displayed commit `cfbaee6a` (DIRTY), and exact original rollout date and exact source commit remain unrecorded in repository evidence. See [releases/1.0.0-beta.12.md](releases/1.0.0-beta.12.md) and [releases/1.0.0-beta.13.md](releases/1.0.0-beta.13.md) for release evidence boundaries. For build, packaging, signing, and AAB retention rules, see [BUILD_AND_RELEASE.md](BUILD_AND_RELEASE.md). For version policy, see [VERSIONING.md](VERSIONING.md).

> **Evidence boundary — distributed Beta 12 versus current source:** The recorded Beta-12 distribution evidence documents database schema **7**, portable restore into an empty installation only, and **no** populated-target merge (see [releases/1.0.0-beta.12.md](releases/1.0.0-beta.12.md)). Current source on `master` runs database schema **10** and supports populated-target transactional merge. The Beta-12 device evidence therefore proves nothing about current-source Schema-10 behavior, populated-target merge, the read-only import preview, or the merged KF-BACKUP-005B Active-learning empty-target capability. PR #81 placed KF-BACKUP-005B on `master`, but no 005B physical-device, APK/AAB, signing, publishing, or external-distribution evidence was produced; source merge is not device validation. None of those capabilities may be reported as physically device-validated.

## Scope

The Android test packages are directly installable signed APKs for focused manual validation. Automated tests and builds do not prove runtime behavior on a physical device. GUI automation, emulator testing, and broad device testing are outside routine automated checks.

This document supplies the manual Android procedure for the `MANUAL_ANDROID_GUI` scope. That scope is defined in [TESTING.md](TESTING.md) and is not redefined here.

An automated test run, a platform build, an APK or AAB packaging step, a store distribution, an installation on a device, and a completed physical-device test are six distinct evidence categories. None of them implies another.

Current package identities:

- normal Release: `com.tachiguro.knownfirst`
- Release-equivalent diagnostic: `com.tachiguro.knownfirst.diagnostic`
- standalone Debug: `com.tachiguro.knownfirst.debug`
- minimum Android version: Android 7.0 (API 24)

The diagnostic package keeps Release optimization, trimming, AOT, and embedded assemblies, while adding symbols and bounded lexical diagnostic actions. The Debug package embeds assemblies and disables Fast Deployment so that its APK can run without Visual Studio. The three package IDs allow side-by-side installation.

## P16-A automation-only identity

`com.tachiguro.knownfirst.guitest` / `KnownFirst GUI Test` is a dedicated Android Debug-derived GUI automation identity. It is not production, BetaDiagnostic, the ordinary developer Debug identity, or a Google Play/Internal Testing identity. P16-A evidence does not show it being built, packaged, installed, distributed, or device-validated.

Its purpose is hard OS/application sandbox separation from ordinary Debug data, a fresh private GUI-test profile, protection of developer/user data, and deterministic offline automation. Future runtime execution requires separate authorization and must record the exact commit/build identity, package identity, device or emulator, Android/API version, language/theme, viewport/density, Appium/UiAutomator2/WebView/Chromedriver versions, and result/evidence artifacts.

Automated GUI execution, emulator/device operations, package installation, and manual Android validation are distinct evidence categories. No physical-device or distribution evidence is implied by this identity or its source contracts.

## Build, Packaging, and Signing

Build execution, APK/AAB packaging, keystore credentials, retention policy, and store publication boundaries are governed strictly by [docs/BUILD_AND_RELEASE.md](BUILD_AND_RELEASE.md).

## Diagnostic Log

Debug and diagnostic builds add these actions to Settings, localized in English, German, and Russian:

- Copy diagnostic report / Diagnosebericht kopieren
- Export diagnostic log / Diagnoselog exportieren
- Clear diagnostic log / Diagnoselog löschen

The bounded log records timestamps, build and app version, lookup phases, the normalized lookup term, explicit language/mode/provider metadata, cache/HTTP/parser outcomes, and sanitized exception details. It excludes imported documents, contexts, definitions, credentials, HTTP headers, and secrets. The normal Release build does not expose diagnostic actions or Android diagnostic log output.

## Manual Checks

After authorized, correctly identified packages exist, record results on an authorized Android device separately for normal Release, diagnostic, and Debug. Verify at least:

1. The APK installs and KnownFirst launches without an immediate crash.
2. System, English, German, and Russian UI selection remains immediate and persists after restart; System follows the supported device language and falls back to English for an unsupported device language.
3. Import source language, lookup mode, and translation target language behave independently from the UI language. The source language remains English or German; Russian is selectable as a translation target only.
4. `Contact` and `Information` use lowercase dictionary lookup while their original context remains exact; `IT` remains case-sensitive.
5. Manual entry opens after NotFound and transient failure, accepts acronym-only, translation-only, or definition-only content, and advances once.
6. Back/Home pauses preparation and Home offers Continue preparation.
7. Cancelling a partially completed batch requires confirmation, retains accepted cards, and returns unresolved/skipped words to the backlog.
8. Android Back, safe areas, responsive Review/Preparation layouts, theme changes, and clipboard import still behave correctly.
9. The same automatic online dictionary lookup completes in all three packages without terminating the process.
10. In the diagnostic package, copy and export the report after a lookup and verify that phase, cache, HTTP, and parser metadata are present without imported document text, context text, definitions, credentials, or headers.
11. Clear the diagnostic log and verify that a later lookup starts a new report.
12. Portable export writes a `.kfarchive` archive to a destination chosen through the Android picker; a cancelled picker changes nothing, and a failed or invalid archive never acquires or writes a destination.
13. Portable import shows the read-only preview before anything is changed and correctly distinguishes restore into an empty installation, populated-target merge, and no-change. Cancelling from the preview leaves the installation unchanged.
14. A confirmed import reports its outcome, including merge counts and the validated safety-copy notice where applicable. A validation failure, a blocking active workflow, an unsupported target state, or a rejected stale plan changes nothing and never presents a false success.
15. The one-time What's New notice appears once for the installed version and stays dismissed afterwards.
16. Settings contains no `Support KnownFirst` control, no `Report a bug` control, and no "coming soon" placeholder panel. Verify their absence in the actual package on the actual device under test, and record that concrete evidence. Milestone 14A removed them from the production source, but that is source-contract evidence only — it is not physical-device or AAB evidence and must never be recorded as if this device check had already been performed. The Help and Support heading and the build-identity line remain present and are expected.
17. Settings → Help and Support exposes a reopenable release-note-history entry point (Milestone 14B). Activating it navigates to the release-note page, which lists prior releases newest-first including the currently installed version, and reopening it after the one-time What's New notice was dismissed does not re-trigger that notice. Verify this on the actual device under test and record the result; it must never be reported as performed based on source-contract or automated-test evidence alone (see [GUI_TEST_MATRIX.md](GUI_TEST_MATRIX.md) row S36).

Record the device model, Android version, APK SHA-256, and each result. A successful build, signature check, or unit-test run must not be reported as physical-device validation. Any Schema-9 or populated-target-merge behavior observed here applies only to the package actually under test and must never be back-attributed to the recorded Beta-12 distribution evidence.
