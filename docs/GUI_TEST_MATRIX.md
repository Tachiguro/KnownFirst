# KnownFirst GUI and Workflow Test Matrix

## Purpose and verification boundary

This is the repeatable manual visual and workflow matrix for human testers. Every row is executed and recorded manually; see "Automation status" below for the exact current automation boundary. It complements automated unit and contract tests. A passing build or contract test does not prove native rendering, safe-area behavior, focus behavior, or visual correctness.

This is a living document, not tied permanently to any single beta. States accumulate as features ship; a state is retired only when the feature it covers is removed from the product.

**Historical provenance (context only, not a scope boundary).** The original state set S01-S18 originated during Beta 7 hardening. States S19-S29 originated with the Beta 10 portable data export/import and What's New coverage. Those origins record where each block came from; they do not limit what this matrix covers. Later states extend the same living matrix as product workflows ship, and no beta number defines its current scope.

Use a disposable test installation with synthetic, non-sensitive content. Do not clear, overwrite, migrate, or otherwise operate on a real user database. Never confirm an automated full-data reset against a production profile — destructive reset confirmation may be exercised only inside a separately identified disposable test package/profile created for that purpose. Do not use uninstall or `pm clear` as test setup. Automated tests must use offline fixtures or fake providers and must never issue live Wikimedia requests. A consented manual network check may be recorded separately.

Store screenshots and recordings outside the repository. Record the build identity, commit, platform, OS version, theme, UI language, viewport or device, and result for every run.

## Automation status

**No row in this matrix is currently mapped to, or may be reported as, an automated matrix result.** Every state below is verified manually and stays manual unless a future package explicitly defines an automation mapping, executes it, and records its evidence.

Two automation assets exist in the repository and must not be confused with matrix coverage:

- The standard Windows **StartupSmoke** launcher (`.\scripts\knownfirst.ps1 -Action GuiTest -GuiScenario StartupSmoke -Configuration Debug`) produces process, window, and startup-event evidence. It executes no matrix interaction: it does not click controls, send input, run workflows, or capture rendered layouts.
- A separate, source-present Windows UIA interaction harness (`scripts/gui-tests/windows/Invoke-GuiTestRun.ps1`) exists with its own scenario registry. It is **not** part of the standard `knownfirst.ps1 -Action GuiTest` routing, and it depends on the external `winapp` CLI.

Neither harness scenario execution nor any `winapp` capability was executed or validated when this section was last reconciled. **Source presence is not proof of a passing scenario, and it is not matrix coverage.** See [TESTING.md](TESTING.md) scope D for the full contract.

Automated GUI validation is [ROADMAP.md](ROADMAP.md) **priority 16** ("Automated GUI validation"), which plans an Android-first deterministic automation effort (Appium/UiAutomator2) plus Windows automation launcher integration.

A passing unit, contract, StartupSmoke, or isolated UIA run is never sufficient to report a row here as passed. A row may only be reported as passed after an explicitly mapped run against that row, with its evidence recorded per "Result recording" below.

## Execution protocol

Run every state at every required viewport. For S17 and S18 at desktop widths, record the expected persistent-sidebar variant instead of marking the row skipped. Use the exact viewport where the harness supports it; for a physical device, record the actual pixel and density-independent dimensions.

Run the complete matrix once in English with System appearance. In addition, repeat D2, M2, and M4 in both English and German and in both Light and Dark. Check System appearance on Windows and Android. Rows marked `Yes` under Android retest must also be exercised on a physical Android device with gesture navigation and, when available, three-button navigation.

Use this screenshot pattern (neutral prefix, not tied to a specific beta number):

`kf-{state-id}-{slug}-{viewport-id}-{platform}-{theme}-{ui-language}.png`

Example: `kf-s09-meaning-picker-m2-android-dark-de.png`. Screenshot files are evidence artifacts and must remain outside the repository.

## Required viewports

| ID | Viewport | Primary coverage |
| --- | --- | --- |
| D1 | 1440 × 900 | Wide Windows desktop |
| D2 | 1280 × 900 | Standard Windows desktop |
| D3 | 960 × 900 | Narrow Windows desktop |
| T1 | 600 × 900 | Compact tablet and drawer layout |
| M1 | 480 × 900 | Large mobile layout |
| M2 | 412 × 915 | Common Android portrait |
| M3 | 360 × 800 | Narrow Android portrait |
| M4 | 320 × 700 | Minimum supported layout |

## Core states (S01-S18)

