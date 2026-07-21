# KnownFirst Beta 7 GUI Test Matrix

## Purpose and verification boundary

This is the repeatable manual visual test matrix for Gemini and human testers. It complements automated contracts; a successful test or build does not prove native rendering, safe-area behavior, or visual correctness.

Use a disposable test installation and non-sensitive sample text. Do not clear, overwrite, or migrate a real user database. Record the app build identity, platform, OS version, theme, UI language, viewport or device, and result for every run.

## Required viewport set

Run each applicable row at:

| ID | Viewport | Coverage |
| --- | --- | --- |
| D1 | 1440 x 900 | Wide Windows desktop |
| D2 | 1280 x 900 | Standard Windows desktop |
| D3 | 960 x 900 | Narrow Windows desktop |
| T1 | 600 x 900 | Compact tablet layout |
| M1 | 480 x 900 | Large mobile layout |
| M2 | 412 x 915 | Common Android portrait |
| M3 | 360 x 800 | Narrow Android portrait |
| M4 | 320 x 700 | Minimum supported layout |

At minimum, repeat D2, M2, and M4 in English and German, and in Light and Dark. Check System appearance once on each platform.

## Layout matrix

| Area | Setup and action | Required evidence |
| --- | --- | --- |
| Desktop shell | Open every primary route at D1-D3 and scroll the page content | Sidebar remains full-height and green to the bottom; it does not move with content; navigation is at the top; attribution and build identity remain reachable at the bottom; no clipping or dead strip appears |
| Mobile header | Open every primary route at T1 and M1-M4 | One header is visible; burger and localized title fit; content starts below it; the status-bar safe area is not covered |
| Mobile menu | Scroll a page, open the burger, then close by backdrop, second burger click, Escape, Android Back, and route selection | Drawer overlays instead of reflowing content; underlying content does not scroll; every navigation choice is reachable inside the drawer; the original page scroll position is preserved |
| App scrolling | Use touch, mouse wheel, trackpad, Page Up/Down, arrow keys, Home, and End where available | App scrollbar chrome is hidden while all input methods still scroll; focus remains visible; text-area selection and internal scrolling still work |
| Home | Exercise empty, active-review, preparation-ready, and learning-ready summaries | Cards share a consistent rhythm; primary continuation is clear; long German labels wrap; no horizontal overflow occurs |
| Import Text | Paste short and very long text, change language and lookup mode, and trigger each validation error | Text area is bounded and internally scrollable without truncating content; fields, help, errors, and primary action remain grouped and reachable |
| Review Words | Use a word with one context and one with multiple long contexts; save and undo decisions | Candidate and context remain readable; context navigation wraps at M1-M4; Known and Unknown stay fully visible in the bottom action area; final content is reachable |
| Prepare Words | Exercise loading, result, many meanings, no-result, retry, manual edit, validation, and confirmation states | No duplicate card or stale candidate appears; form fields wrap; validation is adjacent; meaning list is bounded; action area does not cover content |
| Learn | Exercise prompt, revealed answer, typing feedback, long definition, multiple contexts, and expanded source details | No dead area appears above the action bar; Again, Hard, Good, and Easy remain reachable; long content scrolls and never widens the viewport |
| Settings | Scroll from the first setting through Support, Diagnostics when present, and Reset | All cards and buttons remain reachable; choices wrap cleanly; reset confirmation fits; destructive area is clear but not oversized |
| Diagnostics | Run in Debug or BetaDiagnostic only at D2 and M2 | Diagnostic controls are visually distinct from product actions, remain usable, and do not leak into Release |

## Safe-area and fixed-action checks

On a physical Android device with gesture navigation and, if available, three-button navigation:

1. Open Review Words, Prepare Words, and Learn.
2. Scroll to the end of the content.
3. Rotate only if the current build supports rotation; otherwise keep portrait.
4. Verify the final content can be exposed above the action area.
5. Verify the bottom actions do not overlap the system navigation area.
6. Verify there is no black or background-colored dead zone between content, actions, and the bottom inset.

## Result recording

For each failure, record:

- matrix row and viewport ID;
- platform, OS version, theme, and UI language;
- exact route and workflow state;
- concise reproduction steps;
- expected and actual result;
- screenshot or screen recording stored outside the repository;
- whether the issue blocks Beta 7 acceptance.

Do not mark a row visually passed unless its required evidence was directly inspected.
