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

## Next

- **M2 recognizer** for JS statements and expressions, driving the lexer rescans. Done when
  plain JS passes through byte-identical. Start with the corpus `.js` units as fixtures.

## Open questions

- CI has not run: the repo has no remote yet.
- Lexer leniency vs TypeScript (escape and numeric-literal validation): see `work/todo.md`.

## Corpus pass rate

Not measured yet (M5).

## Deviations from the handoff

- Name: **Pare** (folder name) instead of TsBlank. Projects `Extrode.Pare.*`, CLI `pare`,
  static entry `TypeStripper.Strip` (a class named `Pare` would collide with the namespace).
- `PROGRESS.md` lives here as `work/progress.md`; the handoff stays at `docs/HANDOFF.md`.
- Diagnostic codes print as `PARE001` (syntax), `PARE100`-`PARE107` (rejections).
- Corpus location: microsoft/TypeScript `main` is now the Go codebase (TypeScript 7). Test
  inputs are under `tsc/testdata/tests/cases/{compiler,conformance}`, error baselines under
  `tsc/testdata/baselines/reference/{compiler,conformance}`.
- Deliberate lexer deviation: U+0085 (NEL) is invalid, as in ECMAScript; TypeScript treats it
  as whitespace. Three corpus files hit this.
- Added rejection `ParameterDecorator`: parameter decorators only exist under legacy
  `experimentalDecorators`, so they are the detectable part of that handoff row.
