# KnownFirst Beta 7 UI/UX Acceptance Criteria

## 1. Status and scope

This document originated as the binding UI and UX acceptance specification for the Beta 7 hardening work and remains an active regression baseline for current UI and UX work. It complements `KNOWNFIRST_ARCHITECTURE.md` and `MVP_WORKFLOW.md`; those newer binding workflow and architecture documents remain authoritative and prevail if a conflict exists.

The hardening work must preserve the existing KnownFirst visual identity, color palette, typography, card style, localization, and Light, Dark, and System appearance modes. It must improve consistency and reliability without introducing a new design language.

## 2. Product principles

Every screen and workflow must satisfy these principles:

- Present one clear primary action per screen or active workflow state.
- Avoid decisions that the workflow can make safely and deterministically.
- Give visible feedback for every user action.
- Do not show empty, nonfunctional, or misleading buttons.
- Do not change state silently.
- Use deliberate, repeatable spacing rather than one-off gaps.
- Keep technical details collapsed or hidden unless the user needs them.
- Place validation and errors beside their cause.
- Make destructive actions explicit and require deliberate confirmation.
- Keep frequent actions easy to reach on desktop and mobile.

## 3. Spacing and sizing system

Use only the following spacing steps unless an existing platform safe area or a documented layout constraint requires a different computed value:

- 4 px
- 8 px
- 12 px
- 16 px
- 24 px
- 32 px
- 48 px

Required rules:

- Label-to-field spacing is consistent across forms.
- Field-to-help-text spacing is consistent across forms.
- Field-to-error spacing is consistent across forms.
- Card padding is consistent for equivalent card types.
- Button groups use consistent gaps and wrapping behavior.
- Page margins and bottom padding use shared values.
- Unexplained whitespace greater than 48 px is not acceptable.
- Interactive controls are approximately 44 CSS pixels high or larger.
- Content does not overflow horizontally at approximately 320 CSS pixels wide.
- Long localized labels, long words, and unbroken values wrap without widening the viewport.
- Fixed action areas reserve only the content space they require, including safe-area insets.

## 4. Required test viewports

The UI must be reviewed at each of these viewport sizes:

| Viewport | Primary coverage |
| --- | --- |
| 1440 x 900 | Wide desktop |
| 1280 x 900 | Standard desktop |
| 960 x 900 | Narrow desktop or tablet landscape |
| 600 x 900 | Compact tablet |
| 480 x 900 | Large mobile |
| 412 x 915 | Common Android portrait |
| 360 x 800 | Narrow Android portrait |
| 320 x 700 | Minimum supported narrow layout |

## 5. Required surfaces

Acceptance applies to all of these surfaces:

- Home
- First-run onboarding
- Import Text
- Review Words
- Prepare Words
- Learn
- Settings
- Diagnostics in non-Release configurations
- Mobile navigation
- Desktop sidebar
- Dialogs
- Confirmation areas

## 6. Required states

Each applicable surface must handle these states without broken layout, missing feedback, or unreachable controls:

- Empty
- Loading
- Loaded
- Success
- No results
- Network failure
- Validation failure
- Confirmation open
- Very long imported text
- Very long word or unbroken value
- Many available meanings
- Narrow viewport
- Long German localization

## 7. Navigation acceptance

### 7.1 Desktop sidebar

- The sidebar fills the window height and remains visually continuous to the bottom.
- The sidebar remains independent of right-side content scrolling.
- Navigation stays at the top.
- Developer notice and build identity stay at the bottom when present.
- Nothing is clipped, and no unexplained gap separates the sidebar from the window edge.

### 7.2 Mobile header and menu

- The app header remains visible, with the menu control and current page title available.
- Page content scrolls beneath the header without being obscured.
- Safe-area insets are respected, and a duplicate header is not rendered.
- The navigation drawer or overlay appears above page content without changing the page's scroll position.
- The current route is identifiable.
- A second menu-control activation, Escape, Android Back, or route navigation closes the menu as appropriate.
- Background scrolling is locked while the menu is open and restored when it closes.
- All navigation choices remain reachable without scrolling the underlying page.

## 8. Scrolling and fixed actions