The state ID joins this table to the visual-check table below. Every row therefore defines Setup, Action, Expected visible state, Screenshot, and Android retest requirements.

| ID | State | Setup | Action | Expected visible state | Screenshot | Android retest |
| --- | --- | --- | --- | --- | --- | --- |
| S01 | Home | Open a disposable profile with no active review; seed non-zero counts for at least two dashboard cards. | Navigate to Home, wait for loading to finish, then switch through the required language and theme variants. | Localized title and subtitle, four workflow actions with valid enabled or explained-disabled states, and five statistics cards are visible; desktop has a full-height sidebar and compact widths have one mobile header. | `kf-s01-home-{viewport-id}-{platform}-{theme}-{ui-language}.png` | Yes |
| S02 | Text Import — Empty | Ensure no review is active and open Text Import with title and text blank. | Activate Save and analyze once. | Title-required and text-required messages appear adjacent to their fields; language and lookup controls remain usable; no document or review is created. | `kf-s02-import-empty-{viewport-id}-{platform}-{theme}-{ui-language}.png` | Yes |
| S03 | Text Import — Long Text | Open Text Import and prepare synthetic text longer than the visible text area, including long German compounds and punctuation. | Paste the text, add a title, scroll inside the text area, then scroll the page without submitting. | The complete text remains editable; the text area is bounded and internally scrollable; language, lookup mode, and Save and analyze remain reachable without horizontal expansion. | `kf-s03-import-long-{viewport-id}-{platform}-{theme}-{ui-language}.png` | Yes |
| S04 | Vocabulary Review | In the disposable profile, import text that produces several candidates, one candidate with multiple long contexts, and no previous review decision. | Open Vocabulary Review, move between contexts, mark one item Unknown, and verify Undo on the next item. | Candidate, progress, collapsed metadata, highlighted context, context position, Known and Unknown actions, and Undo state remain coherent; only one candidate is displayed. | `kf-s04-vocabulary-review-{viewport-id}-{platform}-{theme}-{ui-language}.png` | Yes |
| S05 | Preparation — Mode Selection | Complete vocabulary review with at least two unknown unprepared words and no active preparation session. | Navigate to Prepare Words without choosing a mode. | Automatic online is clearly recommended, Manual is available, the batch-size explanation is localized, and Cancel is separate from both method choices. | `kf-s05-preparation-mode-{viewport-id}-{platform}-{theme}-{ui-language}.png` | Yes |
| S06 | Preparation — Online Loading | Use a disposable profile with online consent already granted and an offline fake provider configured to complete only after a controlled delay. | Choose Automatic online and hold the fake response long enough to capture the loading state. | The current candidate and context remain stable; an immediate loading indicator is visible; duplicate submission is prevented; no stale result or second candidate appears. | `kf-s06-online-loading-{viewport-id}-{platform}-{theme}-{ui-language}.png` | Yes |
| S07 | Preparation — Automatic Result | Configure the offline provider with a successful sanitized result containing a long definition, source metadata, and at least three meanings. | Complete S06 and wait for the result. | One selected meaning is readable, definition and translation presentation match the lookup mode, source details are collapsed, change-meaning is available, and product actions use normal product colors rather than DEBUG amber. | `kf-s07-automatic-result-{viewport-id}-{platform}-{theme}-{ui-language}.png` | Yes |
| S08 | Preparation — Manual Editing | Start Manual preparation, or return a controlled no-result response and choose manual entry. | Enter a long definition or translation, trigger empty-useful-answer validation once, correct it, and keep the editor open for capture. | Canonical and encountered forms are read-only, editable fields are aligned, validation is adjacent and cleared after correction, and Save and continue plus Cancel remain reachable. | `kf-s08-manual-editing-{viewport-id}-{platform}-{theme}-{ui-language}.png` | Yes |
| S09 | Preparation — Choose Another Meaning | Use the S07 result with at least three meanings and one long expandable entry. | Activate Change meaning, select a non-primary item, expand its details, then leave the dialog open. | A bounded modal/backdrop and listbox are visible; the chosen item is identifiable; long content wraps; Close is reachable; underlying content is inert and does not scroll. | `kf-s09-meaning-picker-{viewport-id}-{platform}-{theme}-{ui-language}.png` | Yes |
| S10 | Preparation — Finish | Start a preparation batch with at least two items and accept the first so the batch is partially completed. | Activate Cancel preparation (`Vorbereitung beenden`) and stop at the confirmation. | The trigger is hidden; an alert dialog explains what remains prepared; neutral Cancel precedes the destructive final action; focus starts on Cancel; pressing Enter alone does not confirm. | `kf-s10-preparation-finish-{viewport-id}-{platform}-{theme}-{ui-language}.png` | Yes |
| S11 | Learning — Answer Hidden | Prepare at least one reading-mode card with a long context and begin learning. | Open Learn and do not reveal the answer. | Progress, mode, term, context, and Reveal answer are visible; answer content and rating actions are absent; the permanently-known action remains visually separate. | `kf-s11-learning-hidden-{viewport-id}-{platform}-{theme}-{ui-language}.png` | Yes |
| S12 | Learning — Answer Visible | Continue from S11. | Activate Reveal answer and do not rate the card. | Acronym expansion when present, translation, definition, accepted aliases, and collapsed source details appear in order; Again, Hard, Good, and Easy are visible and usable. | `kf-s12-learning-visible-{viewport-id}-{platform}-{theme}-{ui-language}.png` | Yes |
| S13 | Learning — Source Details Expanded | Use the revealed answer from S12 with source project, page title, revision, attribution, and license data. | Expand Source details and leave it open. | Source metadata and license are readable without widening the page; rating controls remain reachable; the disclosure can be collapsed with keyboard or touch. | `kf-s13-source-expanded-{viewport-id}-{platform}-{theme}-{ui-language}.png` | Yes |
| S14 | Settings | Open Settings in a disposable profile with no reset confirmation and, in Debug, diagnostic logging enabled. | Scroll from UI language through appearance, preparation, learning, consent, portable data, DEBUG tools, support, build identity, and reset. | Cards have consistent rhythm; the UI-language choices are exactly System, English, Deutsch, and Русский; System, Light, and Dark appearance choices are available; diagnostic actions alone are amber and labeled DEBUG. Record that the Support KnownFirst and Report a bug controls still render as placeholders that only show a "coming soon" notice: this is a known unresolved Release and every-AAB blocker (see [ROADMAP.md](ROADMAP.md)), not accepted finished functionality, and this matrix only records their presence — it neither implements nor removes them. | `kf-s14-settings-{viewport-id}-{platform}-{theme}-{ui-language}.png` | Yes |
| S15 | Settings — Reset Confirmation | Open Settings on a disposable test profile/package and scroll away from the Reset section first. | Activate Reset all application data but do not confirm it. | The confirmation is automatically revealed; the reset trigger is hidden; Cancel is focused and precedes the destructive final button; Escape cancels and returns focus. Never confirm the reset in automation or against a non-disposable profile. | `kf-s15-reset-confirmation-{viewport-id}-{platform}-{theme}-{ui-language}.png` | Yes |
| S16 | Diagnostics | Run the Debug configuration with a disposable profile containing review, preparation, learning, and cache rows. Release must not expose this route. | Open Diagnostics, adjust artificial time by one hour, refresh, and leave the DEBUG tools section visible. | DEBUG labels, amber controls with dark text, artificial UTC time and offset, and diagnostic tables are visible; no device clock or stored due date changes; Release contains no page or clickable placeholder. | `kf-s16-diagnostics-{viewport-id}-{platform}-{theme}-{ui-language}.png` | Yes |
| S17 | Burger Menu — Open | At T1 and M1-M4, scroll a long route before opening the menu. At D1-D3, use the persistent sidebar variant. | Activate the burger once; on desktop, verify no burger is offered. | Compact widths show a fixed overlay drawer and backdrop above unchanged content; the drawer contains every reachable navigation item. Desktop retains the full-height sidebar without an overlay. | `kf-s17-menu-open-{viewport-id}-{platform}-{theme}-{ui-language}.png` | Yes |
| S18 | Burger Menu — Closed | Continue from S17 and note the underlying page scroll position. | Close by second burger activation, backdrop, Escape, route selection, and Android Back in separate repetitions. | The drawer and backdrop are gone, underlying scroll position is preserved unless a route was selected, focus is visible, and Android Back closes the drawer before navigating back. Desktop remains unchanged. | `kf-s18-menu-closed-{viewport-id}-{platform}-{theme}-{ui-language}.png` | Yes |

