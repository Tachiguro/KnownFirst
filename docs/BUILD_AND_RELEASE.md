# KnownFirst Build and Release Guide

> **Notice:** This document is read only when the user explicitly requests build, configuration verification, packaging, signing, APK/AAB generation, release, artifact reconstruction, or store-related work. It must not be part of routine feature-agent reading.

## 1. Build Verification

Build commands must target specific configurations and platforms as explicitly authorized:

- **Windows Debug:**
  ```powershell
  dotnet build ./KnownFirst.csproj -c Debug -f net10.0-windows10.0.19041.0 --nologo
  ```
- **Windows Release:**
  ```powershell
  dotnet build ./KnownFirst.csproj -c Release -f net10.0-windows10.0.19041.0 --nologo
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
  dotnet build ./KnownFirst.csproj -c BetaDiagnostic -f net10.0-windows10.0.19041.0 --no-restore
  ```

### Build Invariants and Restore Safeguards

- **Serial Android builds:** Always use `-m:1` for Android builds to enforce single-threaded MSBuild execution and prevent parallel asset compilation errors.
- **AOT and Trimming checks:** Android Release and BetaDiagnostic builds must compile with **0 AOT warnings, 0 trimming warnings, and 0 source-generation warnings**.
- **Empty Configuration restore safeguard:** `KnownFirst.csproj` declares `Configuration` in `TreatAsLocalProperty`. Visual Studio or command-line restores with empty configuration properties fall back cleanly to `Debug` without generating empty framework graph errors (`NETSDK1005`).
- **NuGet multi-target restore safeguard:** Core NuGet properties like `PackageVersion` remain uniform across target frameworks to prevent `NU1105` evaluation failures when evaluating Windows and Android targets together.

## 2. Build Identity

Build identity components are governed by [docs/VERSIONING.md](VERSIONING.md):

- Read `<KnownFirstProductVersion>` and `<KnownFirstBuildNumber>` directly from `KnownFirst.csproj`.
- **Formatted identity string:** `KnownFirst · <DisplayVersion> · <Configuration> · Build <BuildNumber> · Commit <ShortSHA>`
- `Services/Diagnostics/BuildIdentityService.cs` formats the runtime identity string.

## 3. Packaging

- APK and AAB creation require explicit user authorization.
- Packaging must use the synchronized intended source commit on master.
- Creating packages as a side effect of routine feature validation is strictly prohibited.
- Parameterized Google Play AAB creation script (reading current version numbers from `KnownFirst.csproj`):
  ```powershell
  .\scripts\publish-android-google-play.ps1 -VersionCode <BuildNumber> -DisplayVersion <ProductVersion>
  ```
  *(Example for Beta 9: `.\scripts\publish-android-google-play.ps1 -VersionCode 9 -DisplayVersion 1.0.0-beta.9`)*

### Legacy Direct-Install Helper Limitation

- `scripts/publish-android-test-packages.ps1` publishes Release, BetaDiagnostic, and Debug APKs, but its artifact names contain hard-coded legacy Beta 6 labels and installation metadata. `scripts/publish-android-beta.ps1` invokes that same helper.
- Until parameterized and updated by tests, do not report output from these helper scripts as current release evidence or distribute generated ZIP instructions as current release metadata. Record any authorized run as a tooling investigation.

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
- Historical artifact reconstruction must target exact source release tags (e.g. `v1.0.0-beta.8`) and must not claim byte identity without physical proof.

## 6. Publication Boundaries

- Authorization to build or sign does not authorize Google Play Store upload or release publishing.
- Store uploads are never automatic.
- Pull-request merge is never automatic.
- Physical-device testing and manual GUI verification are separate explicit packages.

## 7. Post-Merge Release Outputs Package

Only when explicitly requested by the user after a feature or milestone is reviewed, merged, and synchronized to local `master`, an authorized release output sequence generates:

1. Windows Debug build
2. Windows Release build
3. Android Debug build (`-m:1`)
4. Android Release build (`-m:1`)
5. Signed Google Play AAB (when explicitly requested and authorized)

All outputs must target the synchronized merged `master` HEAD commit.
