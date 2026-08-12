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

The application displays the formatted build identity as follows. These are format templates using version-neutral placeholders; they assert no current product version. The verified current source identity is recorded in [PROJECT_STATE.md](PROJECT_STATE.md).

- **Debug:** `KnownFirst · 1.0.0-beta.N · Debug · Build N · Commit <short-sha>`
- **Diagnostic:** `KnownFirst · 1.0.0-beta.N · Diagnostic · Build N · Commit <short-sha>`
- **Prerelease Release:** `KnownFirst · 1.0.0-beta.N · Release · Build N · Commit <short-sha>`
- **Future stable Release:** `KnownFirst · 1.0.0 · Release · Build <number>`

A commit is included for Debug and Diagnostic builds and for every prerelease version. A build produced from a dirty working tree additionally appends ` · DIRTY`, for example `KnownFirst · 1.0.0-beta.N · Release · Build N · Commit <short-sha> · DIRTY`.

## In-app release notes

An in-app release-notes user interface exists. It includes a **one-time per-version What's New notice** and reopenable release-note history; both are narrower than the cumulative specification below. Implemented behavior and planned behavior are kept strictly separate.

### Implemented today: current-version What's New notice and reopenable release-note history

- **Catalog:** A release-note catalog maps a product version to a localized title key and localized bullet keys. Entries exist for `1.0.0-beta.10`, `1.0.0-beta.11`, `1.0.0-beta.12`, and `1.0.0-beta.13`.
- **Selection:** Only the catalog entry whose version equals the running application version is selected. No older unread entry is collected.
- **Display trigger:** The selected entry is shown automatically once, and stays dismissed afterwards.
- **Acknowledgement storage:** Platform `Preferences` (not SQLite) stores the exact **seen version string**. It does not store an integer sequence.
- **Reopenable history:** Settings → Help and Support provides access to Release Notes. The history exposes the catalog newest-first; reopening it does not mutate the stored seen-version state.
- **No matching entry:** A running version without a matching catalog entry shows no modal at all rather than an empty one.
- **Clean install:** A fresh installation shows only the entry matching its own version.
- **Localization:** Current release-note title and bullet content is localized in English, German, and Russian.
- **Isolation:** Portable archive export and import never read or write the seen-version preference. A full application-data reset clears platform preferences, so the notice can appear again.
- **Failure tolerance:** A failed preference read or write never throws; the notice is simply suppressed for that run.

### Planned, not implemented: cumulative unread release-note sequencing

These remain binding future requirements. They extend the implemented reopenable history and must not be described as available.

- **Ordered sequence:** Every distributed version has an ordered release-note sequence.
- **Acknowledged sequence storage:** Platform `Preferences` stores the integer sequence of the highest acknowledged release.
- **Cumulative display on update:** Upon update, the application collects every release newer than the acknowledged sequence and displays them in a scrollable view:
  - Newest release notes appear first, followed by older unread releases.
  - *Example:* User acknowledged Beta 8, skipped Beta 9, and installed Beta 10 -> the view presents Beta 10 notes first, then Beta 9 notes below.
- **Read confirmation:** Acknowledging or closing the completed release-note view records all displayed entries as read in platform preferences.
- **Tester details:** Technical details may exist separately in a collapsed control for Debug/Diagnostic builds.

### User Content Guidelines
- **Titles:** Localized English, German, and Russian version title.
- **Bullets:** Two to four concise user-facing bullet points per version, localized in English, German, and Russian.
- **Length limit:** Maximum approximately 500 characters per language (excluding title).
- **Style:** Clean, non-technical language. Do **not** include Git commit hashes, PR numbers, internal C# class names, database column names, test counts, or unverified future plans.

### Authorship Workflow
- **Drafting:** The feature documentation phase (`DOCUMENT_ONLY`) drafts verified user-facing release notes.
- **Freezing:** Release preparation approves and freezes release-note text before building distribution packages.
- **Consumption:** Build and packaging agents consume pre-approved release-note content.
- **Changelog separation:** `CHANGELOG.md` remains the complete developer/technical history and is **not** rendered directly to end users.