### State-level visual checks (S01-S18)

Every row defines the required Overlap check, Spacing check, Scroll check, and Focus check. Failure of any check fails that state and viewport combination.

| ID | Overlap check | Spacing check | Scroll check | Focus check |
| --- | --- | --- | --- | --- |
| S01 | Header/sidebar, action cards, statistics, attribution, and bottom inset never cover one another. | Hero-to-actions and action-to-statistics gaps follow one consistent 4/8/12/16/24/32/48 rhythm. | Page reaches the last statistic and attribution with wheel, touch, and keyboard; no horizontal scroll. | Tab order follows enabled workflow actions then page content; focus ring is never clipped. |
| S02 | Validation messages do not cover inputs, selects, or submit action, including with the mobile keyboard. | Labels, inputs, errors, and language controls retain equal field-group gaps. | The page reaches the submit action while the empty text area remains independently scrollable. | Submission returns useful focus to the first invalid field or leaves a clearly visible focus path to it. |
| S03 | The long text area never covers language controls or the submit action. | Text-area edges align with title and selectors; help and controls do not crowd. | Mouse, touch, selection drag, and keyboard scroll work inside the text area; page scrolling resumes outside it. | Focus remains visible at the start and end of long content and when tabbing out of the text area. |
| S04 | Context content never sits behind the fixed action area or Android navigation inset. | Progress, candidate, metadata, context, and action sections use consistent vertical gaps. | Every context and the final content line can be exposed above actions; no horizontal scroll. | Context buttons and Known/Unknown follow logical order; saving does not lose visible focus unexpectedly. |
| S05 | Method cards and Cancel never overlap or truncate at M4. | Both method cards have equal internal padding and a clear gap from Cancel. | All explanatory text and actions are reachable without nested dead scrolling. | Automatic online, Manual, then Cancel receive visible focus in order. |
| S06 | Spinner/status does not cover the candidate, context, or action area. | Loading status stays adjacent to the operation it describes with no layout jump. | Existing context remains scrollable while duplicate actions are disabled. | Focus remains on the initiating control or a visible status relationship; disabled controls are not refocused. |
| S07 | Long meanings and source summary do not overlap preparation actions or viewport edges. | Meaning blocks, source disclosure, and actions retain consistent card spacing. | Long sanitized content and final actions are reachable; no content widens the page. | Change meaning, source disclosure, and accept actions have visible, logical focus order. |
| S08 | Mobile keyboard, validation, and bottom actions do not cover the active field. | Every label/input pair and the validation message use the same field rhythm. | Editor and page scroll cooperate so the focused field and final actions can both be revealed. | Failed validation focuses a useful field; correction clears the error without a focus jump. |
| S09 | Dialog stays inside viewport and safe areas; backdrop covers but does not move the page. | Header, meaning rows, expandable details, and Close use consistent dialog padding. | Meaning list scrolls internally to its last item while the background stays locked. | Focus enters the dialog, Escape/Android Back closes it, and focus returns to Change meaning. |
| S10 | Confirmation content and both actions remain above the bottom inset and never cover preparation content. | Message-to-actions gap matches other destructive confirmations; neutral and danger actions are distinct. | Automatic reveal scrolls only as far as needed; all dialog content remains reachable at M4. | Cancel receives initial focus; Escape restores the original trigger; Enter cannot invoke the final danger action. |
| S11 | Context and Reveal answer are not hidden behind the learning action area. | Progress, prompt, context, and action bar follow a compact consistent rhythm. | Long context reaches its end above actions with touch and keyboard. | Reveal answer is the primary next focus target; context navigation remains ordered. |
| S12 | Revealed answer and four ratings never overlap each other or system navigation. | Answer sections and rating controls use equal gaps; no unexplained dead area appears. | Entire answer, source summary, and ratings are reachable without horizontal scroll. | Focus proceeds through disclosures and ratings; shortcuts do not trigger while a destructive confirmation is open. |
| S13 | Expanded metadata does not cover or push ratings outside the reachable scroll range. | Source rows remain compact and clearly separated from answer content. | Expanded details scroll with the workflow area and can be fully exposed above actions. | Disclosure summary retains focus and toggles with Enter or Space. |
| S14 | Cards, choice grids, support actions, diagnostics, and reset never overlap the shell or each other. | Card padding and inter-card gaps remain consistent from first setting to Reset. | Full page reaches Reset and attribution; selectors do not create nested horizontal scrolling. | Tab order follows visual order; changing language/theme preserves a visible usable focus path. |
| S15 | Confirmation and reset feedback fit above Android navigation and do not cover adjacent cards. | Confirmation padding matches other destructive dialogs and does not create an oversized danger block. | Reveal uses nearest scrolling; cancelling leaves Settings at a usable position. | Cancel is focused first; Escape and Cancel restore focus; Enter is blocked on the final destructive action. |
| S16 | DEBUG controls, time status, and wide tables remain bounded; tables may scroll internally without covering controls. | Amber tool section is distinct from neutral diagnostic data sections and uses consistent gaps. | Page scroll and table horizontal scroll both remain usable; no hidden clickable Release area exists. | DEBUG actions have visible focus; disabled Make due/Reset controls are skipped appropriately. |
| S17 | Drawer and backdrop cover the content without reflow; status and navigation bars remain unobstructed. | Drawer links and DEBUG navigation entry use consistent touch-target spacing. | Drawer scrolls independently to attribution/build identity; background scroll is locked. | Focus remains within reachable drawer controls; Escape and Android Back dismiss before route history. |
| S18 | No transparent backdrop or drawer hit area remains after closing. | Closed shell returns to the same header/content spacing as before opening. | Original content scroll position is preserved for non-route closures. | Focus returns to the burger or selected route target and remains visibly outlined. |

