# Branch and worktree inventory

**Snapshot:** 2026-07-22T06:35:00+02:00
**Comparison base:** `origin/master` at
`956e71895cf141805c8c24f7d32691075d439730`

This is an immutable maintenance snapshot taken after fetching `origin` and
before creating the documentation commits. Ahead/behind means
`branch...origin/master`; values are shown as **behind / ahead**. Git reported
every branch ref as fully merged into `origin/master`. Uncommitted worktree
changes are not part of that graph and are called out separately.

## Local branches

| Branch | Tip | Last commit (+02:00) | Remote ref | Behind / ahead | Worktree | Classification |
| --- | --- | --- | --- | ---: | --- | --- |
| `feat/build-identity-and-diagnostics` | `08fa0a9` | 2026-07-20 17:23 | Yes | 18 / 0 | No | Fully merged, deletable later |
| `feature/automatic-dictionary-learning-mvp` | `fb4d759` | 2026-07-16 19:39 | No | 40 / 0 | No | Fully merged, deletable later |
| `feature/beta-7-ui-ux-hardening` | `220e13f` | 2026-07-21 09:27 | Yes | 4 / 0 | No | Fully merged, deletable later |
| `feature/learning-workflow-and-word-normalization` | `8a98eb1` | 2026-07-20 12:28 | Yes | 22 / 0 | No | Fully merged, deletable later |
| `feature/project-governance-and-docs` | `956e718` | 2026-07-22 06:25 | No at snapshot | 0 / 0 | Yes | Active; keep |
| `feature/text-review-vertical-slice` | `d45f51b` | 2026-07-16 14:20 | Yes | 41 / 0 | No | Fully merged, deletable later |
| `fix/android-beta-release-and-preparation-ux` | `70d24bd` | 2026-07-17 00:40 | No | 39 / 0 | No | Fully merged, deletable later |
| `fix/android-online-lookup-crash-diagnostics` | `ae8408b` | 2026-07-20 15:43 | Yes | 19 / 0 | No | Diagnostic branch; fully merged, deletable later |
| `fix/android-release-online-lookup-crash` | `987b5f0` | 2026-07-17 22:40 | Yes | 29 / 0 | No | Diagnostic/release-fix branch; fully merged, deletable later |
| `fix/mobile-review-preparation-ux` | `a94539d` | 2026-07-17 03:23 | No | 37 / 0 | No | Fully merged, deletable later |
| `fix/preparation-language-and-beta-apk` | `5fd9f5d` | 2026-07-17 04:40 | No | 36 / 0 | No | Fully merged, deletable later |
| `fix/word-analysis-and-context-diagnostics` | `b39868d` | 2026-07-17 01:15 | No | 38 / 0 | No | Diagnostic branch; fully merged, deletable later |
| `hotfix/beta-8-online-lookup-crash` | `f1f1891` | 2026-07-21 12:12 | Yes | 2 / 0 | Yes, dirty | Active local work to review; branch ref is merged; covered by release tag |
| `hotfix/beta-8-parser-aot-fix-clean` | `29aff385` | 2026-07-22 05:24 | Yes | 1 / 0 | Yes, clean | Fully merged; replaceable by release tag; deletable later |
| `master` | `3de5d18` | 2026-07-16 04:01 | Yes | 42 / 0 | No | Keep; local ref needs a later safe fast-forward |
| `release/android-beta-5-internal` | `eff2a84` | 2026-07-21 03:08 | Yes | 13 / 0 | No | Historical release branch |
| `release/android-beta-6-internal` | `9a1fa2c` | 2026-07-21 04:11 | Yes | 12 / 0 | No | Historical release branch |
| `release/android-beta-7-internal` | `ea9ba60` | 2026-07-21 09:34 | Yes | 3 / 0 | Yes, dirty | Historical release branch; keep because protected local work exists |

There were no unmerged local branch refs. This does not make the two dirty
worktrees safe to clean or remove.

## Remote branches

