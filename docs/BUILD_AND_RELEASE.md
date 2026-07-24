# KnownFirst Build and Release Guide

> **Notice:** This document is read only when the user explicitly requests build, configuration verification, packaging, signing, APK/AAB generation, release, artifact reconstruction, or store-related work. It must not be part of routine feature-agent reading.

## Operation Isolation Rules

- **Isolated execution:** Execute only the exact requested build or packaging operation.
- **No side-effect testing:** Do not run automated unit tests, smoke tests, or GUI tests as a side effect of a build or package request unless separately requested.
- **No extra targets:** Do not build additional target platforms or configurations beyond the explicitly requested intent.
- **No version auto-increment:** No build or package operation increments product version or build number in `KnownFirst.csproj`. Version changes occur only in an explicit versioning/release task.
- **Local rebuilds:** Rebuilding unchanged source code does not alter version identity.
- **Clarification for ambiguous APK requests:** An APK request without specified configuration (Debug vs Release vs BetaDiagnostic) is ambiguous and requires one concise clarification question.
- **Store upload isolation:** Generating a build, signing, or creating an AAB does not authorize Google Play Store upload. Store upload is never automatic and requires separate explicit authorization.
- **Device testing isolation:** Manual GUI or physical device testing remains a separate authorized package (see [docs/BETA_TESTING.md](BETA_TESTING.md)).

## 1. Isolated Build Commands

### WINDOWS_DEBUG_BUILD
```powershell
dotnet build ./KnownFirst.csproj -c Debug -f net10.0-windows10.0.19041.0 --nologo
```

### WINDOWS_RELEASE_BUILD
```powershell
dotnet build ./KnownFirst.csproj -c Release -f net10.0-windows10.0.19041.0 --nologo
```

### WINDOWS_BETADIAGNOSTIC_BUILD
```powershell
dotnet restore ./KnownFirst.csproj -p:Configuration=BetaDiagnostic
dotnet build ./KnownFirst.csproj -c BetaDiagnostic -f net10.0-windows10.0.19041.0 --no-restore
```

### ANDROID_DEBUG_BUILD
```powershell
dotnet build ./KnownFirst.csproj -c Debug -f net10.0-android -m:1
```

### ANDROID_RELEASE_BUILD
```powershell
dotnet build ./KnownFirst.csproj -c Release -f net10.0-android -m:1
```

### ANDROID_BETADIAGNOSTIC_BUILD
```powershell
dotnet restore ./KnownFirst.csproj -p:Configuration=BetaDiagnostic
dotnet build ./KnownFirst.csproj -c BetaDiagnostic -f net10.0-android -m:1 --no-restore
```

### Build Invariants and Safeguards
- **Serial Android builds:** Always use `-m:1` for Android builds to enforce single-threaded MSBuild execution and prevent parallel asset compilation errors.
- **AOT and Trimming checks:** Android Release and Android BetaDiagnostic builds must compile with **0 AOT warnings, 0 trimming warnings, and 0 source-generation warnings**. (Does not apply to Windows builds).
- **Empty Configuration restore safeguard:** `KnownFirst.csproj` declares `Configuration` in `TreatAsLocalProperty`. Visual Studio or command-line restores with empty configuration properties fall back cleanly to `Debug` without generating empty framework graph errors (`NETSDK1005`).
- **NuGet multi-target restore safeguard:** Core NuGet properties like `PackageVersion` remain uniform across target frameworks to prevent `NU1105` evaluation failures when evaluating Windows and Android targets together.

## 2. Build Identity

Build identity components are governed by [docs/VERSIONING.md](VERSIONING.md):
- Read `<KnownFirstProductVersion>` and `<KnownFirstBuildNumber>` directly from `KnownFirst.csproj`.
- **Formatted identity string:** `KnownFirst · <DisplayVersion> · <Configuration> · Build <BuildNumber> · Commit <ShortSHA>`
- `Services/Diagnostics/BuildIdentityService.cs` formats the runtime identity string.

## 3. Isolated Packaging Commands

Packaging creation requires explicit user authorization and must target the synchronized intended source commit on `master`.

### ANDROID_DEBUG_APK
Requires explicit user request. Executes build steps required for Debug APK.

### ANDROID_RELEASE_APK
Requires explicit user request. Executes build steps required for Release APK.

### ANDROID_BETADIAGNOSTIC_APK
Requires explicit user request. Executes build steps required for BetaDiagnostic APK.

### ANDROID_GOOGLE_PLAY_AAB
Executes parameterized Google Play AAB creation script (reading version numbers from `KnownFirst.csproj`):
```powershell
.\scripts\publish-android-google-play.ps1 -VersionCode <BuildNumber> -DisplayVersion <ProductVersion>
```

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

## 7. FULL_RELEASE_OUTPUT_PACKAGE

This composite operation is **never** inferred from feature completion, PR merge, synchronization, or an individual build request. It is executed **only** upon explicit user request after a milestone is reviewed, merged, and synchronized to `master`:

1. Windows Debug build
2. Windows Release build
3. Android Debug build (`-m:1`)
4. Android Release build (`-m:1`)
5. Signed Google Play AAB

All outputs must target the synchronized merged `master` HEAD commit.
