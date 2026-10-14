# Pare

A TypeScript type stripper in C# for .NET 10, NativeAOT-compatible. It overwrites type-only
syntax with spaces, so the output is JavaScript with every remaining character at its original
line and column. No source maps, no Node, no Bun, no Deno.

Pare does not type check. Use `tsgo --noEmit` for that.

Status: M0 (scaffold). The stripper passes input through unchanged until the recognizer lands.
See [work/progress.md](work/progress.md).

## Build

```sh
dotnet build Pare.slnx
dotnet test --solution Pare.slnx
dotnet publish src/Extrode.Pare.Cli -c Release -r osx-arm64 -o tmp/aot   # NativeAOT CLI
```

## CLI

```
pare --stdin < in.ts > out.js
```

Exit codes: 0 ok, 1 rejected syntax, 2 I/O or usage error.

## Library

```csharp
bool ok = TypeStripper.Strip(source, output, diagnostics);
```

`source` is `ReadOnlySpan<char>`, `output` an `IBufferWriter<char>`, `diagnostics` an
`IDiagnosticSink`. Why UTF-16: [docs/decisions/2026-10-14-001-utf16-input.md](docs/decisions/2026-10-14-001-utf16-input.md).

## Non-whitespace changes

The output differs from the input only by spaces, except for (once implemented):

- relative import specifiers `./x.ts` rewritten to `./x.js` (CLI and MSBuild only)
- ASI fixes: a `)` moved to the end of an erased return type, and a `;` written at the start
  of an erased statement when the previous line could otherwise merge with the next