| Remote branch | Tip | Last commit (+02:00) | Behind / ahead | Merge status |
| --- | --- | --- | ---: | --- |
| `origin/master` | `956e718` | 2026-07-22 06:25 | 0 / 0 | Integration base |
| `origin/feat/build-identity-and-diagnostics` | `08fa0a9` | 2026-07-20 17:23 | 18 / 0 | Fully merged |
| `origin/feature/beta-7-ui-ux-hardening` | `220e13f` | 2026-07-21 09:27 | 4 / 0 | Fully merged |
| `origin/feature/learning-workflow-and-word-normalization` | `8a98eb1` | 2026-07-20 12:28 | 22 / 0 | Fully merged |
| `origin/feature/text-review-vertical-slice` | `d45f51b` | 2026-07-16 14:20 | 41 / 0 | Fully merged |
| `origin/fix/android-online-lookup-crash-diagnostics` | `ae8408b` | 2026-07-20 15:43 | 19 / 0 | Fully merged |
| `origin/fix/android-release-online-lookup-crash` | `987b5f0` | 2026-07-17 22:40 | 29 / 0 | Fully merged |
| `origin/hotfix/beta-8-online-lookup-crash` | `f1f1891` | 2026-07-21 12:12 | 2 / 0 | Fully merged |
| `origin/hotfix/beta-8-parser-aot-fix-clean` | `29aff385` | 2026-07-22 05:24 | 1 / 0 | Fully merged; release-tagged |
| `origin/release/android-beta-5-internal` | `eff2a84` | 2026-07-21 03:08 | 13 / 0 | Fully merged; historical release |
| `origin/release/android-beta-6-internal` | `9a1fa2c` | 2026-07-21 04:11 | 12 / 0 | Fully merged; historical release |
| `origin/release/android-beta-7-internal` | `ea9ba60` | 2026-07-21 09:34 | 3 / 0 | Fully merged; historical release |

`origin/HEAD` points to `origin/master`. Every remote branch was fully
merged into `origin/master`. The documentation branch did not yet exist on
the remote at snapshot time; publishing this task adds it without changing
`master`.

## Worktrees

| Worktree label | Branch | HEAD | State at snapshot | Recommendation |
| --- | --- | --- | --- | --- |
| Main repository | `release/android-beta-7-internal` | `ea9ba60` | Dirty: protected modified branding image and protected untracked icon-generation script | Keep and do not switch, clean, reset, or remove |
| `KnownFirst-worktrees/beta-8-online-lookup-crash` | `hotfix/beta-8-online-lookup-crash` | `f1f1891` | Dirty: three modified parser/test files and two untracked parser fixtures | Treat as active protected local work; review separately before any cleanup |
| `KnownFirst-worktrees/beta-8-parser-aot-fix-clean` | `hotfix/beta-8-parser-aot-fix-clean` | `29aff385` | Clean | Candidate for later owner-approved removal after release retention is confirmed |
| `KnownFirst-worktrees/project-governance-and-docs` | `feature/project-governance-and-docs` | `956e718` | Clean before documentation edits | Active; keep through review |

## Tags

| Tag | Type | Tag object | Target | Purpose |
| --- | --- | --- | --- | --- |
| `v1.0.0-beta.8` | Annotated | `a9fce05f64089757ce6f6319240d32c51185951a` | `29aff385f2c3cabca49b70bd011bf4c09808df6d` | Exact source of the Google Play Internal Testing Beta 8 AAB |

## Later cleanup recommendations

No cleanup is authorized by this inventory.

When the owner starts a separate cleanup task:

1. preserve and review the dirty main repository and dirty online-lookup
   worktree first;
2. keep `master`, the active documentation branch while under review, and the
   Beta 8 release tag;
3. consider removing the clean parser-AOT worktree only after confirming that
   the tag and remote release source are sufficient;
4. consider deleting fully merged feature/fix/hotfix refs only after checking
   for worktree attachment, untracked files, and operational retention needs;
5. decide whether Beta 5, Beta 6, and Beta 7 require their own archival tags
   before deleting historical release branches; and
6. fast-forward the stale local `master` ref only from a clean, explicitly
   authorized context.

Do not delete remote branches, local branches, worktrees, or directories as a
side effect of documentation or feature development.
