# KnownFirst

Skip what you know. Learn what matters.

## Overview

KnownFirst is a local-first vocabulary learning application designed to extract words from user-provided texts, filter out words the user already knows, and prepare only unknown, relevant words for learning.

The project is currently in early MVP development. The application foundation is working, but text analysis and vocabulary-learning features are not implemented yet.

## Core idea

The planned workflow is:

1. Import a text.
2. Analyze its vocabulary.
3. Mark newly discovered words as known or unknown.
4. Prepare the highest-priority unknown words.
5. Learn them using real contexts from imported texts.

## Current development status

The following foundation is currently available:

- .NET 10 MAUI Blazor Hybrid application
- Windows and Android builds
- Responsive navigation
- English and German user interface
- System, Light, and Dark appearance modes
- Local SQLite foundation
- Initial Settings pages
- Automated unit-test foundation

Text importing, text analysis, word preparation, and learning workflows are still planned work.

## Planned platforms

- Windows
- Android

Android is the initial release priority.

## Technology stack

- C#
- .NET 10
- .NET MAUI Blazor Hybrid
- Razor
- SQLite
- MSTest

## Project structure

- `KnownFirst` contains the MAUI Blazor Hybrid application, Razor UI, platform integrations, local data access, and application services.
- `KnownFirst.Core` contains platform-independent language and Settings policies.
- `KnownFirst.Tests` contains the MSTest unit and consistency tests for Core behavior and localization resources.

## Prerequisites

- Visual Studio Community 2026 or a compatible current version
- .NET 10 SDK
- .NET MAUI workload
- Android SDK for Android builds

## Build and run

Restore all projects from the repository root:

```shell
dotnet restore KnownFirst.slnx
```

Build the Windows target:

```shell
dotnet build KnownFirst.csproj -f net10.0-windows10.0.19041.0
```

Build the Android target:

```shell
dotnet build KnownFirst.csproj -f net10.0-android
```

For interactive development, open `KnownFirst.slnx` in Visual Studio, select the Windows Machine or an Android emulator/device target, and start debugging.

## Tests

Run the complete automated test suite from the repository root:

```shell
dotnet test KnownFirst.Tests/KnownFirst.Tests.csproj
```

## Privacy direction

KnownFirst is designed as a local-first application:

- Imported texts remain on the device.
- Vocabulary data remains on the device.
- No account is currently required.
- No analytics or advertising are currently included.

This direction describes the current architecture and is not a promise of final legal compliance.

## Roadmap

- Milestone 1: application foundation
- Milestone 2: text import and known/unknown review
- Milestone 3: word preparation and basic learning
- Later: offline dictionaries, spaced repetition, backup, Google Drive synchronization, typing mode, and statistics

## Contributing

Contribution guidelines will be expanded later. Issues and pull requests are welcome once the MVP architecture stabilizes.

## Author

Developed by Tachiguro.

## License

KnownFirst is licensed under the [Apache License 2.0](LICENSE.txt).