## What's New and portable data export/import states (S19-S29)

Each state below is explicitly separated into deterministic interaction, expected application behavior, screenshot requirement, and Android physical-device requirement, as distinct from the legacy S01-S18 table shape.

| ID | State | Deterministic interaction | Expected application behavior | Screenshot requirement | Android physical-device requirement |
| --- | --- | --- | --- | --- | --- |
| S19 | What's New — Displayed | Install or update to a version with unread release notes on a disposable profile; launch the app once. | The What's New modal appears automatically exactly once, showing the current version's localized title and bullet points; underlying content is inert while the modal is open. | `kf-s19-whatsnew-shown-{viewport-id}-{platform}-{theme}-{ui-language}.png` | Yes — confirm on a physical Android install, not only Windows/emulator. |
| S20 | What's New — Dismissed, not redisplayed | Continue from S19; close the modal (Close button or Escape). | The modal closes, the acknowledged version is recorded, and relaunching the app for the same version does not redisplay it. | `kf-s20-whatsnew-dismissed-{viewport-id}-{platform}-{theme}-{ui-language}.png` | Yes |
| S21 | Portable Export — Idle | Open Settings on a disposable profile with some learning data present. | The Data Export control is visible, enabled, and the privacy/no-encryption notice is visible above it before any action is taken. | `kf-s21-export-idle-{viewport-id}-{platform}-{theme}-{ui-language}.png` | Yes |
| S22 | Portable Export — Save-dialog cancellation | Activate Data Export, then cancel the native Save dialog without choosing a location. | The app returns to Settings with no archive written and a neutral (non-error) "cancelled" feedback state; the Data Export control remains usable. | `kf-s22-export-cancelled-{viewport-id}-{platform}-{theme}-{ui-language}.png` | Yes |
| S23 | Portable Export — Successful feedback | Activate Data Export and complete the native Save dialog to a disposable synthetic location. | A success feedback message appears; the archive file exists at the chosen location and is non-empty. | `kf-s23-export-success-{viewport-id}-{platform}-{theme}-{ui-language}.png` | Yes |
| S24 | Portable Import — File selection | On a disposable **empty** installation, activate Data Import. | The native Open dialog appears filtered to `.kfarchive`; cancelling it returns to Settings with no state change. | `kf-s24-import-file-select-{viewport-id}-{platform}-{theme}-{ui-language}.png` | Yes |
| S25 | Portable Import — Confirmation | Continue from S24; select a valid archive produced in S23 (or an equivalent synthetic fixture). | A confirmation dialog shows the selected file's name and states that import proceeds only because the installation is empty; Cancel and confirm actions are both visible. | `kf-s25-import-confirm-{viewport-id}-{platform}-{theme}-{ui-language}.png` | Yes |
| S26 | Portable Import — Cancellation | Continue from S25; activate Cancel instead of confirming. | The dialog closes, no data is imported, and the installation remains empty; Settings returns to its prior state. | `kf-s26-import-cancelled-{viewport-id}-{platform}-{theme}-{ui-language}.png` | Yes |
| S27 | Portable Import — Blocked State | On a disposable populated installation, activate Data Import with a controlled synthetic archive/target pair whose merge preflight is blocked for a reason **other than** an active workflow — that is, the plan reports a required merge decision (`merge-requires-user-decision`) or a blocking prerequisite (`merge-blocked-by-prerequisite`). The one remaining populated-target refusal, `target-not-empty`, applies only to a legacy Schema-7 target and may be recorded here instead when such a target is available. A populated target is **not** blocked merely for being populated: the normal populated-target path is the merge preview covered by S30-S32. | No preview panel opens. Settings shows a clear blocked message stating that the archive cannot be imported right now and that nothing was changed, and the selected file is released. No preview counts, no safety copy, no writer invocation, and no target mutation occur. Existing local data is byte-for-byte unchanged. Do not record the active-workflow case here; that case is S33. | `kf-s27-import-blocked-{viewport-id}-{platform}-{theme}-{ui-language}.png` | Yes |
| S28 | Portable Import — Corrupt/unsupported archive refusal | On a disposable empty installation, attempt Data Import with a deliberately corrupted file (flipped byte) or an unsupported `formatVersion` fixture. | Import is refused with a clear validation-failure message before any mutation; the installation remains empty. | `kf-s28-import-corrupt-refused-{viewport-id}-{platform}-{theme}-{ui-language}.png` | Yes |
| S29 | Portable Import — Successful recovery | On a disposable **empty** installation, import a valid archive containing synthetic completed review/preparation/learning data. | Import succeeds, a success message appears, and the imported data is visible and usable in Review/Prepare/Learn/Settings as if created locally; no duplicate or missing rows. | `kf-s29-import-success-{viewport-id}-{platform}-{theme}-{ui-language}.png` | Yes |

