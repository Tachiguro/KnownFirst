# KnownFirst Project State

**Status date:** 2026-08-16
**State source:** Synchronized `master` baseline. Authoritative live Git and PR state are discovered dynamically per [docs/NEW_CHAT_BOOTSTRAP.md](NEW_CHAT_BOOTSTRAP.md).

This document records stable, verified architectural facts and current capabilities. Plans belong in [ROADMAP.md](ROADMAP.md); active operational task state belongs in [CURRENT_WORK.md](CURRENT_WORK.md).

## Stable Release & Source Identity

| Field | Verified value |
| :--- | :--- |
| **Project** | KnownFirst |
| **Source Version (`master`)** | `1.0.0-beta.13` (build 13) — merged via PR #92 |
| **Active Database Schema** | SQLite `PRAGMA user_version` 10 |
| **Package ID** | `com.tachiguro.knownfirst` |
| **Target Distribution** | Google Play Internal Testing |
| **Distributed Status** | `1.0.0-beta.12` distributed and user-tested (confirmed 2026-07-30; see [docs/releases/1.0.0-beta.12.md](releases/1.0.0-beta.12.md)). `1.0.0-beta.13` has not been distributed. |
| **Installed Displayed Identity** | `1.0.0-beta.12` / Release / Build 12 / Commit `cfbaee6a` (DIRTY) |

## Supported Platforms

- **Android:** Distributed through Google Play Internal Testing; minimum Android version is API 24 (Android 7.0).
- **Windows:** Primary local development and automated/manual verification platform.
- **iOS & Mac Catalyst:** Deliberately removed from the project and not supported.

## Production Capabilities

- English, German, and Russian UI localization with persisted System, Light, and Dark appearance modes;
- exact text import with deterministic Unicode-aware sentence and vocabulary analysis;
- Russian as a translation target for English and German source texts (Russian source text remains deferred);
- simplified Definition or Translation import mode selection;
- resumable Known/Unknown vocabulary review with persisted decisions and Undo;
- language-scoped vocabulary identity and global minimal known-word markers;
- frequency-prioritized automatic or manual preparation;
- explicit online-lookup consent, read-only Wiktionary lookup with automatic Wikipedia definition fallback, and local SQLite lexical cache;
- source attribution, alternative-meaning selection, manual correction, and context snapshots;
- recognition and spelling card directions with independent deterministic schedules;
- Learn screen card direction indicators and visual "Repeat" badges for `IsAgainRepeat` cards;
- resumable learning sessions and permanent-known cleanup;
- portable `.kfarchive` (v2) data export with native Save dialog on Windows and Android;
- recovery import of `.kfarchive` into empty installations with native Open dialog;
- transactional populated-target import with validated pre-merge safety copies, preflight preview, collision-free action keys, and atomic commit-or-rollback (stale plans rejected; re-import converges to `NoChanges`);
- portable Active learning-workflow preservation and resume into empty Schema-10 targets (KF-BACKUP-005B) and populated-target Active-workflow convergence/conflict safety (KF-BACKUP-005C);
- card scheduling replay through existing scheduler in deterministic order preserving Sense, PreferredMeaning, and Direction;
- reopenable release-note history (`/release-notes`) and Help & Support entry point;
- functional "Report a bug" email composer action launching with structured template prompts and clipboard copy fallback;
- one-time localized What's New notice shown once per version;
- transactional local persistence (Schema 10), startup maintenance, and bounded structured diagnostics.

## Development, Tooling & Packaging Foundations

- **Repository Tooling & Path Portability (PR #111):** Organized script hierarchy (`scripts/packaging/`, `scripts/validation/`, `scripts/tools/`) with dynamic root resolution (`$PSScriptRoot` / `__file__`), eliminating fixed clone path dependencies.
- **Safe Artifact Cleanup (PR #110):** Canonical launcher `Clean` and `Clean -Deep` actions with root safety validation, log retention pruning, and protection of user databases and release packages.
- **Windows Distribution Packaging Infrastructure (PR #107, PR #113, PR #114):** Dedicated publishing scripts (`scripts/packaging/publish-windows-portable.ps1`, `scripts/packaging/publish-windows-msix.ps1`) supporting unpackaged win-x64 portable ZIP and MSIX packaging with SHA-256 sidecars, isolated build roots under `artifacts/build/` and `artifacts/obj/`, Store version mapping `1.0.13.0`, and contract test coverage. On 2026-08-16, the canonical `WindowsPortablePackage` action was executed from synchronized `master` commit `9e455d0e03494cac8e713cd4d16c66946124f852`, producing verified self-contained archive `artifacts\windows-portable\KnownFirst-1.0.0-beta.13-build13-win-x64-9e455d0.zip` (SHA-256 `cebd84824aa4e7909edb3a6e83c467573c3e245535ae585b6be832934a45a81e`) with confirmed runtime payload markers.
- **Windows Release Storage Isolation (PR #104):** Test-profile redirection under `artifacts/gui-tests/windows/profiles/` for safe local Gate-12 visual verification without touching real host data.
- **Pre-AAB Gate Test Infrastructure (PR #94, PR #96):** Configuration-sensitive `DefineConstants` test coverage and MSBuild property analysis for Release gating.
- **Android Rendered GUI Automation Foundation (PR #91 / P16-A):** MSBuild intermediate isolation and Release Notes screenshot capture foundation (P16-B/P16-C matrix automation pending).

## Evidence Boundaries & Release Limitations

- **Release Packaging & Distribution Boundary:** Source merge is not a packaging or distribution event. No final Release AAB/APK package has been authorized or created for `1.0.0-beta.13`. A real self-contained Windows Portable Release ZIP (`KnownFirst-1.0.0-beta.13-build13-win-x64-9e455d0.zip`) and matching SHA-256 sidecar were successfully produced and structurally verified from synchronized `master` commit `9e455d0e03494cac8e713cd4d16c66946124f852` on 2026-08-16. No real MSIX package has yet been produced; the Portable package has not been launched or installed on a clean/secondary PC, and no external distribution has occurred.
- **Pre-AAB Release-Readiness Gate:** Mandatory pre-AAB release-readiness verification ([docs/BUILD_AND_RELEASE.md](BUILD_AND_RELEASE.md) §7) remains strictly required on the live candidate HEAD before any future release package creation.
- **Store Identity:** Partner Center Store identity inputs remain template placeholders (`devidentity`).
- **Support KnownFirst:** Unimplemented planned feature; completely absent from production rendering without placeholders.
- **Cloud & Accounts:** No cloud synchronization, accounts, analytics, advertising, or payments exist. All persistence and backups are local-first.