- App scrollbars may be visually hidden only when touch, mouse-wheel, trackpad, and keyboard scrolling remain functional.
- Text areas retain native editing and scrolling behavior.
- Global wildcard rules must not suppress native control behavior.
- Scrollable content is discoverable through continuation, shadows, or gradients where needed.
- Review Words, Prepare Words, and Learn action bars align with the bottom app area on desktop and mobile.
- Fixed actions do not leave a dead black area or obscure the final content.
- The last content remains reachable with the smallest necessary bottom padding.
- Android safe-area padding is applied once.

## 9. Workflow-specific acceptance

### 9.1 Import Text

- The text area uses centralized `.text-area` styling, is responsive, bounded on desktop, compact on mobile, and internally scrollable.
- Imported text is never truncated or modified.
- Source language, lookup mode, and primary action remain reasonably discoverable without excessive empty space.

### 9.2 Review Words

- Progress, candidate, context, context navigation, and Known/Unknown decisions remain prominent.
- The fixed decision actions are fully visible, keep Undo reachable, and do not cover content.
- Saving and undo states provide visible feedback and reject duplicate submissions.
- `Discard import` is positioned as a clearly destructive trailing/end action in the bottom workflow action bar on normal-width layouts with destructive/danger styling, distinct from neutral workflow-exit actions.
- Activating `Discard import` presents an explicit inline destructive alert dialog confirming whole-active-import discard while suppressing competing primary review actions.
- Confirmation provides a neutral Cancel action (restoring trigger focus) and a destructive Confirm action with `data-destructive-confirm`.
- Narrow/mobile layouts stack the action bar responsively, keeping all decision and discard actions reachable without horizontal overflow.
- Automated test coverage establishes markup, CSS, and accessibility contracts only; rendered Windows and Android WebView appearance, focus handling, and touch interactions are not manually proven by this package and are not claimed.

### 9.3 Prepare Words

- Method selection appears once at the start of a new batch.
- Loading, result, no-result, failure, retry, manual-edit, progression-recovery, and validation states are visually distinct.
- Manual preparation presents one primary multiline answer field (Definition or Translation based on import context) using centralized `.text-area` styling (shared typography, border, radius, background, padding, focus ring, disabled state, and vertical resize); legacy combined mode is retained as a bounded compatibility exception.
- Advanced options (Acronym expansion when applicable, Accepted spelling aliases) are collapsed by default.
- Redundant form inputs (canonical term, encountered form, Additional Note) are removed from the normal manual editor; candidate term and metadata remain visible.
- Mode-specific validation displays dedicated localized errors for empty Definition / empty Translation, is scrolled fully into view, and focuses the invalid field.
- Choosing another meaning uses a bounded, accessible presentation that wraps long values.
- "End preparation" is styled as a neutral/secondary action in the bottom workflow action bar with inline confirmation; competing disposition actions are suppressed while confirmation is open.
- Successful normal Accept persists durably before Learning readiness is queried or navigation occurs.
- When Learning readiness indicates that still-open genuinely-new demand is fully satisfiable by eligible prepared vocabulary, Preparation automatically transitions to `/learn` without first displaying another Preparation candidate.
- When eligible prepared backlog is below open demand, Preparation continues normal candidate progression.
- When daily genuinely-new admission capacity is already exhausted, readiness evaluates to false; Preparation continues normally and same-day re-entry to `/prepare-words` remains unblocked without an automatic redirect loop.
- Dispositions (**Skip for now**, **Mark as known**, **Exclude from learning**) and **End preparation** do not query Learning readiness or trigger automatic Learning navigation.
- The active Preparation session remains paused and resumable after automatic transition to Learning.
- If readiness query, navigation callback, or candidate loading fails after a successful commit, the UI renders the dedicated progression-recovery banner with a progression-only Retry button; progression retry re-evaluates the progression flow and never repeats acceptance.
- Fixed actions remain reachable without covering the current candidate or form.
- Automated test coverage verifies structural DOM contracts, accessibility relationships, workflow wiring, and coordinator state transitions; rendered WebView appearance, actual Windows focus behavior, Android touch behavior, and platform-specific layout are not manually proven by this package and are not claimed.

### 9.4 Learn

