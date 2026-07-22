## Summary

Describe the problem, the chosen scope, and the user-visible result.

## Scope

- [ ] The feature or fix scope is explicit.
- [ ] Unrelated refactoring and generated output are excluded.
- [ ] Existing worktrees and protected local changes were not modified.

## Verification

- [ ] Focused automated tests were added or identified.
- [ ] All affected automated tests pass.
- [ ] The complete required test suite passes.
- [ ] The Windows Debug build passes, or the reason it is not applicable is
      recorded below.
- [ ] Android build or runtime validation was completed when required, or is
      explicitly marked unverified.
- [ ] AOT and trimming impact was reviewed.
- [ ] Manual verification was completed when behavior cannot be proved
      automatically.

### Commands and results

List exact commands, totals, platform, configuration, and any unverified
boundary.

## Data and compatibility

- [ ] Database migration impact was reviewed.
- [ ] Data compatibility and cleanup behavior were reviewed.
- [ ] Database changes are documented in
      `docs/DATABASE_CONTRACT.md` with migration tests.
- [ ] No real user database or private application data was used.
- [ ] Backup/restore compatibility is addressed when the change affects it.

## Privacy and security

- [ ] External data transmission is unchanged or documented.
- [ ] Logs and diagnostics exclude private user content and credentials.
- [ ] No secrets, signing material, private logs, or personal data are
      committed.

## Documentation and release

- [ ] `CHANGELOG.md` records user-visible impact.
- [ ] `docs/PROJECT_STATE.md` is updated when milestone state changed.
- [ ] Relevant architecture documents and ADRs are updated.
- [ ] Release notes and handoff are updated when this is release work.
- [ ] Known limitations and deferred validation are explicit.
- [ ] Relative Markdown links were checked.

## Repository hygiene

- [ ] No APK, AAB, database, log, screenshot, secret, or build artifact is
      committed.
- [ ] Files were staged explicitly rather than with `git add .`.
- [ ] Commit messages are clear and the branch was pushed without force.

## Remaining risks or manual follow-up

State what is not proved, who must verify it, and whether it blocks merge.
