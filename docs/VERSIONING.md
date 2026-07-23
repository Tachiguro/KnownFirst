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

The in-app release-notes user interface is explicitly deferred from Beta 9 and targets **Beta 10** as its first release:

- **Beta 9:** Does not show an in-app release-notes dialog.
- **First target release:** Beta 10.
- **Display trigger:** Displayed once automatically after first launch following an installation or update to a new version.
- **Reopen access:** Users can reopen the release notes at any time from Settings.
- **Content:**
  - Release builds present a concise localized summary of user-visible changes.
  - Debug and Diagnostic builds additionally display detailed technical/tester notes.
- **Storage:** Persisted using platform `Preferences` rather than the SQLite learning database.
- **Localization:** English and German resources required.
- **Schema boundary:** Must not introduce a database migration.
- **Accuracy:** Release notes content must be authored strictly from verified implemented behavior, never from unverified plans.
