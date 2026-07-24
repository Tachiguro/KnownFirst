# KnownFirst

Skip what you know. Learn what matters.

## Overview

KnownFirst is a local-first vocabulary learning application. It analyzes user-provided texts, lets the user classify newly encountered vocabulary, prepares unknown words with real source contexts, and schedules learning cards.

The current MVP supports:

- .NET 10 MAUI Blazor Hybrid on Windows and Android
- responsive navigation and Android safe-area handling
- English and German UI localization
- immediate, persisted System, Light, and Dark appearance modes
- text import with deterministic Unicode-aware analysis and exact source coordinates
- resumable Known/Unknown vocabulary review with Undo
- automatic and manual word preparation
- optional online dictionary lookup (Wiktionary with automatic Wikipedia definition fallback) with explicit consent and a local SQLite cache
- recognition and spelling learning cards with deterministic scheduling
- local SQLite persistence, migrations, transactions, and cleanup
- persistent structured diagnostics with redaction and bounded retention
- automated Core, persistence, workflow, localization, and diagnostics tests

The binding product and architecture specifications are [docs/KNOWNFIRST_ARCHITECTURE.md](docs/KNOWNFIRST_ARCHITECTURE.md) and [docs/MVP_WORKFLOW.md](docs/MVP_WORKFLOW.md).

## Documentation

Start with [AGENTS.md](AGENTS.md) and [docs/INDEX.md](docs/INDEX.md). They define the universal rules, task-based reading router, and delivery workflow.

- [Task Index](docs/INDEX.md) routes agents to task-specific specifications.
- [Agent Workflow](docs/AGENT_WORKFLOW.md) defines detailed operational delivery and validation policies.
- [Build and Release Guide](docs/BUILD_AND_RELEASE.md) defines build, packaging, signing, and release procedures.
- [Project state](docs/PROJECT_STATE.md) records verified current release, capabilities, tests, database status, and limitations.
- [Roadmap](docs/ROADMAP.md) records prioritized next milestones.
- [Changelog](CHANGELOG.md) records user-visible release changes.
- [Database contract](docs/DATABASE_CONTRACT.md) defines persisted-data and migration rules.
- [Architecture](docs/KNOWNFIRST_ARCHITECTURE.md), [MVP workflow](docs/MVP_WORKFLOW.md), and [word analysis](docs/WORD_ANALYSIS.md) are binding specifications.
- [Decision records](docs/decisions/README.md), [release notes](docs/releases/1.0.0-beta.8.md), and [handoffs](docs/handoffs/2026-07-22-beta-8-release.md) preserve rationale and release evidence.

## Technology stack

- C# and .NET 10
- .NET MAUI Blazor Hybrid
- Razor
- SQLite
- MSTest

## Project structure

- `KnownFirst` contains the MAUI application, Razor UI, platform integrations, local data access, and application services.
- `KnownFirst.Core` contains platform-independent language, text-analysis, preparation, learning, navigation, and Settings policies.
- `KnownFirst.Tests` contains the automated Core, SQLite, service, localization, UI-contract, and diagnostics tests.
- `scripts` contains repeatable build, smoke-test, and packaging workflows.

## Prerequisites

- Visual Studio Community 2026 or a compatible current version
- .NET 10 SDK
- .NET MAUI workloads
- Android SDK when building Android

## Windows development workflow

Run the complete deterministic Windows verification from the repository root:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File ./scripts/smoke-test-windows.ps1
```

The script cleans generated project output, performs a plain restore, verifies every expected target in `project.assets.json`, builds the Debug solution and Windows target, runs all tests, launches the actual Windows executable, observes its window and startup-complete log event, keeps it alive for the smoke interval, and closes it.

Individual commands remain available:

```powershell
dotnet restore ./KnownFirst.csproj --force-evaluate --no-cache
dotnet build ./KnownFirst.csproj -c Debug -f net10.0-windows10.0.19041.0 --no-restore
dotnet build ./KnownFirst.slnx -c Debug
dotnet test ./KnownFirst.Tests/KnownFirst.Tests.csproj -c Debug
powershell -NoProfile -ExecutionPolicy Bypass -File ./scripts/verify-build-configurations.ps1
```

For interactive development, open `KnownFirst.slnx` in Visual Studio, select Windows Machine, and start debugging.

### Empty Configuration restore safeguard

Visual Studio can supply `Configuration` as an empty global MSBuild property during project evaluation. Global properties are immutable unless a project explicitly treats them as local. Without that declaration, the conditional Debug fallback appeared correct in the project file but could not replace the empty value; NuGet then completed a restore with an empty framework graph, and the next Windows build failed with `NETSDK1005`.

`KnownFirst.csproj` declares `Configuration` in `TreatAsLocalProperty`. The existing conditional fallback can therefore resolve only an empty value to `Debug`, while explicit `Debug`, `Release`, and `BetaDiagnostic` selections remain unchanged. This makes plain command-line restore and Visual Studio design-time restore deterministic after deleting `.vs`, `bin`, and `obj`.

Visual Studio restore also requires standard NuGet properties such as `PackageVersion` to have one value across every target framework in a multi-target project. Debug and BetaDiagnostic package versions are therefore configuration-wide; Android-specific application IDs, titles, and visible versions remain scoped to Android. This prevents Visual Studio from replacing a valid assets file with an empty `NU1105` graph when Windows and Android are evaluated together.

## Other build configurations

Build the persistent diagnostic configuration on Windows:

```powershell
dotnet restore ./KnownFirst.csproj -p:Configuration=BetaDiagnostic
dotnet build ./KnownFirst.csproj -c BetaDiagnostic -f net10.0-windows10.0.19041.0 --no-restore
```

Build Android without launching an emulator:

```powershell
dotnet build ./KnownFirst.csproj -c Debug -f net10.0-android
```

## Diagnostics

Runtime logs are stored outside the repository under the application data directory. On Windows, the location is equivalent to:

```text
%LOCALAPPDATA%\KnownFirst\Logs
```

Logs use structured JSON lines, session-specific files, size-based rolling, finite file retention, and age retention. They include build, platform, process, lifecycle, workflow, and exception metadata. Imported text, credentials, authorization headers, secrets, and private keys must not be logged; diagnostics record bounded identifiers, hashes, counts, lengths, outcomes, and timings instead.

## Privacy direction

KnownFirst is local-first:

- Imported texts and learning data remain on the device.
- No account, analytics, advertising, or mandatory cloud service is required.
- Optional dictionary requests send only the selected term and required language information directly to Wikimedia (Wiktionary / Wikipedia) after user consent.
- No document, context sentence, or learning history is sent to the KnownFirst developer.

This direction describes the current architecture and is not a promise of final legal compliance.

## Platforms

- Windows
- Android

Android is the initial release priority; Windows is the primary local development and verification workflow.
iOS and Mac Catalyst are intentionally not supported and are not active project targets.

## Author

Developed by Tachiguro.

## License

KnownFirst is licensed under the [Apache License 2.0](LICENSE.txt).
