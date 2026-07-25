# KnownFirst internal backlog

A compact internal backlog for solo development. GitHub Issues are deferred until collaboration or external testing makes them useful; this file replaces them for now.

## Table

| ID | Priority | Area | Summary | Status | Release blocker | Notes |
| --- | --- | --- | --- | --- | --- | --- |
| KF-LEARN-001 | P2 | Learning UX | Distinguish intentional Again repeats and opposite card directions in the Learn UI. | Implemented on feature branch | No | Investigation confirmed no accidental backend duplication; the Learn card view and page now surface `IsAgainRepeat` and card direction so a legitimate repeat/opposite-direction card is visually distinct from a first-time card. See [CURRENT_WORK.md](CURRENT_WORK.md). |
| KF-UX-001 | P2 | Settings / Portable recovery | Add the standard vertical spacing to the portable-import confirmation and hide or disable the original Export and Import actions until Cancel or Import completes. | Backlog | No | Candidate for a later small recovery-UX package, together with KF-STATE-001. |
| KF-STATE-001 | P2 | Navigation / Portable recovery | Refresh Home and burger-menu workflow availability immediately after a successful portable import. Cancelled and failed imports must not change state. | Backlog | No | Candidate for the same later recovery-UX package as KF-UX-001. |
