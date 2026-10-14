# AGENTS.md

Pare: TypeScript type stripper in C# / .NET 10 NativeAOT. Full brief: [docs/HANDOFF.md](docs/HANDOFF.md).
Progress, next steps, corpus pass rate: [work/progress.md](work/progress.md), updated every session.

## Layout

- `src/Extrode.Pare/` core library (lexer, recognizer, eraser). **Zero package references.**
- `src/Extrode.Pare.Cli/` `pare` CLI, NativeAOT.
- `tests/Extrode.Pare.Tests/` xUnit v3 on Microsoft.Testing.Platform.
- Planned: `src/Extrode.Pare.Build/` (M6), `tests/Extrode.Pare.Conformance/` (M5),
  `tests/Extrode.Pare.Bench/` (M7).

## Build and test

```sh
dotnet build Pare.slnx
dotnet test --solution Pare.slnx
dotnet publish src/Extrode.Pare.Cli -c Release -r osx-arm64 -o tmp/aot
```

## Rules

- Hot path: no allocation, LINQ, regex, reflection, `dynamic` or boxing. No AST objects; the
  recognizer records erase ranges.
- Grammar questions: TypeScript's `parser.ts` is the reference. Match it and cite the function name.
- No packages in the core library. Ask before adding any package anywhere else.
- Tests first for each row of handoff sections 5, 6 and 7.
- Agent-written files: a `\u2028`-style escape in tool input lands on disk as the raw
  character, and a raw line separator inside a C# string is a compile error. Run
  `scripts/fix-escapes.sh` after writing C# that contains escapes. C# sources stay ASCII.
