# KnownFirst Beta 7 GUI and Workflow Test Matrix

## Purpose and verification boundary

This is the repeatable manual visual and workflow matrix for Gemini and human testers. It complements automated unit and contract tests. A passing build or contract test does not prove native rendering, safe-area behavior, focus behavior, or visual correctness.

Use a disposable test installation with synthetic, non-sensitive content. Do not clear, overwrite, migrate, or otherwise operate on a real user database. Never confirm an automated full-data reset. Do not use uninstall or `pm clear` as test setup. Automated tests must use offline fixtures or fake providers and must never issue live Wikimedia requests. A consented manual network check may be recorded separately.

Store screenshots and recordings outside the repository. Record the build identity, commit, platform, OS version, theme, UI language, viewport or device, and result for every run.

## Execution protocol

Run every state at every required viewport. For S17 and S18 at desktop widths, record the expected persistent-sidebar variant instead of marking the row skipped. Use the exact viewport where the harness supports it; for a physical device, record the actual pixel and density-independent dimensions.

Run the complete matrix once in English with System appearance. In addition, repeat D2, M2, and M4 in both English and German and in both Light and Dark. Check System appearance on Windows and Android. Rows marked `Yes` under Android retest must also be exercised on a physical Android device with gesture navigation and, when available, three-button navigation.

Use this screenshot pattern:

`b7-{state-id}-{slug}-{viewport-id}-{platform}-{theme}-{ui-language}.png`

Example: `b7-s09-meaning-picker-m2-android-dark-de.png`. Screenshot files are evidence artifacts and must remain outside the repository.

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

## State setup, action, and visible result

The state ID joins this table to the visual-check table below. Every row therefore defines Setup, Action, Expected visible state, Screenshot, and Android retest requirements.

