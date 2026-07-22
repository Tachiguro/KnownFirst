# Changelog

All notable user-visible changes to KnownFirst are documented in this file.
The structure follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/)
and uses the application's prerelease version identifiers.

## [Unreleased]

No user-visible application changes are recorded.

### Internal

- Removed the unsupported iOS and Mac Catalyst application targets, platform
  folders, Apple-only diagnostics branches, and local Apple launch settings.
- Android and Windows are now the only active application target platforms.

- Added immutable version-1 backup data contracts, explicit external enum
  mappings, centralized format limits, and privacy-safe stable error codes.
- Added strict source-generated JSON metadata and typed UTF-8 codecs for the
  future `manifest.json` and `data.json` payloads without reflection fallback.
- Hardened database initialization so an unsupported future schema is rejected
  before any table, cache, or version mutation.
- ZIP/archive creation, database snapshotting, usable backup, restore, file
  selection, and backup/restore UI remain unimplemented and unreachable.

## [1.0.0-beta.8] - 2026-07-22

### Added

- No new user-facing features. Beta 8 is a release-stability update.

### Changed

- No intentional workflow or persisted-data-model changes from Beta 7.

### Fixed

- Fixed the Android Release crash during automatic online vocabulary lookup.
- Replaced reflection-dependent JSON serialization in the Release AOT path
  with source-generated serialization metadata.
- Replaced the AOT-unsafe CSS-selector path in the Wiktionary parser.

### Known limitations

- Versioned backup and restore are not implemented.
- Cloud synchronization is not implemented.
- A dictionary cache miss requires a network connection and explicit Wikimedia
  lookup consent.
- iOS and Mac Catalyst are intentionally not supported product platforms; their
  targets and platform folders were removed in the current platform-cleanup
  branch.
- The direct-install Android test-package script still uses legacy Beta 6
  artifact labels and must not be used to claim a Beta 8 package.

See the [Beta 8 release notes](docs/releases/1.0.0-beta.8.md) for release
identity and verification evidence.
