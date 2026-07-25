# KnownFirst internal backlog

A compact internal backlog for solo development. GitHub Issues are deferred until collaboration or external testing makes them useful; this file replaces them for now.

## Table

| ID | Priority | Area | Summary | Status | Release blocker | Notes |
| --- | --- | --- | --- | --- | --- | --- |
| KF-LEARN-001 | P1 | Learning | Investigate whether the same card identity and direction can appear unintentionally twice in one session. Distinguish accidental duplication from the legitimate one-time repetition after an explicit Again rating. | Investigation required | Only if accidental duplication is confirmed | Tests required before implementation. See [CURRENT_WORK.md](CURRENT_WORK.md) for the active next action. |
| KF-UX-001 | P2 | Settings / Portable recovery | Add the standard vertical spacing to the portable-import confirmation and hide or disable the original Export and Import actions until Cancel or Import completes. | Backlog | No | Candidate for a later small recovery-UX package, together with KF-STATE-001. |
| KF-STATE-001 | P2 | Navigation / Portable recovery | Refresh Home and burger-menu workflow availability immediately after a successful portable import. Cancelled and failed imports must not change state. | Backlog | No | Candidate for the same later recovery-UX package as KF-UX-001. |