## Populated-target Import states (S30-S35)

These states cover the non-destructive populated-target Import workflow. Importing into a populated installation is a normal supported path: the application computes a read-only preview first and only mutates after an explicit confirmation. S27 covers the blocked disposition and S33 the active-workflow case; S24-S26 and S29 continue to cover empty-target restore.

Every row uses synthetic, disposable profiles only. Never export from, or import into, a real user installation. None of these rows has been executed; each must be run and recorded before it may be reported as passed.

| ID | State | Setup | Deterministic interaction | Expected application behavior | Screenshot requirement | Android physical-device requirement |
| --- | --- | --- | --- | --- | --- | --- |
| S30 | Portable Import — Populated-target merge preview | Disposable profile A with synthetic completed review/preparation/learning data, exported to a `.kfarchive`. Disposable populated profile B on the same current schema, holding durable data that overlaps A only partially, so the archive contains both genuinely new items and items already present in B. | On profile B, activate Data Import, select the archive from A, and stop at the preview without confirming. | A read-only preview panel appears before any confirmation, naming the selected file and presenting the merge case distinctly from the empty-target restore case. It lists the New, Enriched, Kept as a separate variant, and Already present counts. It states that a private safety copy of the current data is created before anything changes and that the import is transactional with automatic rollback. Both a Cancel action and the merge confirmation action are offered. No mutation, no safety copy, and no writer invocation occur while the preview is open; profile B is unchanged for as long as nothing is confirmed. | `kf-s30-import-merge-preview-{viewport-id}-{platform}-{theme}-{ui-language}.png` | Yes |
| S31 | Portable Import — Merge applied | Continue directly from S30 with the preview panel open. | Activate the merge confirmation action exactly once and wait for the result. | The import independently re-validates and re-evaluates the operation before mutating, rather than trusting the preview. A private safety copy is created and validated first. A success message reports that the archive was merged into the existing data, followed by result counts in the same New / Enriched / Kept as a separate variant / Already present categories, and by the safety-copy result notice. Pre-existing local data in profile B is still present and usable, and the imported additions are visible in Review/Prepare/Learn/Settings. Record the observed counts exactly; do not assert any convergence or deduplication guarantee beyond what the result actually reports. | `kf-s31-import-merge-applied-{viewport-id}-{platform}-{theme}-{ui-language}.png` | Yes |
| S32 | Portable Import — Merge no-change | Continue from S31 on the same profile B, so its content already fully represents the archive; or prepare an equivalent archive whose content is already present in the target. | Activate Data Import, select that same archive, and stop at the panel. | The preview reports that nothing in the archive is new and states explicitly that no database changes will be made and that no safety copy is needed. No counts list is shown. The panel offers only a Close action — no confirmation action is available, so no mutating path can be reached from it. Closing the panel returns to Settings with the target unchanged: no safety copy, no writer invocation, and no duplicated rows. | `kf-s32-import-merge-no-change-{viewport-id}-{platform}-{theme}-{ui-language}.png` | Yes |
| S33 | Portable Import — Active-workflow blocked | Disposable populated profile with an incompatible workflow genuinely in progress — an active review, preparation, or learning session — plus a valid archive that would otherwise merge. | With that session still in progress, activate Data Import and select the archive. | No preview panel opens. Settings shows a blocked message stating that Data Import was refused because a review, preparation, or learning session is in progress and that nothing was changed, and inviting the tester to finish or leave that session first. The target is unchanged before any mutation could occur; no safety copy is created and the merge writer is never invoked. | `kf-s33-import-active-workflow-blocked-{viewport-id}-{platform}-{theme}-{ui-language}.png` | Yes |
| S34 | Portable Import — Stale-plan rejection | Requires a controlled synthetic fixture or a separately identified disposable test package that can change the target between preview and confirmation. A normal unaided human run cannot reliably create this condition, and the row must not be recorded as blocked merely because the condition did not occur naturally. | Open a merge preview as in S30, have the controlled fixture change the target's durable data while the preview is still open, then activate the merge confirmation action. | The writer's own in-transaction recomputation rejects the now-stale plan. A message states that the data changed since the preview was created, that the import was not applied, and that nothing was changed. No committed target mutation occurs. The safety copy created before the writer ran is deliberately retained rather than deleted, so its presence is expected and is not a failure. | `kf-s34-import-stale-plan-{viewport-id}-{platform}-{theme}-{ui-language}.png` | Yes — only inside the separately identified disposable test package. |
| S35 | Portable Import — Preview or confirmation cancellation | Disposable populated profile and a valid archive that would produce a merge preview. | Perform three separate repetitions: (a) activate Data Import and cancel the native Open dialog without choosing a file; (b) open the preview and activate Cancel; (c) open the preview and press Escape. | Repetition (a) returns to Settings with no preview panel and no state change. Repetitions (b) and (c) close the preview panel, release the selected file, and return focus to the Data Import control. In every repetition no mutation occurs, no safety copy is created, the merge writer is never invoked, and Settings remains fully usable with Data Export and Data Import still available. | `kf-s35-import-cancelled-{viewport-id}-{platform}-{theme}-{ui-language}.png` | Yes |

