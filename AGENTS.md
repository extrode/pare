# AGENTS.md

Pare: TypeScript type stripper in C# / .NET 10 NativeAOT. Full brief: [docs/handoff.md](docs/handoff.md).
Progress, next steps, corpus pass rate: [work/progress.md](work/progress.md), updated every session.

## Layout

- `src/Extrode.Pare/` core library (lexer, recognizer, eraser). **Zero package references.**
- `src/Extrode.Pare.Cli/` `pare` CLI, NativeAOT.
- `src/Extrode.Pare.Build/` MSBuild task + `build/*.props`/`.targets`, NuGet package. MSBuild
  packages are compile-only (approved 2026-10-14, 18.9.6). Loads under `dotnet build` only.
- `tests/Extrode.Pare.Tests/` xUnit v3 on Microsoft.Testing.Platform. Fast unit tests (<1 s).
- `tests/Extrode.Pare.Integration/` slow (~2 min): real `dotnet build`/`publish` on temp projects;
  the sample tests drive Chromium, Firefox and WebKit via Microsoft.Playwright (approved
  2026-10-15, 1.63.0; needs network for jsDelivr) over the build, publish and NativeAOT publish,
  and run the stripped modules in Node and Deno.
- `tests/Extrode.Pare.Conformance/` TypeScript corpus (`dotnet run scripts/get-ts-corpus.cs`) and the
  differential test against tsc (`dotnet run scripts/get-tsc.cs`); each skips when its input is
  missing.
- `tests/Extrode.Pare.Bench/` BenchmarkDotNet (approved 2026-10-15, 0.15.8) over
  `dotnet run scripts/get-bench-corpus.cs`, and `compare`: output and speed against Node, oxc,
  ts-blank-space, esbuild (npm, pinned in `compare/package.json`) and Deno.

## Build and test

```sh
dotnet build Pare.slnx
dotnet test --solution Pare.slnx
tests/Extrode.Pare.Tests/bin/Debug/net10.0/Extrode.Pare.Tests   # unit only, after a build
dotnet publish src/Extrode.Pare.Cli -c Release -r osx-arm64 -o tmp/aot
scripts/pack-cli.sh   # .NET tool + per-RID packages and archives into tmp/dist (~4 min, Docker for Linux)
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
