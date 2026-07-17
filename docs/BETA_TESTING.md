# KnownFirst Android beta testing

## Scope

The Android beta package is a directly installable signed APK for focused manual validation. Automated tests and builds do not prove runtime behavior on a physical device. GUI automation, emulator testing, and broad device testing are outside the standard beta-publish command.

Current identity:

- package ID: `com.tachiguro.knownfirst`
- display version: `0.1.0-beta.2`
- application version: `2`
- minimum Android version: Android 7.0 (API 24)

## Signing identity

The beta signing identity lives outside the repository:

- `%USERPROFILE%\KnownFirst-Secrets\knownfirst-beta.keystore`
- `%USERPROFILE%\KnownFirst-Secrets\knownfirst-beta-signing-password.txt`

Never commit either file and never generate a replacement while the existing keystore is usable. Back up the keystore and password together in a secure location. Losing the keystore prevents an APK from updating an installed beta signed with that identity. Exposing the keystore or password compromises the update identity and requires an explicit incident response.

The password may instead be supplied through `KNOWNFIRST_ANDROID_SIGNING_PASSWORD`. The publishing script does not print it.

## One-command publish

From the repository root run:

```powershell
.\scripts\publish-android-beta.ps1
```

The script publishes `KnownFirst.csproj` for `net10.0-android` in Release, creates an APK, signs it with the external beta keystore, verifies the APK with Android SDK `apksigner`, and writes:

- `artifacts/android-beta/KnownFirst-0.1.0-beta.2-android.apk`
- `artifacts/android-beta/KnownFirst-0.1.0-beta.2-android.apk.sha256.txt`
- `artifacts/android-beta/KnownFirst-0.1.0-beta.2-android.zip`
- `artifacts/android-beta/INSTALLATION.txt`

The ZIP contains only the APK and `INSTALLATION.txt`. The `artifacts` directory and Android package/signing files are ignored by Git.

## Manual checks

On an authorized Android device, verify at least:

1. The APK installs and KnownFirst launches without an immediate crash.
2. English/German UI switching remains immediate and persists after restart.
3. Import source language, lookup mode, and target language behave independently from UI language.
4. `Contact` and `Information` use lowercase dictionary lookup while their original context remains exact; `IT` remains case-sensitive.
5. Manual entry opens after NotFound and transient failure, accepts acronym-only, translation-only, or definition-only content, and advances once.
6. Back/Home pauses preparation and Home offers Continue preparation.
7. Cancelling a partially completed batch requires confirmation, retains accepted cards, and returns unresolved/skipped words to the backlog.
8. Android Back, safe areas, responsive Review/Preparation layouts, theme changes, and clipboard import still behave correctly.

Record the device model, Android version, APK SHA-256, and each result. A successful build, signature check, or unit-test run must not be reported as physical-device validation.
