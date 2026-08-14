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
Executes the canonical launcher to create a Google Play AAB:
```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\knownfirst.ps1 -Action GooglePlayBundle
```
A successful local package consists of the final AAB and its matching SHA-256 sidecar. Creation does not authorize upload, installation, or device testing. Warnings prohibited by the release contract will fail packaging.

### WINDOWS_PORTABLE_PACKAGE
Executes the canonical launcher to create a transportable, self-contained Windows x64 Release ZIP archive and SHA-256 sidecar:
```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\knownfirst.ps1 -Action WindowsPortablePackage
```
- **Channel contract:** Release configuration, `net10.0-windows10.0.19041.0`, `win-x64` RID, unpackaged (`WindowsPackageType=None`), `SelfContained=true`, `WindowsAppSDKSelfContained=true`.
- **Packaging behavior:** Publishes into dedicated `artifacts\build\windows-portable\Release\net10.0-windows10.0.19041.0\win-x64\publish\`, validates required self-contained markers (`KnownFirst.exe`, `KnownFirst.dll`, `hostfxr.dll`, `Microsoft.WindowsAppRuntime.Bootstrap.dll`), archives the complete publish directory to `artifacts\windows-portable\KnownFirst-<ProductVersion>-build<BuildNumber>-win-x64-<ShortCommit>.zip`, and writes a matching `.sha256.txt` sidecar.
- **Isolation:** Intermediate and build outputs are isolated under `artifacts\obj\windows-portable\` and `artifacts\build\windows-portable\`; ordinary `bin\Release\` outputs backing `WindowsBuild` and `ValidateAll` validation evidence are never rewritten.
- **Update model:** Manual replacement channel only. The portable ZIP contains no updater, no self-update mechanism, and no installer. Updating an installation requires extracting a newer archive manually.
- **Boundaries:** Creation does not launch the executable, install the application, create an installer, or distribute the package.

### WINDOWS_MSIX_PACKAGE
Executes the canonical launcher to create an x64 Release MSIX package and SHA-256 sidecar:
```powershell
# Unsigned (default / Microsoft Store oriented):
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\knownfirst.ps1 -Action WindowsMsixPackage

