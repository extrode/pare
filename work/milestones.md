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
| M0 | Repo, solution, CI, API sketch, UTF-16 decision | 2026-10-14 | shipped | CI not run yet (no remote) |
| M1 | Lexer: JS + TS tokens, templates, regex detection, contextual keywords | 2026-10-14 | shipped | 17,582 corpus files, 0 unexpected |
| M2 | Recognizer for JS statements and expressions | 2026-10-14 | shipped | 1,048 corpus .js files byte-identical |
| M3 | TS type grammar + all handoff section 5 erasures | 2026-10-14 | shipped | 0 syntax errors on 13,935 corpus files |
| M4 | Section 6 rejections, section 7 traps, ASI fixes | | in progress | done except lexer escape validation |
| M5 | Conformance + differential harness vs tsgo | | planned | >= 99% eligible corpus |
| M6 | CLI files/globs, MSBuild task, sample app | | in progress | CLI done 2026-10-14 |
| M7 | Performance pass + benchmarks | | planned | 0 B per Strip call |
| M8 | Dev middleware, tsgo script polish | | planned | optional |

Status is one of `planned`, `in progress`, `shipped`, `dropped`. A dropped milestone stays in the
table with a note saying why: the reason it was dropped is the most useful thing on the row, and
deleting it means re-litigating the decision in six months.