- No dead area separates the card from the action bar.
- Again, Hard, Good, and Easy remain fully visible after the answer is revealed, with permanently visible localized textual labels across English, German, and Russian.
- Red is reserved for destructive/permanent actions and is not used for normal learning ratings; `Again` is styled with neutral muted styling and strong border without destructive danger tokens or affordances.
- Rating controls provide a coherent non-color visual hierarchy:
  - `Again`: neutral muted surface (`var(--color-surface-muted)`), 1px solid border (`var(--color-border-strong)`), neutral text;
  - `Hard`: elevated surface (`var(--color-surface-elevated)`), 1px dashed warm border (`--rating-hard-border: #9a6a32`), neutral text;
  - `Good`: sole primary solid-filled rating (`var(--color-primary)`);
  - `Easy`: soft success surface (`var(--color-success-surface)`), 2px solid border (`var(--color-success)`), success-green text.
- Standard `.button` sizing, focus-visible outline (`outline: 3px solid var(--color-focus-ring)`), and disabled styling (`opacity: 0.56; cursor: not-allowed`) remain active on all rating controls.
- Text-to-background contrast and UI component border contrast satisfy WCAG AA/AAA expectations across both Light and Dark themes.
- Long definitions and source details remain readable and scrollable.
- Context navigation remains associated with the context.
- Content never overflows horizontally.
- Automated test coverage verifies static Razor markup, CSS contracts, absence of danger tokens, and event wiring; rendered Windows and Android WebView appearance, touch responsiveness, and ambient lighting contrast are not manually proven by this package and are not claimed.

### 9.5 Settings

- The complete page is scrollable.
- Display Name card in third position offers bounded input with Save and Remove actions, persisting local name or removing when cleared (with danger styling for name removal).
- New words per day offers 4-preset selection in visual order (5 Recommended, 1, 10, Custom); selecting Custom reveals an accessible numeric input with immediate validation (1..50), non-blocking workload warning above 15, and canonicalization at the semantic save boundary.
- Reset confirmation and all its actions remain reachable and fully visible.
- Destructive settings are clear without consuming disproportionate space; Online Dictionary consent revocation uses an inline confirmation dialog matching the destructive confirmation pattern.
- Unfinished support actions (such as Support KnownFirst) are absent from production Settings rendering; no placeholder or "coming soon" control appears.
- Diagnostic actions remain logically grouped, visually marked as diagnostic, and gated so they are absent in Release.
- The remaining Settings content, including the build identity, stays structurally clear, correctly labelled, and accessible.
- Help & Support offers the release-note-history entry point (opening `/release-notes`) and the functional "Report a bug" action. Both are normal production controls — implemented in Debug, BetaDiagnostic, and Release, never placeholder or debug-only.
- Activating "Report a bug" opens the system email composer with recipient `Tachiguro+KnownFirst_BugReport@gmail.com`, localized subject, structured template prompts, and safe technical metadata only. It never sends automatically. If the email client cannot be opened, a localized copy-address fallback is displayed.

### 9.6 Release-note history

- The `/release-notes` page shows every existing release note, newest first, and can be reopened as often as the user wants.
- Reopening the history never alters the automatic one-time What's New notice or its seen state; dismissing that notice never removes an entry from the history.
- Each release note is a labelled section with a heading carrying its version and a list of its bullets, so headings and lists remain semantically correct and navigable.
- The page remains readable and scrollable at every required viewport, with the standard page header and back navigation.
- No Support KnownFirst or placeholder control appears on this page or in Help & Support.
- Current automated coverage for this surface is source/markup contract evidence only. Visual acceptance and runtime navigation are verified manually per [GUI_TEST_MATRIX.md](GUI_TEST_MATRIX.md) and are not claimed by automated results.

### 9.7 First-run onboarding

