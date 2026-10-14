# Progress

Replaces the handoff's root `PROGRESS.md` (root holds only README, AGENTS, CHANGELOG by repo
convention). Update at the end of every session.

## Done

- **M0 (2026-10-14):** solution `Pare.slnx`, core library `Extrode.Pare`, CLI `pare`
  (`--stdin`, `--help`, `--version`), xUnit v3 tests (30 pass), CI workflow for Linux, Windows,
  macOS including NativeAOT publish and a smoke test. Local `osx-arm64` AOT publish: 1.1 MB, no
  AOT/trim warnings. UTF-16 decision: [docs/decisions/2026-10-14-001-utf16-input.md](../docs/decisions/2026-10-14-001-utf16-input.md).

- **M1 (2026-10-14):** lexer in `src/Extrode.Pare/Lexing/`. Design follows TypeScript's
  `scanner.ts`: `Next()` is context-free; the parser calls `ReScanSlashAsRegex`,
  `ReScanTemplateContinuation` and `ReScanGreaterThan` where only the grammar knows the goal.
  So the lexer holds no brace stack and `LexerState` is 4 ints + 3 bytes, copied to save and
  restore. The handoff's "previous token decides regex" and "brace-depth stack" live only in the
  test harness `tests/Extrode.Pare.Tests/Lexing/Tokenizer.cs`, used for the corpus round-trip.
  `scripts/get-ts-corpus.cs` pins microsoft/TypeScript `9eedcb0` (fetch ~15 s, 91 MB).
  Corpus round-trip (`tests/Extrode.Pare.Conformance`): 12,503 cases split into 17,582 script
  files (794 tsx/jsx/json units skipped), ~1.2 s debug. Error tokens: 88 files where TypeScript's
  baseline also has a scanner error, 8 named deviations, 0 unexpected.

- **M2 + M3 (2026-10-14):** one TS-aware recognizer in `src/Extrode.Pare/Parsing/` (built
  together: a JS-only recognizer would have been rewritten for M3). A `ref struct` recursive
  descent parser modelled on TypeScript's `parser.ts`. It records erase ranges (`Edit`) and
  rejections in pooled lists; `EditWriter` blanks the ranges and keeps line terminators.
  Speculation (`<` type arguments, parenthesized arrows) saves and restores lexer state and list
  counts, with TS's `notParenthesizedArrow` memo. A syntax error jumps the lexer to EOF, so every
  loop unwinds without exceptions. ASI fixes: `;` written at the start of an erased statement or
  member unless the previous token ends in `;` or `{`; `)` moved to the end of a multi-line
  return type; `(` moved to the start of type parameters that precede a line break.
  All section 6 rejections are reported in one pass. Unit tests: one per row of handoff
  sections 5, 6, 7 (`tests/Extrode.Pare.Tests/StripTests.cs`, 227 tests total).

- **M6 CLI (2026-10-14):** `pare <files|dirs|globs...> [--outdir dir] [--check-only]`.
  Output goes next to each source or is mirrored under `--outdir` from the inputs' common
  directory. Every file is processed even after a rejection. Globs are matched by a small
  hand-written matcher (`Glob.cs`), since no packages are allowed. The specifier rewrite lives in
  the core as `StripOptions.RewriteRelativeImportExtensions`, so the MSBuild task can reuse it:
  one same-length `Edit` with a `Replace` char per specifier. AOT binary: 1.2 MB, no trim
  warnings.

## Next

- **M6 rest:** MSBuild task `Extrode.Pare.Build` (in-process, incremental), sample app.
- **M4 leftovers:** lexer escape and numeric-separator validation (`work/todo.md`).

## Open questions

- CI has not run: the repo has no remote yet.
- Lexer leniency vs TypeScript (escape and numeric-literal validation): see `work/todo.md`.

## Corpus pass rate

`StripCorpusTests` (2026-10-14, ~27 s debug): of 13,935 files with no TS1xxx baseline (jsx,
`.d.ts` and untyped `node_modules` skipped), 11,753 strip, 2,172 are rejected under section 6,
10 are named deviations, **0 syntax errors, 0 moved characters**. All 1,048 plain `.js` files
come back byte-identical, apart from 2 named deviations that contain TS syntax. Every output is
parsed by Acornima. 229 fail, and each falls under either a named class of early error that
TypeScript reports from the checker (duplicate declarations, `super` misuse, private names
outside classes, ...) or an Acornima limit (decorators before `export`). The differential
check against tsgo is still M5.

## Deviations from the handoff

- Name: **Pare** (folder name) instead of TsBlank. Projects `Extrode.Pare.*`, CLI `pare`,
  static entry `TypeStripper.Strip` (a class named `Pare` would collide with the namespace).
- `PROGRESS.md` lives here as `work/progress.md`; the handoff is `docs/handoff.md`.
- Diagnostic codes print as `PARE001` (syntax), `PARE100`-`PARE107` (rejections).
- Corpus location: microsoft/TypeScript `main` is now the Go codebase (TypeScript 7). Test
  inputs are under `tsc/testdata/tests/cases/{compiler,conformance}`, error baselines under
  `tsc/testdata/baselines/reference/{compiler,conformance}`.
- Deliberate lexer deviation: U+0085 (NEL) is invalid, as in ECMAScript; TypeScript treats it
  as whitespace. Three corpus files hit this.
- Added rejection `ParameterDecorator`: parameter decorators only exist under legacy
  `experimentalDecorators`, so they are the detectable part of that handoff row.