## Reopenable release-note history state (S36)

This state covers the Milestone 14B reopenable release-note history. The workflow currently has automated service/unit/contract and source/markup contract coverage only; that coverage proves neither rendering nor navigation and must never be reported as satisfying this row. **This row has not been executed. No runtime, rendered, or device evidence exists for it.**

| ID | State | Setup | Deterministic interaction | Expected application behavior | Screenshot requirement | Android physical-device requirement |
| --- | --- | --- | --- | --- | --- | --- |
| S36 | Release-note history — reopenable from Settings | Disposable profile on a build whose active version has an existing release-note entry. Launch once and dismiss the automatic What's New notice so its seen state is persisted. | Open Settings, locate the release-note-history action under Help & Support, and activate it. Return to Settings and activate it a second time. Then restart the application. | Help & Support exposes exactly one release-note-history action. Activating it navigates to the release-note page, whose header shows the localized release-note title rather than a "not found" title. The page lists Beta 12, Beta 11, and Beta 10 newest-first, each with its version heading and its own bullets. The second activation reopens the same complete history unchanged. After the restart the automatic What's New notice does **not** reappear, proving that opening the history neither reset nor re-triggered the one-time notice. Page header, entry spacing, and text remain usable and readable at every required viewport, with no horizontal overflow. No Support KnownFirst or Report a bug control is present. | `kf-s36-release-note-history-{viewport-id}-{platform}-{theme}-{ui-language}.png` | Yes |

