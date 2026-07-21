# KnownFirst Agent Workflow

Git is the source of truth for KnownFirst work.

An agent receives the current branch and one concrete work package. It must read the binding repository, architecture, and MVP workflow instructions before changing behavior. Historical chat transcripts are not a substitute for the current branch and specifications.

For each work package, the agent must:

1. Establish the current behavior from code and a reproducible check.
2. Define the acceptance criteria for the package.
3. Add or identify a regression test before implementing a behavior fix.
4. Make the smallest durable change that satisfies the criteria.
5. Run focused tests during development.
6. Run the complete required test suite and relevant checkpoint build before declaring the package green.
7. Review the diff for scope, safety, localization, generated files, and secrets.
8. Commit only the explicit files belonging to the completed package.
9. Push every green package immediately without rewriting published history.

Complete one work package before starting the next. Every pushed commit must compile and pass its required tests independently.

Full platform builds are checkpoint activities, not a substitute for focused development tests. A Release build or artifact may be produced only from a clean, pushed commit and only when the active task explicitly requires it.

Automated tests must use temporary or isolated data, fake clocks, and fake network responses as appropriate. They must never read, modify, reset, or delete a real KnownFirst application database or real application data.
