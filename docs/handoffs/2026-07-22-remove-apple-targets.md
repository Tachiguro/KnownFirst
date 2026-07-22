# Remove Apple targets handoff

## Scope

This handoff records the platform-target cleanup performed on
`build/remove-apple-targets`, based on master commit
`9434f99b64f82718d4b0b0fa4dbaf607a4536aa6`.

## Starting state

- One registered worktree: `C:\Dev\KnownFirst`.
- The worktree was clean before the branch was created.
- Android and Windows were the intended product platforms; iOS and Mac
  Catalyst were still present as inactive, unvalidated project targets.
- Version `1.0.0-beta.8`, application versions, package identities, and the
  Android release evidence were preserved.

## Changes

- Reduced `KnownFirst.csproj` to `net10.0-android` and
  `net10.0-windows10.0.19041.0`.
- Removed iOS and Mac Catalyst `SupportedOSPlatformVersion` entries and the
  template Mac Catalyst runtime guidance.
- Removed `Platforms/iOS` and `Platforms/MacCatalyst`.
- Removed Apple-only diagnostic target-framework and MAUI platform branches.
- Removed the ignored iOS runtime/profile settings from `KnownFirst.csproj.user`.
- Updated the Windows smoke-test framework inventory and build-configuration
  checks so Apple targets are rejected.
- Added focused tests for target frameworks, platform directories, solution
  shape, package identity, AOT, and trimming.
- Updated current documentation and this handoff to state that Android and
  Windows are the only active app platforms.

## Verification

- Build-configuration verification passed.
- Icon verification passed.
- Focused platform/configuration tests: 7 passed, 0 failed, 0 skipped.
- Complete `KnownFirst.Tests` suite: 418 passed, 0 failed, 0 skipped.
- Windows Debug build: succeeded with 0 warnings and 0 errors.
- Android Debug build: succeeded with 0 warnings and 0 errors.
- Android Release build: succeeded with 0 warnings and 0 errors, including no
  AOT, trimmer, or source-generator warnings. The standard linker size-optimization
  informational message was emitted; it was not a build warning.
- The Beta 8 AAB remains 37,163,924 bytes with SHA-256
  `66528BE580127AA715D09CB0F580FCD603A11451F401D11E0C0D73883053D737`.
- The Beta 7 AAB remains SHA-256
  `8DD2CAC243F0C2B9FCE1088146799E209817EE9D30B40DBA9F60E66A956314C3`.
- No Apple target output remains under `bin` or `obj`; generated APKs remain
  confined to those ignored build directories.

No publish, AAB creation, installation, ADB/device action, database access, or
Wikipedia/Backup work is part of this branch.

## Remaining platforms and limitations

- Android remains the release-priority platform and Windows remains the local
  development and verification platform.
- iOS and Mac Catalyst are intentionally not supported and no Apple build or
  device validation is claimed.
- Linux remains a later feasibility or alternative-host architecture check.
- Microsoft Store publication is planned; Google Play remains in internal
  testing and public publication is planned.

## Next exact action

Implement the Wikipedia fallback behind Wiktionary on a separate feature
branch, after auditing the provider and persistence model. Backup/Restore
Phase 3 remains paused.