## Cross-state shell, scrolling, and Android checks

For every applicable row:

1. Verify the desktop sidebar remains full-height, with navigation at the top and attribution plus build identity reachable at the bottom.
2. Verify compact layouts show one header, a fitting localized title, and content below the status-bar safe area.
3. Exercise wheel, trackpad, touch, Page Up/Down, arrow keys, Home, and End where the control supports them.
4. Verify hidden scrollbar chrome does not disable scrolling or text-area selection.
5. On Android, verify the last content can be exposed above fixed actions and the system navigation inset.
6. Verify there is no black or background-colored dead strip between content, actions, and safe areas.

## End-to-end scenarios

Use offline provider fixtures for automated portions. A manual network repetition requires explicit consent and must be recorded as manual.

### A. House — English definition

- Setup: disposable empty profile; English source; Definition mode; offline `house` fixture.
- Steps: import a short sentence containing "house," mark House Unknown, choose automatic preparation, accept a definition, then open Learn.
- Expected: English identity and coordinates remain exact; a confirmed definition creates the configured card direction; no translation is required.

### B. Tree — English to German

- Setup: disposable empty profile; English source; Translation mode targeting German; offline `tree` fixture.
- Steps: import, mark Tree Unknown, prepare automatically, choose the German meaning, accept, and reveal the learning answer.
- Expected: source, target, and explanation language remain separate; the German translation is presented without dictionary markup.

