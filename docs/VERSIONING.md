# KnownFirst Versioning Policy

## Independent identity fields

KnownFirst build identity consists of four distinct, complementary fields:

1. **Product version:** The user-facing semantic version string (e.g. `1.0.0-beta.9` or `1.0.0`).
2. **Build number:** A strictly monotonically increasing integer used for package version codes (e.g. `9`).
3. **Build configuration:** The compilation profile (`Debug`, `BetaDiagnostic` [displayed as `Diagnostic`], or `Release`).
4. **Commit identity:** Git commit hash, short hash, branch name, and dirty state.

## Beta product versions

Until stable `1.0.0`, every completed user-facing feature or milestone that is distributed to testers increments:

`1.0.0-beta.N`

The beta number increments once per tester release, not per commit or ordinary local build.

## Build number

- Strictly monotonically increasing for all distributable or store packages.
- Independent from the beta product version number.
- Rebuilding or re-uploading the same product version may retain the product version string while increasing the build number.
- Android `versionCode` uses this build number.

## No product-version increase

Do not increase the product version for:

- documentation-only or governance changes;
- internal refactorings without user-visible behavior changes;
- repeated local builds of unchanged source code;
- intermediate commits during an unfinished feature branch.

Commit identity distinguishes intermediate and unreleased builds.

## Stable 1.0.0 exit criteria

Reaching stable `1.0.0` requires:

- reliable vocabulary import, text analysis, word preparation, and learning workflows;
- user-facing and tested backup/restore capability;
- safe application updates and database schema migrations;
- zero known blocking crash or data-loss defects;
- completed privacy disclosures, attribution notices, and license compliance;
- verified Windows and Android execution;
- working update and release-notes experience;
- completed internal testing approval.

## Post-1.0 Semantic Versioning

Following `1.0.0`, KnownFirst strictly follows Semantic Versioning (`MAJOR.MINOR.PATCH`):

- **PATCH:** Backwards-compatible bug fixes.
- **MINOR:** Backwards-compatible new features.
- **MAJOR:** Incompatible API, UI workflow, or data-contract changes.

## Visible identity examples

The application displays the formatted build identity as follows:

- **Debug:** `KnownFirst · 1.0.0-beta.9 · Debug · Build 9 · Commit <short-sha>`
- **Diagnostic:** `KnownFirst · 1.0.0-beta.9 · Diagnostic · Build 9 · Commit <short-sha>`
- **Prerelease Release:** `KnownFirst · 1.0.0-beta.9 · Release · Build 9 · Commit <short-sha>`
- **Future stable Release:** `KnownFirst · 1.0.0 · Release · Build <number>`

## In-app release notes (Beta 10 requirement)

The in-app release-notes user interface is explicitly deferred from Beta 9 and targets **Beta 10** as its first release.

### Cumulative Unread Release Notes Specification
- **Display trigger:** Shown automatically once after the first application launch following installation or update to a new version. The automatic display occurs once for the current unread release-note set.
- **Ordered sequence:** Every distributed version has an ordered release-note sequence.
- **Acknowledged sequence storage:** Platform `Preferences` (not SQLite) stores the integer sequence of the highest acknowledged release.
- **Cumulative display on update:** Upon update, the application collects every release newer than the acknowledged sequence and displays them in a scrollable view:
  - Newest release notes appear first, followed by older unread releases.
  - *Example:* User acknowledged Beta 8, skipped Beta 9, and installed Beta 10 -> the view presents Beta 10 notes first, then Beta 9 notes below.
- **Read confirmation:** Acknowledging or closing the completed release-note view records all displayed entries as read in platform preferences.
- **Reopen access:** Users can reopen release notes from Settings at any time. Reopening from Settings does not alter version identity or acknowledged version state.
- **Clean install:** A fresh installation displays only the current release notes unless a future product decision explicitly specifies full history.

### User Content Guidelines
- **Titles:** Localized German and English version title.
- **Bullets:** Two to four concise user-facing bullet points per version.
- **Length limit:** Maximum approximately 500 characters per language (excluding title).
- **Style:** Clean, non-technical language. Do **not** include Git commit hashes, PR numbers, internal C# class names, database column names, test counts, or unverified future plans.
- **Tester details:** Technical details may exist separately in a collapsed control for Debug/Diagnostic builds.

### Authorship Workflow
- **Drafting:** The feature documentation phase (`DOCUMENT_ONLY`) drafts verified user-facing release notes.
- **Freezing:** Release preparation approves and freezes release-note text before building distribution packages.
- **Consumption:** Build and packaging agents consume pre-approved release-note content.
- **Changelog separation:** `CHANGELOG.md` remains the complete developer/technical history and is **not** rendered directly to end users.
- **No UI implementation:** This specification governs future implementation. No UI code is implemented in this documentation package.
