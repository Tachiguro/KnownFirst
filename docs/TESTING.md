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
- **StartupSmoke boundaries:** StartupSmoke does NOT click controls, does NOT send user input, does NOT execute interaction workflows, and does NOT capture or compare rendered layouts. It performs no AI analysis, no import automation, and no token assertions. `report.zip` is uploaded or sent to an external analysis tool only when a human explicitly chooses to do so; no AI invocation and no upload happens automatically as part of running StartupSmoke.
- **Separate Windows UIA interaction harness (source-present, separately invoked, not runtime-validated here):** `scripts/gui-tests/windows/Invoke-GuiTestRun.ps1` is a second, independent Windows runner that dispatches the scenarios declared in `scripts/gui-tests/windows/scenarios.json` (`001-import-definition-reset`, `PreparationSelectedMeaning`, `PreparationAcceptFailureRecovery`, `PreparationInvalidContextRecovery`, `DiagnosticsPopulated`, `PortableDataExportSelfImport`). Its shared library `scripts/gui-tests/windows/lib/GuiTestRunnerCore.ps1` exposes a deliberately restricted set of `winapp ui` operations (inspect, search, invoke, set-value, get-value, wait-for, window-only screenshot) and excludes click, send-keys, drag, touch, pen, hover, and full-screen capture; it redirects the application to an isolated profile via `KNOWNFIRST_GUI_TEST_ROOT` and compares real database/preferences hashes before and after a run. It writes its own report, summary, runner log, and screenshot evidence.
  - It depends on the external `winapp` CLI, which is not part of this repository.
  - It is **not** reachable through the standard `.\scripts\knownfirst.ps1 -Action GuiTest` route: that launcher accepts only the `StartupSmoke` scenario and always invokes `run-scenario-startup-smoke.ps1`. The harness is invoked separately by running `Invoke-GuiTestRun.ps1` directly with its own `-Scenario` parameter (see the script's own parameter block and examples).
  - Neither the harness nor `winapp` was executed or runtime-validated by the documentation package that recorded this section. The presence of these scripts and scenario definitions is **source-contract evidence only**; it is not proof that any scenario currently passes on the current toolchain.
  - This harness is not a completed matrix-automation system. It currently establishes automated coverage for **no** row of [docs/GUI_TEST_MATRIX.md](GUI_TEST_MATRIX.md).

### E. MANUAL_WINDOWS_GUI
- **Definition:** Manual visual and interaction testing on Windows using disposable synthetic data.
- **Reference:** Governed by [docs/GUI_TEST_MATRIX.md](GUI_TEST_MATRIX.md).
- **Boundaries:** Requires launching application and visual evidence. Does not run automatically after UI implementation. Screenshots remain outside repository.

### F. MANUAL_ANDROID_GUI
- **Definition:** Separate explicitly authorized physical device or emulator test package.
- **Reference:** Governed by [docs/BETA_TESTING.md](BETA_TESTING.md).
- **Boundaries:** Requires recording exact build identity, device/emulator model, OS version, navigation mode, language, and theme. No `pm clear`, app uninstall, data reset, or real-data use without separate explicit authorization. Never inferred from "all automated tests".

### G. ANDROID_GUI_AUTOMATION_FOUNDATION (P16-A)

- **Source-present harness:** `scripts/gui-tests/android/`; entry point `scripts/gui-tests/android/Invoke-AndroidGuiTest.ps1`; sole scenario `P16A-SettingsReleaseNotesNavigation`.
- **Architecture:** Appium 3, UiAutomator2 5, and WebdriverIO 9. Native context is reserved for lifecycle/device/full-device screenshots; WebView context is reserved for rendered CSS-selector interaction.
- **Safety contract:** dedicated package only, `com.tachiguro.knownfirst.guitest`; fail closed for every other KnownFirst package ID; already-installed application only; no build, package, install, update, uninstall, reset, or `pm clear`; no direct ADB command in the repository runner; `noReset=true`; `fullReset=false`; explicit Chromedriver executable required with no automatic acquisition; no automatic npm/dependency installation/update; loopback Appium server; the runner terminates only the server process it owns; fresh isolated application-private GUI-test profile; deterministic offline provider path.
- **Artifacts:** bounded structured summary, log, device, capability, safety, and screenshot evidence under `artifacts/gui-tests/android/runs/...`.
- **Current evidence boundary:** source/configuration contracts, profile-isolation unit behavior, runner/evidence pure logic, and static scenario/safety structure are validated. The harness has **not** been runtime-executed; it does not prove Android platform build, APK installation, Appium or UiAutomator2 compatibility, Chromedriver/WebView compatibility, rendered interaction, emulator/device behavior, screenshot correctness, or matrix coverage. `MANUAL_ANDROID_GUI` remains a distinct scope and P16-A does not replace it.
- **Recorded bounded evidence:** AndroidGuiAutomationContractTests genuine RED **0 passed / 4 failed** → GREEN **4 passed / 0 failed**; GuiTestProfileTests genuine RED **10 passed / 2 failed** → GREEN **12 passed / 0 failed**; Node evidence tests genuine RED **0 passed / 2 failed** → GREEN **3 passed / 0 failed**. The Node missing `test` import and Windows file-URL import issues were corrected before accepting RED. Affected/regression TEST_ONLY was **144 passed / 0 failed / 0 skipped** across AndroidGuiAutomationContractTests, GuiTestProfileTests, UiWorkflowContractTests, ReleaseNotesTests, AndroidPublishingScriptContractTests, and AndroidIconConfigurationTests; pure Node was **3 passed / 0 failed / 0 skipped**; pre/post `git diff --check` passed.
- **Dependency boundary:** Appium `3.6.0`, UiAutomator2 driver `5.0.7`, and WebdriverIO `9.30.1` are pinned. Lockfile metadata was generated during IMPLEMENT with `npm install --package-lock-only --ignore-scripts`; no `node_modules` was created, no third-party package code was executed, and no dependency runtime compatibility is proven.

### H. FULL_VALIDATION
- **Definition:** Validates automated tests and required builds (Windows Debug/Release, Android Debug/Release). Does NOT run rendered GUI tests or manual verification.
- **Command:**
  ```powershell
  .\scripts\knownfirst.ps1 -Action ValidateAll
  ```
  OR (interactive menu option 6)
- **Includes:** All automated tests, Windows Debug, Windows Release, Android Debug build validation, and Android Release build validation.
- **Does NOT include:** Rendered GUI test execution, manual interaction testing, or Android package creation.

## Layered Confidence Model

This model clarifies what each test layer proves and does not prove. It applies in addition to, not instead of, the concrete test scopes (A-G) above.

| Layer | Proves | Does not prove |
| --- | --- | --- |
| Pure unit tests | Isolated logic/policy correctness for the exact inputs exercised | Integration with real persistence, UI rendering, or platform behavior |
| Service/integration tests (isolated synthetic databases) | Correct behavior across service boundaries with real SQLite semantics | Rendered UI, native platform file/picker behavior, or real device conditions |
| Architecture/source-contract tests | Structural invariants (e.g. AOT/trimming-safe serialization, DI registration, script contract shape) | Runtime correctness of the logic those contracts wrap |
| Component/workflow behavior tests | A component's state transitions, service calls, and navigation given inputs, exercised in a test host | Actual browser/WebView rendering, click interaction in a running process, or visual layout |
| Rendered GUI interaction tests (only when actually executed and recorded) | That the exercised controls really rendered and really responded in a running application instance, for the exact scenario, build configuration, viewport, and environment that were executed | Complete matrix coverage, any workflow that was not executed, Android or other platform behavior, accessibility correctness, or visual/layout acceptance. Executing one scenario proves that scenario only |
| Platform/manual validation | Real device/OS behavior, visual and accessibility correctness | Nothing beyond what is manually recorded for that specific run |
| Release-script contract tests | Script argument binding, guard conditions, and static invariants | That a script's underlying build, sign, or publish operation actually succeeds end-to-end on the current toolchain |
| Optional targeted mutation testing (critical data-safety logic) | That existing tests actually fail when specific mutations are introduced into safety-critical code paths (e.g. backup/merge/export) | General code quality outside the mutated paths |

**Source or markup inspection cannot prove that a runtime button is clickable or produces its intended result.** `UI_CONTRACT_AUTOMATED` (scope C) inspects static structure only; a control can pass every markup contract check while being bound to a shared no-op or placeholder handler at runtime. Automated static detection (see "Production-Control Policy" below) narrows this gap but does not replace rendered GUI verification for controls classified as release-critical.

## Production-Control Policy

This policy implements the governing rule established in [docs/ROADMAP.md](ROADMAP.md) "Documentation Reconciliation and Release-Readiness Program": every enabled and visible actionable control in a Release build must produce a meaningful implemented outcome.

- Every enabled, visible Release action requires an implemented effect.
- A planned but unfinished feature remains documentation-only (tracked in [docs/ROADMAP.md](ROADMAP.md)) and must not be represented by any Release-visible control.
- An unfinished control must not be present in the Release DOM/component tree or accessibility tree. CSS hiding alone (e.g. `display:none`, `visibility:hidden`) is insufficient — the element must not render into the Release output at all.
- A disabled placeholder control is not permitted in an AAB unless a later, separate, explicit product decision creates a genuine user-facing unavailable-state requirement (e.g. "this action requires an internet connection" with real conditional logic) — a disabled button that exists only because the feature is unimplemented is not that case.
- Where practical, automated tests should detect: production `NotImplementedException` paths, empty event handlers, dead navigation targets (a `NavigateTo` target with no matching route), and known placeholder-handler bindings (e.g. a handler shared across multiple visually distinct controls with no differentiated effect).
- Automated static detection narrows risk but does not replace rendered GUI verification — see "Layered Confidence Model" above.

## Debug-Only UI Rules

- A diagnostic control or visual overlay (layout outline, element border, bounding box, diagnostic overlay, developer badge, or similar) requires an explicit build or diagnostic gate (e.g. the existing `DiagnosticsEnabled`-gated lexical-log actions in `Components/Pages/Settings.razor`).
- It must be clearly identifiable as diagnostic while visible in Debug/BetaDiagnostic (e.g. the existing `debug-label`/`button-debug` visual marking).
- It must be absent in Release.
- No normal Release setting may reactivate it.
- Release-contract automated coverage should confirm, where practical: absence of unfinished controls, absence of diagnostic outlines/overlays, absence of debug-only navigation entries, and absence of placeholder handlers from rendered Release-configuration workflows. Configuration-sensitive contract tests (tests that assert differently depending on build configuration) are the appropriate mechanism; static markup inspection alone cannot fully prove Release-only absence when a control's visibility is runtime-conditional — rendered GUI verification remains necessary for release-critical controls, per "Layered Confidence Model" above.

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