| ID | State | Setup | Action | Expected visible state | Screenshot | Android retest |
| --- | --- | --- | --- | --- | --- | --- |
| S01 | Home | Open a disposable profile with no active review; seed non-zero counts for at least two dashboard cards. | Navigate to Home, wait for loading to finish, then switch through the required language and theme variants. | Localized title and subtitle, four workflow actions with valid enabled or explained-disabled states, and five statistics cards are visible; desktop has a full-height sidebar and compact widths have one mobile header. | `b7-s01-home-{viewport-id}-{platform}-{theme}-{ui-language}.png` | Yes |
| S02 | Text Import — Empty | Ensure no review is active and open Text Import with title and text blank. | Activate Save and analyze once. | Title-required and text-required messages appear adjacent to their fields; language and lookup controls remain usable; no document or review is created. | `b7-s02-import-empty-{viewport-id}-{platform}-{theme}-{ui-language}.png` | Yes |
| S03 | Text Import — Long Text | Open Text Import and prepare synthetic text longer than the visible text area, including long German compounds and punctuation. | Paste the text, add a title, scroll inside the text area, then scroll the page without submitting. | The complete text remains editable; the text area is bounded and internally scrollable; language, lookup mode, and Save and analyze remain reachable without horizontal expansion. | `b7-s03-import-long-{viewport-id}-{platform}-{theme}-{ui-language}.png` | Yes |
| S04 | Vocabulary Review | In the disposable profile, import text that produces several candidates, one candidate with multiple long contexts, and no previous review decision. | Open Vocabulary Review, move between contexts, mark one item Unknown, and verify Undo on the next item. | Candidate, progress, collapsed metadata, highlighted context, context position, Known and Unknown actions, and Undo state remain coherent; only one candidate is displayed. | `b7-s04-vocabulary-review-{viewport-id}-{platform}-{theme}-{ui-language}.png` | Yes |
| S05 | Preparation — Mode Selection | Complete vocabulary review with at least two unknown unprepared words and no active preparation session. | Navigate to Prepare Words without choosing a mode. | Automatic online is clearly recommended, Manual is available, the batch-size explanation is localized, and Cancel is separate from both method choices. | `b7-s05-preparation-mode-{viewport-id}-{platform}-{theme}-{ui-language}.png` | Yes |
| S06 | Preparation — Online Loading | Use a disposable profile with online consent already granted and an offline fake provider configured to complete only after a controlled delay. | Choose Automatic online and hold the fake response long enough to capture the loading state. | The current candidate and context remain stable; an immediate loading indicator is visible; duplicate submission is prevented; no stale result or second candidate appears. | `b7-s06-online-loading-{viewport-id}-{platform}-{theme}-{ui-language}.png` | Yes |
| S07 | Preparation — Automatic Result | Configure the offline provider with a successful sanitized result containing a long definition, source metadata, and at least three meanings. | Complete S06 and wait for the result. | One selected meaning is readable, definition and translation presentation match the lookup mode, source details are collapsed, change-meaning is available, and product actions use normal product colors rather than DEBUG amber. | `b7-s07-automatic-result-{viewport-id}-{platform}-{theme}-{ui-language}.png` | Yes |
| S08 | Preparation — Manual Editing | Start Manual preparation, or return a controlled no-result response and choose manual entry. | Enter a long definition or translation, trigger empty-useful-answer validation once, correct it, and keep the editor open for capture. | Canonical and encountered forms are read-only, editable fields are aligned, validation is adjacent and cleared after correction, and Save and continue plus Cancel remain reachable. | `b7-s08-manual-editing-{viewport-id}-{platform}-{theme}-{ui-language}.png` | Yes |
| S09 | Preparation — Choose Another Meaning | Use the S07 result with at least three meanings and one long expandable entry. | Activate Change meaning, select a non-primary item, expand its details, then leave the dialog open. | A bounded modal/backdrop and listbox are visible; the chosen item is identifiable; long content wraps; Close is reachable; underlying content is inert and does not scroll. | `b7-s09-meaning-picker-{viewport-id}-{platform}-{theme}-{ui-language}.png` | Yes |
| S10 | Preparation — Finish | Start a preparation batch with at least two items and accept the first so the batch is partially completed. | Activate Cancel preparation (`Vorbereitung beenden`) and stop at the confirmation. | The trigger is hidden; an alert dialog explains what remains prepared; neutral Cancel precedes the destructive final action; focus starts on Cancel; pressing Enter alone does not confirm. | `b7-s10-preparation-finish-{viewport-id}-{platform}-{theme}-{ui-language}.png` | Yes |
| S11 | Learning — Answer Hidden | Prepare at least one reading-mode card with a long context and begin learning. | Open Learn and do not reveal the answer. | Progress, mode, term, context, and Reveal answer are visible; answer content and rating actions are absent; the permanently-known action remains visually separate. | `b7-s11-learning-hidden-{viewport-id}-{platform}-{theme}-{ui-language}.png` | Yes |
| S12 | Learning — Answer Visible | Continue from S11. | Activate Reveal answer and do not rate the card. | Acronym expansion when present, translation, definition, accepted aliases, and collapsed source details appear in order; Again, Hard, Good, and Easy are visible and usable. | `b7-s12-learning-visible-{viewport-id}-{platform}-{theme}-{ui-language}.png` | Yes |
| S13 | Learning — Source Details Expanded | Use the revealed answer from S12 with source project, page title, revision, attribution, and license data. | Expand Source details and leave it open. | Source metadata and license are readable without widening the page; rating controls remain reachable; the disclosure can be collapsed with keyboard or touch. | `b7-s13-source-expanded-{viewport-id}-{platform}-{theme}-{ui-language}.png` | Yes |
| S14 | Settings | Open Settings in a disposable profile with no reset confirmation and, in Debug, diagnostic logging enabled. | Scroll from UI language through appearance, preparation, learning, consent, DEBUG tools, support, build identity, and reset. | Cards have consistent rhythm; English and Deutsch remain the only UI-language choices; System, Light, and Dark are available; diagnostic actions alone are amber and labeled DEBUG. | `b7-s14-settings-{viewport-id}-{platform}-{theme}-{ui-language}.png` | Yes |
| S15 | Settings — Reset Confirmation | Open Settings and scroll away from the Reset section first. | Activate Reset all application data but do not confirm it. | The confirmation is automatically revealed; the reset trigger is hidden; Cancel is focused and precedes the destructive final button; Escape cancels and returns focus. Never confirm the reset in automation. | `b7-s15-reset-confirmation-{viewport-id}-{platform}-{theme}-{ui-language}.png` | Yes |
| S16 | Diagnostics | Run the Debug configuration with a disposable profile containing review, preparation, learning, and cache rows. Release must not expose this route. | Open Diagnostics, adjust artificial time by one hour, refresh, and leave the DEBUG tools section visible. | DEBUG labels, amber controls with dark text, artificial UTC time and offset, and diagnostic tables are visible; no device clock or stored due date changes; Release contains no page or clickable placeholder. | `b7-s16-diagnostics-{viewport-id}-{platform}-{theme}-{ui-language}.png` | Yes |
| S17 | Burger Menu — Open | At T1 and M1–M4, scroll a long route before opening the menu. At D1–D3, use the persistent sidebar variant. | Activate the burger once; on desktop, verify no burger is offered. | Compact widths show a fixed overlay drawer and backdrop above unchanged content; the drawer contains every reachable navigation item. Desktop retains the full-height sidebar without an overlay. | `b7-s17-menu-open-{viewport-id}-{platform}-{theme}-{ui-language}.png` | Yes |
| S18 | Burger Menu — Closed | Continue from S17 and note the underlying page scroll position. | Close by second burger activation, backdrop, Escape, route selection, and Android Back in separate repetitions. | The drawer and backdrop are gone, underlying scroll position is preserved unless a route was selected, focus is visible, and Android Back closes the drawer before navigating back. Desktop remains unchanged. | `b7-s18-menu-closed-{viewport-id}-{platform}-{theme}-{ui-language}.png` | Yes |

## State-level visual checks

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

## Cross-state shell, scrolling, and Android checks

For every applicable row:

1. Verify the desktop sidebar remains green and full-height, with navigation at the top and attribution plus build identity reachable at the bottom.
2. Verify compact layouts show one header, a fitting localized title, and content below the status-bar safe area.
3. Exercise wheel, trackpad, touch, Page Up/Down, arrow keys, Home, and End where the control supports them.
4. Verify hidden scrollbar chrome does not disable scrolling or text-area selection.
5. On Android, verify the last content can be exposed above fixed actions and the system navigation inset.
6. Verify there is no black or background-colored dead strip between content, actions, and safe areas.

## End-to-end scenarios

Use offline provider fixtures for automated portions. A manual network repetition requires explicit consent and must be recorded as manual.

### A. House — English definition

- Setup: disposable empty profile; English source; Definition mode; offline `house` fixture.
- Steps: import a short sentence containing “house,” mark House Unknown, choose automatic preparation, accept a definition, then open Learn.
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

## Result recording

For every state and viewport combination, record:

- state ID and viewport ID;
- platform, OS version, actual device dimensions, navigation mode, theme, and UI language;
- build identity and commit;
- pass, fail, blocked, or breakpoint-not-applicable result;
- concise reproduction steps and expected versus actual behavior for failures;
- evidence path outside the repository;
- whether the failure blocks Beta 7 acceptance and whether an Android retest remains open.

Do not mark a row visually passed unless its required visible state, overlap, spacing, scrolling, and focus behavior were directly inspected.
