# KnownFirst Testing Guide

This document defines the test scopes, execution rules, failure policies, and structural assessment for KnownFirst.

KnownFirst currently uses a single MSTest automated test project:
`KnownFirst.Tests/KnownFirst.Tests.csproj`

Test classes do not currently use formal MSTest category attributes; filtering relies on class, namespace, or test-name patterns.

## Test Scopes

### A. FOCUSED_AUTOMATED
- **Definition:** Unit and integration tests directly affected by the current approved implementation.
- **Usage:** Used during TDD red/green development loop.
- **Filter pattern:** Filter by exact class, namespace, or test name (e.g. `dotnet test ./KnownFirst.Tests/KnownFirst.Tests.csproj --filter "FullyQualifiedName~TextAnalyzerTests"`).
- **Boundaries:** Does not include unrelated contract, persistence, provider, or UI markup tests. Does not perform builds or launch application processes.

### B. ALL_AUTOMATED
- **Definition:** The complete `KnownFirst.Tests` project execution.
- **Command:**
  ```powershell
  dotnet test ./KnownFirst.Tests/KnownFirst.Tests.csproj -c Debug
  ```
- **Includes:** All automated unit, policy, provider, persistence, and UI markup/contract tests in the project. (Exact test totals belong in concrete execution evidence, not in this durable contract.)
- **Does NOT include:** Windows smoke-test execution, rendered GUI verification, manual GUI matrices, physical Android device testing, or application packaging.

### C. UI_CONTRACT_AUTOMATED
- **Definition:** Automated source, markup, Razor, and CSS contract checks (`KnownFirst.Tests/UiWorkflowContractTests.cs`).
- **Command:**
  ```powershell
  dotnet test ./KnownFirst.Tests/KnownFirst.Tests.csproj --filter "FullyQualifiedName~UiWorkflowContractTests"
  ```
- **Scope:** Inspects component structure, CSS classes, HTML attributes, and required markup invariants using static analysis and AngleSharp.
- **Does NOT prove:** Actual browser/WebView rendering, click interaction in a running process, focus behavior, safe-area layout, viewport correctness, or native platform behavior.

### D. WINDOWS_SMOKE
- **Definition:** Composite verification script executed from repository root:
  ```powershell
  powershell -NoProfile -ExecutionPolicy Bypass -File ./scripts/smoke-test-windows.ps1
  ```
- **Composite operations:** Clears generated output, performs plain restore, builds solution and Windows target, runs automated tests, launches the Windows executable, observes process window/log event, keeps process alive for smoke interval, and closes executable.
- **Boundaries:** Runs **only** upon explicit Windows smoke-test request. Not equivalent to plain Windows Debug build, automated unit tests, or manual GUI matrix testing.

### E. MANUAL_WINDOWS_GUI
- **Definition:** Manual visual and interaction testing on Windows using disposable synthetic data.
- **Reference:** Governed by [docs/GUI_TEST_MATRIX.md](GUI_TEST_MATRIX.md).
- **Boundaries:** Requires launching application and visual evidence. Does not run automatically after UI implementation. Screenshots remain outside repository.

### F. MANUAL_ANDROID_GUI
- **Definition:** Separate explicitly authorized physical device or emulator test package.
- **Reference:** Governed by [docs/BETA_TESTING.md](BETA_TESTING.md).
- **Boundaries:** Requires recording exact build identity, device/emulator model, OS version, navigation mode, language, and theme. No `pm clear`, app uninstall, data reset, or real-data use without separate explicit authorization. Never inferred from "all automated tests".

### G. FULL_VALIDATION
- **Definition:** A user-approved, explicitly enumerated combination of test and build scopes.
- **Rules:** Before execution, the agent must list every included component (e.g., all automated tests + Windows Debug build + manual GUI check). Never inferred from a normal implementation-complete statement.

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
