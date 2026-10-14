# Progress

Replaces the handoff's root `PROGRESS.md` (root holds only README, AGENTS, CHANGELOG by repo
convention). Update at the end of every session.

## Done

- **M0 (2026-10-14):** solution `Pare.slnx`, core library `Extrode.Pare`, CLI `pare`
  (`--stdin`, `--help`, `--version`), xUnit v3 tests (30 pass), CI workflow for Linux, Windows,
  macOS including NativeAOT publish and a smoke test. Local `osx-arm64` AOT publish: 1.1 MB, no
  AOT/trim warnings. UTF-16 decision: [docs/decisions/2026-10-14-001-utf16-input.md](../docs/decisions/2026-10-14-001-utf16-input.md).

## Next

- **M1 lexer:** all JS and TS tokens, templates with brace-depth stack, regex vs division by
  previous token, contextual keywords. Lexer state is a struct for cheap save/rewind.
- `scripts/get-ts-corpus.cs`: sparse clone of `microsoft/TypeScript` `tests/cases/{conformance,compiler}`
  at a pinned commit, into `.corpus/` (gitignored). M1's done-condition needs it.

## Open questions

- CI has not run: the repo has no remote yet.
- Version pinning of the corpus commit: pick when writing `get-ts-corpus.cs`.

## Corpus pass rate

Not measured yet (M5).

## Deviations from the handoff

- Name: **Pare** (folder name) instead of TsBlank. Projects `Extrode.Pare.*`, CLI `pare`,
  static entry `TypeStripper.Strip` (a class named `Pare` would collide with the namespace).
- `PROGRESS.md` lives here as `work/progress.md`; the handoff stays at `docs/HANDOFF.md`.
- Diagnostic codes print as `PARE001` (syntax), `PARE100`-`PARE107` (rejections).
- Added rejection `ParameterDecorator`: parameter decorators only exist under legacy
  `experimentalDecorators`, so they are the detectable part of that handoff row.