# External certificate signing mode:
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\knownfirst.ps1 -Action WindowsMsixPackage -MsixSigning External
```
- **Channel contract:** Release configuration, `net10.0-windows10.0.19041.0`, `win-x64` RID, MSIX package (`WindowsPackageType=MSIX`), `AppxBundle=Never`, `UapAppxPackageBuildMode=SideloadOnly`, `SelfContained=true`, `WindowsAppSDKSelfContained=true`.
- **Output:** Staged and finalized under `artifacts\windows-msix\KnownFirst-<ProductVersion>-build<BuildNumber>-x64-<ShortCommit>-<SigningMarker>-<IdentityMarker>.msix` with a matching `.sha256.txt` sidecar.
- **Isolation:** Intermediate and build outputs are isolated under `artifacts\obj\windows-msix\` and `artifacts\build\windows-msix\`.
- **Signing modes:**
  - `None` (default): `AppxPackageSigningEnabled=false`. The Microsoft Store signs packages during Store ingestion; local signing is not required for Store submission. An unsigned package cannot be sideloaded without a signature.
  - `External`: Signs using a certificate that already exists in the current user's Windows Certificate Store, identified solely by its 40-character SHA-1 thumbprint read from environment variable `KNOWNFIRST_WINDOWS_MSIX_CERT_THUMBPRINT`.
- **Signing boundary & safety:** No certificate, PFX file, private key, or password is accepted, created, imported, trusted, or stored in the repository. The launcher forwards only the signing mode; the publishing script reads the environment variable directly so thumbprints never appear in launcher commands, logs, or state records. Missing or malformed thumbprints fail closed before building.
- **Store identity classification & placeholders:** `Platforms\Windows\Package.appxmanifest` currently retains development/template placeholder identity values (`maui-package-name-placeholder`, `CN=User Name`, `User Name`). Both the launcher and publishing script inspect the manifest via `scripts/windows-distribution-common.ps1` and classify the package as `devidentity`. Current MSIX packages are technical development-identity artifacts and are **NOT** Microsoft Store submission candidates until real Partner Center identity values are authoritatively supplied in a future separately scoped package.
- **Version mapping:** MAUI generates `Identity/@Version` as `<Display[0]>.<Display[1]>.<Display[2]>.<ApplicationVersion>`. The Microsoft Store requires the fourth section to be 0 and the first section to be non-zero. The MSIX packaging variant maps `ApplicationDisplayVersion` to `1.0.<KnownFirstBuildNumber>` and `ApplicationVersion` to `0`, yielding `1.0.13.0` for build 13. Application runtime identity is unaffected and reads from `KnownFirstProductVersion`/`KnownFirstBuildNumber` assembly metadata (`1.0.0-beta.13` / build `13`).
- **Update model:** The Microsoft Store MSIX is the intended production Windows install and update channel once Store submission work is authorized and completed; Microsoft Store infrastructure delivers application updates. Sideloading auto-update via `.appinstaller` is explicitly out of scope and unimplemented.
- **Boundaries:** Creation does not install, sideload, contact Partner Center, upload, or publish the MSIX.

### Windows Distribution Architecture and Evidence Boundaries
- **Single source of truth:** `scripts/windows-distribution-common.ps1` authoritatively derives short commit prefixes, artifact filenames, signing markers (`unsigned`/`signed`), Store identity markers (`devidentity`/`storeidentity`), MSIX candidate selection, and ZIP file-entry counting. Both packaging scripts and the launcher dot-source this helper.
- **Runtime evidence boundary:** Automated source-contract tests and deterministic MSBuild evaluation verify packaging arguments, path isolation, version mapping, and safety guards. No real portable ZIP or MSIX has yet been produced by this work package; actual publish execution, presence of runtime payload markers on the toolchain, certificate signing, clean-PC installation/execution, and Store ingestion/update behavior remain unproven until separately authorized operations.

### Legacy Direct-Install Helper Limitation
- `scripts/publish-android-test-packages.ps1` publishes Release, BetaDiagnostic, and Debug APKs, but its artifact names contain hard-coded legacy Beta 6 labels and installation metadata. `scripts/publish-android-beta.ps1` invokes that same helper.
- Until parameterized and updated by tests, do not report output from these helper scripts as current release evidence or distribute generated ZIP instructions as current release metadata. Record any authorized run as a tooling investigation.

## 4. Signing Identity and Safety

- **Android beta signing identity:** lives strictly outside the repository:
  - `%USERPROFILE%\KnownFirst-Secrets\knownfirst-beta.keystore`
  - `%USERPROFILE%\KnownFirst-Secrets\knownfirst-beta-signing-password.txt`
  - Supply password via environment variable `KNOWNFIRST_ANDROID_SIGNING_PASSWORD` when automated script execution is authorized.
- **Windows MSIX signing identity:**
  - Uses existing certificates from the local Windows Certificate Store only.
  - Specified via 40-character SHA-1 thumbprint in environment variable `KNOWNFIRST_WINDOWS_MSIX_CERT_THUMBPRINT`.
  - The repository never creates, imports, trusts, or stores certificates, PFX files, or passwords.
- Never print, log, copy, or commit signing credentials, thumbprints, passwords, or keystores.

## 5. Artifact Retention Policy

- Retain exactly the **two newest verified Google Play AABs** and matching SHA-256 sidecars in the local storage location (the current release and immediately preceding release).
- Retain verified Windows distribution artifacts (`artifacts\windows-portable\` and `artifacts\windows-msix\`) locally along with their matching SHA-256 sidecars.
- Never delete the previous release artifact until the new release is created, signed, hashed, and verified.
- Temporary generated files in `bin/` or `obj/` are transient outputs, not retained release artifacts.
- Historical artifact reconstruction must target exact source release tags (e.g. `v1.0.0-beta.8`) and must not claim byte identity without physical proof.

## 6. Publication Boundaries

- Authorization to build or sign does not authorize Google Play Store upload, Microsoft Store submission, or release publishing.
- Store uploads and submissions are never automatic.
- Pull-request merge is never automatic.
- Physical-device testing and manual GUI verification are separate explicit packages.

## 7. Mandatory Pre-AAB Release-Readiness Gate

**No `ANDROID_GOOGLE_PLAY_AAB` `PACKAGE_ONLY` operation may begin until this gate has passed for the exact candidate commit on `master`.** The gate is evidence to collect and record, not a formality; each item must be independently verified against that candidate commit, not inferred from an earlier commit.

1. Local `master` is synchronized, clean, and equals `origin/master`.
2. No unresolved unfinished control appears in the Release UI (see [docs/TESTING.md](TESTING.md) "Production-Control Policy").
3. Planned but unimplemented features are present only in documentation (`docs/ROADMAP.md`), not in Release rendering.
4. No unfinished control is merely concealed with CSS or left present in the accessibility tree.
5. No debug-only layout outline, diagnostic border, bounding box, overlay, badge, menu item, or developer control appears in the Release candidate (see [docs/TESTING.md](TESTING.md) "Debug-Only UI Rules").
6. `docs/PROJECT_STATE.md`, `docs/ROADMAP.md`, `docs/CURRENT_WORK.md`, `CHANGELOG.md`, the target release notes, version/build identity, and What's New content were reviewed against the candidate commit.
7. The documentation-review outcome is recorded as either "updated and current" (with the specific edits made) or "reviewed and already current, with no content change required." A passing review does not require a textual edit when none is warranted.
8. `ALL_AUTOMATED` passes on the exact candidate commit.
9. `UI_CONTRACT_AUTOMATED` is explicitly accounted for (it is a subset of `ALL_AUTOMATED`, but its result is called out separately for traceability).
10. Configuration-sensitive contract tests confirm that unfinished and diagnostic UI is absent from the Release configuration specifically, not only from the configuration under normal test execution.
11. Critical rendered GUI scenarios (see [docs/GUI_TEST_MATRIX.md](GUI_TEST_MATRIX.md)) affected by changes since the previous candidate gate pass are executed and recorded, with an explicit rationale for the selected scenario scope. Unrelated GUI scenarios are not required when no affected workflow changed, but the scope decision itself must be recorded.
12. A rendered or screenshot-based Release verification confirms that no diagnostic outline, overlay, or placeholder control is visible in the affected UI.
13. `FULL_VALIDATION` (or the documented equivalent Windows/Android build matrix) passes.
14. Android Release has zero prohibited AOT, trimming, source-generation, and warning findings (see "Build Invariants and Safeguards" above).
15. Required manual Android validation (see [docs/BETA_TESTING.md](BETA_TESTING.md)) is completed when Android-visible behavior changed since the previous candidate.
16. Known release blockers (see [docs/ROADMAP.md](ROADMAP.md) "Public-release blockers" and "Documentation Reconciliation and Release-Readiness Program") are classified and resolved for the intended distribution level.
17. Only after every applicable item above passes may a separately authorized `PACKAGE_ONLY` `ANDROID_GOOGLE_PLAY_AAB` operation begin.

**Clarifications:**
- Passing unit tests alone never authorizes packaging.
- Building successfully alone never authorizes packaging.
- Documentation review alone never authorizes packaging.
- CSS-hidden unfinished controls do not satisfy this gate.
- A control excluded only visually but still interactive or exposed to the accessibility tree does not satisfy this gate.
- AAB creation never authorizes upload or store publication (see "Publication Boundaries" above).
- Internal Testing and public Google Play promotion remain distinct distribution levels; passing this gate authorizes packaging, not a specific distribution level.
- The unfinished-control and debug-UI prohibitions (items 2-5, 10, 12) apply to every future AAB candidate, not only the next one.

## 8. FULL_RELEASE_OUTPUT_PACKAGE

This composite operation is **never** inferred from feature completion, PR merge, synchronization, or an individual build request. It is executed **only** upon explicit user request after a milestone is reviewed, merged, and synchronized to `master`:

1. Windows Debug build
2. Windows Release build
3. Android Debug build (`-m:1`)
4. Android Release build (`-m:1`)
5. Signed Google Play AAB

All outputs must target the synchronized merged `master` HEAD commit.
