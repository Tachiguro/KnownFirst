# KnownFirst Testing Guide

This document defines the test scopes, execution rules, failure policies, and structural assessment for KnownFirst.

KnownFirst currently uses a single MSTest automated test project:
`KnownFirst.Tests/KnownFirst.Tests.csproj`

Test classes do not currently use formal MSTest category attributes; filtering relies on class, namespace, or test-name patterns.

## Test Scopes

### A. FOCUSED_AUTOMATED
- **Definition:** Unit, integration, and contract tests directly affected by the current approved implementation.
- **Usage:** Used during TDD red/green development loop.
- **Filter pattern:** Filter by exact class, namespace, or test name (e.g. `dotnet test ./KnownFirst.Tests/KnownFirst.Tests.csproj --filter "FullyQualifiedName~TextAnalyzerTests"`).
- **Boundaries:** Does not include unrelated persistence, provider, or UI markup tests. Does not perform builds or launch application processes.

### B. ALL_AUTOMATED
- **Definition:** The complete `KnownFirst.Tests` project execution (unit, integration, persistence, provider, policy, and UI-contract tests).
- **Command:**
  ```powershell
  dotnet test ./KnownFirst.Tests/KnownFirst.Tests.csproj -c Debug
  ```
- **Includes:** All automated unit, policy, provider, persistence, and UI markup/contract tests in the `KnownFirst.Tests` project. (Exact test totals belong in concrete execution evidence, not in this durable contract.)
- **Does NOT include:** Windows GUI smoke-test execution, rendered GUI verification, manual GUI matrices, physical Android device testing, or application packaging.

### C. UI_CONTRACT_AUTOMATED
- **Definition:** Automated source, markup, Razor, and CSS contract checks (`KnownFirst.Tests/UiWorkflowContractTests.cs`).
- **Command:**
  ```powershell
  dotnet test ./KnownFirst.Tests/KnownFirst.Tests.csproj --filter "FullyQualifiedName~UiWorkflowContractTests"
  ```
- **Scope:** Inspects component structure, CSS classes, HTML attributes, and required markup invariants using static analysis and AngleSharp.
- **Does NOT prove:** Actual browser/WebView rendering, click interaction in a running process, focus behavior, safe-area layout, viewport correctness, or native platform behavior.

### D. WINDOWS_GUI_AUTOMATED
- **Definition:** Automated Windows GUI test scenarios using a structured launcher.
- **Startup smoke test (current):**
  ```powershell
  .\scripts\knownfirst.ps1 -Action GuiTest -GuiScenario StartupSmoke -Configuration Debug
  ```
  OR (interactive menu option 2.1 / 2.2)
- **Behavior:** Wraps the Windows smoke-test verification with structured artifact reporting and disposable profile isolation. Performs restore, build, automated tests, Windows Debug launch, process/window/startup-event observation, and clean shutdown. Writes JSON summary, step log, and compressed report package to `artifacts/gui-tests/windows/runs/<timestamp-scenarioid>/`. Uses unique disposable application profile under `artifacts/gui-tests/windows/profiles/<run-id>/` to prevent corruption of user data.
- **Boundaries:** Does NOT click controls, send input, or perform interaction workflows. Does not capture rendered pixels or compare layouts. No AI analysis, import automation, or token assertions.
- **Branch reconciliation:** This package originates from baseline b5e4b05 (Archive-v2 / Slice 2 merge). KF-MEANING-001 Slice 3 exists on separate branch feature/meaning-centric-multi-sense-preparation-v1 (commit a51b0e8) and must be reconciled with master before merging this GUI-test branch.

### E. MANUAL_WINDOWS_GUI
- **Definition:** Manual visual and interaction testing on Windows using disposable synthetic data.
- **Reference:** Governed by [docs/GUI_TEST_MATRIX.md](GUI_TEST_MATRIX.md).
- **Boundaries:** Requires launching application and visual evidence. Does not run automatically after UI implementation. Screenshots remain outside repository.

### F. MANUAL_ANDROID_GUI
- **Definition:** Separate explicitly authorized physical device or emulator test package.
- **Reference:** Governed by [docs/BETA_TESTING.md](BETA_TESTING.md).
- **Boundaries:** Requires recording exact build identity, device/emulator model, OS version, navigation mode, language, and theme. No `pm clear`, app uninstall, data reset, or real-data use without separate explicit authorization. Never inferred from "all automated tests".

### G. FULL_VALIDATION
- **Definition:** Validates automated tests and required builds (Windows Debug/Release, Android Debug/Release). Does NOT run rendered GUI tests or manual verification.
- **Command:**
  ```powershell
  .\scripts\knownfirst.ps1 -Action ValidateAll
  ```
  OR (interactive menu option 6)
- **Includes:** All automated tests, Windows Debug, Windows Release, Android Debug build validation, and Android Release build validation.
- **Does NOT include:** Rendered GUI test execution, manual interaction testing, or Android package creation.

## Test-Only Failure Policy

When executing in `TEST_ONLY` mode:
1. **Never modify production code or test code.**
2. **Report the exact failing scope** and test output.
3. **Classify the failure:**
   - Expected TDD red result (missing feature intended by approved plan)
   - Product regression (previously working feature broken)
   - Broken test (invalid assertion or fixture)
   - Environment/tooling failure (missing SDK, locked file, build error)
   - Unrelated pre-existing failure
4. **Stop after reporting.** Do not attempt automatic code fixes.

## Current Test Organization Assessment

The current `KnownFirst.Tests` project organization has been assessed as follows:
- `TextAnalyzerTests.cs` is a well-focused text-analysis test group.
- `MvpCorePolicyTests.cs` currently combines several unrelated policy, review, navigation, and JSON contract concerns.
- `UiWorkflowContractTests.cs` is an automated markup/contract group, not a rendered GUI test.
- `KnownFirst.Tests.csproj` currently links production code, UI files, scripts, and documentation into a single test project.

### Future Non-Blocking Refactoring Candidates
When test-organization refactoring is eventually authorized, candidate split classes include:
- `ReviewActionPolicyTests`
- `PrimaryNavigationPolicyTests`
- `MeaningPreviewPolicyTests`
- `ProviderFormRelationPolicyTests`
- `LexicalLookupOutcomePolicyTests`
- `LexicalJsonContractTests`

*Note: This documentation package does not perform any test refactoring. Any future test-organization package must begin with its own `PLAN_ONLY` phase. Creating multiple test projects is not currently recommended.*
