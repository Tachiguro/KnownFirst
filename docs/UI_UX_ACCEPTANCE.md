# KnownFirst Beta 7 UI/UX Acceptance Criteria

## 1. Status and scope

This document is the binding UI and UX acceptance specification for the Beta 7 hardening work. It complements `KNOWNFIRST_ARCHITECTURE.md` and `MVP_WORKFLOW.md`; those documents remain authoritative for product behavior and architecture.

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

- The text area is responsive, bounded on desktop, compact on mobile, and internally scrollable.
- Imported text is never truncated or modified.
- Source language, lookup mode, and primary action remain reasonably discoverable without excessive empty space.

### 9.2 Review Words

- Progress, candidate, context, context navigation, and Known/Unknown decisions remain prominent.
- The fixed decision actions are fully visible and do not cover content.
- Saving and undo states provide visible feedback and reject duplicate submissions.

### 9.3 Prepare Words

- Method selection appears once at the start of a new batch.
- Loading, result, no-result, failure, retry, manual-edit, and validation states are visually distinct.
- A validation error is close to the relevant fields and primary action, is scrolled fully into view, and focuses the first relevant invalid field.
- Choosing another meaning uses a bounded, accessible presentation that wraps long values.
- Fixed actions remain reachable without covering the current candidate or form.

### 9.4 Learn

- No dead area separates the card from the action bar.
- Again, Hard, Good, and Easy remain fully visible after the answer is revealed.
- Long definitions and source details remain readable and scrollable.
- Context navigation remains associated with the context.
- Content never overflows horizontally.

### 9.5 Settings

- The complete page is scrollable.
- Reset confirmation and all its actions remain reachable and fully visible.
- Destructive settings are clear without consuming disproportionate space.
- Support and diagnostics are grouped logically.

## 10. Forms and feedback

Translation, definition, note, accepted forms, acronym expansion, validation, secondary actions, and the primary action must be grouped according to their domain relationship.

For every interactive control:

- Its purpose is clear from its label and context.
- Activating it performs the stated action exactly once.
- Its enabled and disabled states match whether the action is currently valid.
- It produces immediate visible feedback.
- Cancel returns to the prior logical state without applying pending changes.

## 11. Confirmations and revealed content

Destructive actions use one consistent inline pattern:

- The normal state shows one red trigger button.
- Opening confirmation hides the trigger at the same logical location.
- The open state shows a neutral Cancel action and a red final action.
- Both actions are fully visible, and the confirmation scrolls into view.
- Enter never confirms a destructive action.
- Escape cancels and restores the original state.

Automatic scrolling is appropriate when newly revealed validation errors, manual-entry fallbacks, retry areas, or confirmation areas would otherwise be missed. Normal actions must not cause gratuitous scrolling. Focus moves to the first useful control in newly revealed content and returns sensibly when that content closes.

## 12. Verification boundary

Automated unit and contract tests verify deterministic behavior, state transitions, localization keys, CSS/Razor contracts, focus targets, scroll targets, and Release exclusion where practical. They do not prove visual perfection, platform safe areas, touch behavior, or native rendering.

Final visual acceptance requires the repeatable manual matrix in `GUI_TEST_MATRIX.md`, Windows screenshots, and Android device validation. Any result not inspected visually must be reported as unverified rather than inferred from a successful build.