### C. Haus — German definition

- Setup: disposable empty profile; German source; Definition mode; offline `Haus` fixture.
- Steps: import a German sentence, mark Haus Unknown, prepare, accept a German definition, and start learning.
- Expected: German noun capitalization and exact source coordinates are preserved; definition mode does not request a translation target.

### D. Baum — German to English

- Setup: disposable empty profile; German source; Translation mode targeting English; offline `Baum` fixture.
- Steps: import, review, prepare, select the English meaning, accept, and learn.
- Expected: English translation is stored with German source identity; typing comparison retains the intended German case rules.

### E. Existing learning cards plus a new import

- Setup: disposable profile with at least one due review and one future card.
- Steps: import and review a new document, prepare at least one new word, then return Home and Learn.
- Expected: due cards remain available and precede new cards; the new import does not duplicate or delete existing learning data.

### F. The same text in English and German

- Setup: two synthetic documents with identical characters but intentionally different source-language selections.
- Steps: import the first as English and complete review; import the second as German.
- Expected: language-scoped identities remain distinct, original text and coordinates remain exact, and no cross-language duplicate suppression corrupts either document.

### G. Invented word

- Setup: disposable profile and an offline not-found fixture for a unique invented token.
- Steps: import, mark Unknown, run automatic preparation, observe no-result, retry once, then open manual entry.
- Expected: no fake definition appears; retry is conditional; manual entry is usable and requires at least one useful answer field.

### H. Missing online consent

- Setup: disposable profile with consent absent or revoked and at least one unprepared word.
- Steps: choose Automatic online but stop at the disclosure; repeat by selecting Manual and by cancelling.
- Expected: no request starts before consent; disclosure choices are clear; Manual and Cancel remain usable; no API key is requested.

### I. Cache hit and cache miss

- Setup: offline counting provider plus an empty disposable lexical cache.
- Steps: perform one controlled lookup to create a cache miss, repeat the same normalized request for a hit, then change language or lookup mode for a distinct miss.
- Expected: only the first and distinct requests call the provider; cache keys include the relevant languages and mode; results are sanitized consistently on hit and miss.

### J. Close and reopen the app

- Setup: manually create an active disposable workflow, non-default UI language, and non-System appearance without using reset or uninstall.
- Steps: close the application normally, reopen it, and navigate among Home, the active workflow, and Settings.
- Expected: application startup succeeds once; settings and authoritative workflow state persist; no duplicate process appears; debug artificial time resets because it is intentionally in-memory only.

### K. Portable export then import round trip

- Setup: disposable profile A with at least one completed review, one completed preparation batch, and one completed learning session; a second, separate disposable **empty** profile B.
- Steps: on profile A, export a `.kfarchive` archive (S21-S23); on profile B, import that archive (S24, S25, S29).
- Expected: profile B's Review/Prepare/Learn/Settings surfaces reflect the imported data with no duplication; profile A is unchanged by the export.

## Safety rules (binding for both manual and any future automated execution)

- Never automate, script, or otherwise operate against a real user database or a production installation profile.
- Use only synthetic, disposable test data for every state in this matrix.
- Destructive reset confirmation (S15) may be automated only inside a separately identified disposable test package/profile created specifically for that purpose; it must never run against a device's normal installation.
- Production user data must never be reset, cleared, migrated, or overwritten as a side effect of running this matrix.
- Live Wikimedia network requests remain prohibited in automated test execution; only a manually consented, separately recorded network check is permitted.
- Portable export/import states (S21-S35, scenario K) must run only against disposable profiles; never export from or import into a real user's installation as part of this matrix.

## Result recording

For every state and viewport combination, record:

- state ID and viewport ID;
- platform, OS version, actual device dimensions, navigation mode, theme, and UI language;
- build identity and commit;
- pass, fail, blocked, or breakpoint-not-applicable result;
- concise reproduction steps and expected versus actual behavior for failures;
- evidence path outside the repository;
- whether the failure blocks release acceptance and whether an Android retest remains open.

Do not mark a row visually passed unless its required visible state, overlap, spacing, scrolling, and focus behavior (or, for S19-S35, the described application behavior) were directly inspected.