- Onboarding is hosted in a dedicated fullscreen shell without normal navigation chrome (desktop sidebar, mobile headers) or the What's New modal.
- The workflow progresses logically across nine ordered steps with Back and Continue actions; the Welcome step provides native labelled UI language selection (`<select id="onboarding-ui-language-select">` with options `System`, `English`, `German`, `Russian` in `.field-group`) matching Settings, and Appearance (System, Light, Dark) selection; the final summary screen provides the Finish action and an explicit reminder that all settings can be adjusted later in Settings. No global Skip action exists.
- The Display Name step allows continuing with an entered name or explicitly skipping via the dynamic Skip action (`Onboarding_DisplayNameSkip`) when the input is empty/whitespace; empty inputs continue normalizing to `null`.
- Online dictionary lookup consent is presented with readable service highlights (Wiktionary primary, Wikipedia fallback, local privacy); persisted default is Off; progression requires an explicit user choice (`[ Enable Online Dictionary ]` or `[ Keep Online Dictionary Disabled ]`) before Continue is enabled; selecting Keep Disabled when consent was granted invokes the destructive confirmation dialog with Cancel/Escape focus restoration.
- Enhanced Term Recognition explains the user benefit of decomposing German compound words into core vocabulary components offline on device without universal-language claims.
- Practice setup includes concise explanatory helper texts for Card Direction and Learning Mode matching Settings.
- Daily Pace presents 4 presets in consistent order (5 Recommended, 1, 10, Custom); selecting Custom reveals an accessible numeric input with immediate validation (1..50), non-blocking workload warning above 15, and canonicalization at the semantic Continue boundary.
- Learning Day Timing provides timezone and 24-hour cutoff selectors with minute precision (`00..59`) and accessible labelling.
- Localized EN, DE, and RU strings render cleanly without truncation, horizontal overflow, or broken layout across all required viewports (320px up to 1440px).
- Finishing onboarding persists completion, clears progress, and transitions cleanly to the standard application shell in the same running process.

### 9.8 Home

- The main application heading `KnownFirst` remains unchanged.
- When a normalized local Display Name is configured, Home renders the localized greeting prepended before the subtitle separated by a single whitespace:
  - EN: `Welcome, {0}.`
  - DE: `Willkommen, {0}.`
  - RU: `Добро пожаловать, {0}.`
- When no Display Name is configured (null / absent), Home preserves the existing subtitle-only rendering without an empty greeting, placeholder, or spurious separator whitespace.
- The active review summary card, workflow action tiles, quick actions, statistics, and navigation chrome remain visually and functionally unchanged.
- Localized EN, DE, and RU strings wrap cleanly without horizontal overflow across all required viewports (320px up to 1440px).
- Automated test coverage verifies source/markup and localization contracts only; rendered GUI behavior is verified manually.

## 10. Forms and feedback

Translation, definition, note, accepted forms, acronym expansion, validation, secondary actions, and the primary action must be grouped according to their domain relationship.

For every interactive control:

- Its purpose is clear from its label and context.
- Activating it performs the stated action exactly once.
- Its enabled and disabled states match whether the action is currently valid.
- It produces immediate visible feedback.
- Selected choice state must be communicated visually (e.g. active border/background) and programmatically via `aria-pressed`.
- Cancel returns to the prior logical state without applying pending changes.

## 11. Confirmations and revealed content

Destructive actions use one consistent inline pattern:

- The normal state shows one red trigger button.
- Opening confirmation hides the trigger at the same logical location and displays an inline alert dialog.
- The open state shows a neutral Cancel action and a red final action.
- Both actions are fully visible, and the confirmation scrolls into view.
- Revealed destructive confirmations receive initial post-render focus on the Cancel action.
- Enter never confirms a destructive action.
- Escape cancels and restores the original state.
- Dismissing the confirmation via Cancel or Escape restores focus to the triggering action button.
- Only activating the destructive Confirm action executes the destructive operation.

Automatic scrolling is appropriate when newly revealed validation errors, manual-entry fallbacks, retry areas, or confirmation areas would otherwise be missed. Normal actions must not cause gratuitous scrolling. Focus moves to the first useful control in newly revealed content and returns sensibly when that content closes.

## 12. Verification boundary

Automated unit and contract tests verify deterministic behavior, state transitions, localization keys, CSS/Razor contracts, focus targets, scroll targets, and Release exclusion where practical. They do not prove visual perfection, platform safe areas, touch behavior, or native rendering.

Final visual acceptance requires the repeatable manual matrix in `GUI_TEST_MATRIX.md`, Windows screenshots, and Android device validation. Any result not inspected visually must be reported as unverified rather than inferred from a successful build.
