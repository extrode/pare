# Milestones — pare

Where this project is going, coarse. A milestone is a destination worth naming, not a task: it
survives many sprints, it is the thing you would tell someone who asked "what is this for", and
it changes rarely. If an entry here moves week to week, it belongs in `todo.md` or `status/`.

This is the one file in `work/` that is allowed to go a month without an edit. Editing it
means the plan changed, which is worth noticing.

**Distinction from the neighbours.** `milestones.md` is where we are going. `todo.md` is
everything not yet scheduled. `status/current.md` is what is being worked on right now. A single
item legitimately appears in all three at different times in its life, and should be in exactly
one of them at any given moment.

| # | Milestone | Target | Status | Notes |
|---|-----------|--------|--------|-------|
| M0 | Repo, solution, CI, API sketch, UTF-16 decision | 2026-10-14 | shipped | CI on GitHub Actions since 2026-10-15 |
| M1 | Lexer: JS + TS tokens, templates, regex detection, contextual keywords | 2026-10-14 | shipped | 17,582 corpus files, 0 unexpected |
| M2 | Recognizer for JS statements and expressions | 2026-10-14 | shipped | 1,048 corpus .js files byte-identical |
| M3 | TS type grammar + all handoff section 5 erasures | 2026-10-14 | shipped | 0 syntax errors on 13,935 corpus files |
| M4 | Section 6 rejections, section 7 traps, ASI fixes | | shipped | lexer escape validation dropped 2026-10-31: documented as leniency |
| M5 | Conformance + differential harness vs tsgo | 2026-10-15 | shipped | 99.96% of the eligible corpus |
| M6 | CLI files/globs, MSBuild task, sample app | 2026-10-15 | shipped | sample app browser-tested |
| M7 | Performance pass + benchmarks | 2026-10-15 | shipped | 0 B per Strip call |
| M8 | Dev middleware (re-strip under `dotnet watch` hot reload), tsgo script polish | 2026-10-15 | shipped | `AddPareDevelopment` |

Status is one of `planned`, `in progress`, `shipped`, `dropped`. A dropped milestone stays in the
table with a note saying why: the reason it was dropped is the most useful thing on the row, and
deleting it means re-litigating the decision in six months.
