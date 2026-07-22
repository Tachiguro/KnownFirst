# Beta 8 Android Release handoff

**Date:** 2026-07-22
**Released version:** `1.0.0-beta.8`
**Release source:** `29aff385f2c3cabca49b70bd011bf4c09808df6d`

## Starting problem

The optimized, trimmed, AOT-compiled Android Release could start but terminate
during automatic online vocabulary lookup. Debug or non-AOT behavior was not a
sufficient reproduction boundary, so the fix had to preserve the actual
Release configuration.

## First cause: JSON metadata under AOT

Persisting and reopening lexical lookup results, aliases, and diagnostic
records used reflection-dependent `System.Text.Json` calls. The Android AOT
path could not reliably discover the required metadata.

Commit `f1f1891eaf42ad9e7610afc2e9f796771fed27e7` introduced explicit
source-generated contexts:

- `KnownFirst.Core/Preparation/LexicalJsonSerializerContext.cs`
- `Services/Diagnostics/DiagnosticsJsonSerializerContext.cs`

It routed the affected cache, preparation, learning, text-review, and logging
serialization through generated type information and added round-trip and
workflow regression tests. Provider failures were also contained so a lookup
failure produces a controlled state.

## Second cause: AngleSharp selector path under AOT

After the JSON cause was removed, a second crash remained in the Wiktionary
HTML parser. The affected path used AngleSharp CSS selector matching for
classes and attributes that was unsafe in the tested AOT environment.

Commit `29aff385f2c3cabca49b70bd011bf4c09808df6d` replaced that path with
explicit DOM traversal and direct class/attribute checks. It added
`KnownFirst.Tests/Fixtures/Wiktionary/parser-aot-cases.html` and parser
regression coverage.

## Verification

The release brief confirms:

- the signed AAB was built from `29aff385`;
- the application was exercised on a physical Android device;
- automatic online lookup completed without the process terminating;
- prepared content could enter learning;
- reset, restart, and continuation behavior were exercised; and
- the AAB was accepted and tested through Google Play Internal Testing.

The repository does not contain device model, OS version, AAB checksum,
screenshots, or a complete manual-results matrix. Preserve that distinction
when reporting the release.

The source-equivalent merge tree later passed 389 automated tests with 0
failures and 0 skipped tests.

## Commit map

| Purpose | Commit |
| --- | --- |
| Beta 7 source before Release/AOT hotfixes | `ea9ba60ddb7f804c7eb31e82c945dd27defe42ee` |
| Source-generated JSON and lookup containment | `f1f1891eaf42ad9e7610afc2e9f796771fed27e7` |
| Parser AOT fix and Beta 8 release source | `29aff385f2c3cabca49b70bd011bf4c09808df6d` |
| Pull-request merge to `master` | `956e71895cf141805c8c24f7d32691075d439730` |
| Annotated release tag | `v1.0.0-beta.8` -> `29aff385` |

## Relevant files

- `KnownFirst.csproj`
- `KnownFirst.Core/Preparation/LexicalJsonSerializerContext.cs`
- `Services/Diagnostics/DiagnosticsJsonSerializerContext.cs`
- `Services/Lexical/LexicalCacheRepository.cs`
- `Services/Lexical/LexicalEnrichmentService.cs`
- `Services/Lexical/WiktionaryHtmlParser.cs`
- `Services/Study/PreparationService.cs`
- `Services/Study/LearningService.cs`
- `Services/TextReviewService.cs`
- `KnownFirst.Tests/MvpCorePolicyTests.cs`
- `KnownFirst.Tests/StudyWorkflowServiceTests.cs`
- `KnownFirst.Tests/WiktionaryProviderTests.cs`
- `KnownFirst.Tests/Fixtures/Wiktionary/parser-aot-cases.html`

## Open next steps

1. Complete project-governance documentation.
2. Begin Data Safety v1 requirements.
3. Audit schema and migration compatibility.
4. Only then design versioned backup and restore.

Do not start backup/restore implementation from this handoff alone.

## Artifact hygiene

Do not carry temporary probe packages, logs, local databases, signing material,
screenshots, or uncommitted experimental parser fixtures into later branches.
Only the committed release source, tests, documentation, and explicit external
release evidence are authoritative.
