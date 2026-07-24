# KnownFirst Build and Release Guide

> **Notice:** This document is read only when the user explicitly requests build, configuration verification, packaging, signing, APK/AAB generation, release, artifact reconstruction, or store-related work. It must not be part of routine feature-agent reading.

## 1. Build Verification

Build commands must target specific configurations and platforms as authorized:

- **Windows Debug:**
  ```powershell
  dotnet build ./KnownFirst.slnx -c Debug
  ```
- **Windows Release:**
  ```powershell
  dotnet build ./KnownFirst.csproj -c Release -f net10.0-windows10.0.19041.0
  ```
- **Android Debug (serial build):**
  ```powershell
  dotnet build ./KnownFirst.csproj -c Debug -f net10.0-android -m:1
  ```
- **Android Release (serial build):**
  ```powershell
  dotnet build ./KnownFirst.csproj -c Release -f net10.0-android -m:1
  ```
- **Windows BetaDiagnostic:**
  ```powershell
  dotnet restore ./KnownFirst.csproj -p:Configuration=BetaDiagnostic
  dotnet build ./KnownFirst.csproj -c BetaDiagnostic -f net10.0-windows10.0.19041.0
  ```

### Build Invariants

- Use `-m:1` for Android builds to enforce single-threaded MSBuild execution and prevent parallel asset compilation errors.
- Release and BetaDiagnostic builds must compile with **0 AOT warnings, 0 trimming warnings, and 0 source-generation warnings**.

## 2. Build Identity

Build identity components are governed by [docs/VERSIONING.md](VERSIONING.md):

- **Product version:** `<KnownFirstProductVersion>` in `KnownFirst.csproj` (e.g. `1.0.0-beta.9`).
- **Build number:** `<KnownFirstBuildNumber>` in `KnownFirst.csproj` (e.g. `9`).
- **Formatted identity:** `KnownFirst · <DisplayVersion> · <Configuration> · Build <BuildNumber> · Commit <ShortSHA>`
- **Source of truth:** `KnownFirst.csproj` owns authoritative version numbers. `Services/Diagnostics/BuildIdentityService.cs` formats runtime identity string.

## 3. Packaging

- APK and AAB creation require explicit user authorization.
- Packaging must use the synchronized intended source commit on master.
- Creating packages as a side effect of routine feature validation is prohibited.
- Parameterized Google Play AAB creation script:
  ```powershell
  .\scripts\publish-android-google-play.ps1 -VersionCode 9 -DisplayVersion 1.0.0-beta.9
  ```

## 4. Signing Identity and Safety

- The beta signing identity lives strictly outside the repository:
  - `%USERPROFILE%\KnownFirst-Secrets\knownfirst-beta.keystore`
  - `%USERPROFILE%\KnownFirst-Secrets\knownfirst-beta-signing-password.txt`
- Never print, log, copy, or commit signing credentials or keystores.
- Supply password via environment variable `KNOWNFIRST_ANDROID_SIGNING_PASSWORD` when automated script execution is authorized.

## 5. Artifact Retention Policy

- Retain exactly the **two newest verified Google Play AABs** and matching SHA-256 sidecars in the local storage location (the current release and immediately preceding release).
- Never delete the previous release artifact until the new release is created, signed, hashed, and verified.
- Temporary generated files in `bin/` or `obj/` are transient outputs, not retained release artifacts.
- Historical artifact reconstruction must target exact source release tags (e.g., `v1.0.0-beta.8`) and must not claim byte identity without physical proof.

## 6. Publication Boundaries

- Authorization to build or sign does not authorize Google Play Store upload or release publishing.
- Store uploads are never automatic.
- Pull-request merge is never automatic.
- Physical-device testing and manual GUI verification are separate explicit packages.

## 7. Post-Merge Release Outputs Package

After a user-facing milestone or release feature is reviewed, merged, and synchronized to local `master`, an authorized post-merge release output sequence generates:

1. Windows Debug build
2. Windows Release build
3. Android Debug build (`-m:1`)
4. Android Release build (`-m:1`)
5. Signed Google Play AAB (when explicitly requested and authorized)

All outputs must target the synchronized merged `master` HEAD commit.
