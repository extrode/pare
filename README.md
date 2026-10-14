# Pare

A TypeScript type stripper in C# for .NET 10, NativeAOT-compatible. It overwrites type-only
syntax with spaces, so the output is JavaScript with every remaining character at its original
line and column. No source maps, no Node, no Bun, no Deno.

Pare does not type check. Use `tsgo --noEmit` for that.

Status: the library and the `pare` CLI work. The MSBuild task is next. See [work/progress.md](work/progress.md).

## Build

```sh
dotnet build Pare.slnx
dotnet test --solution Pare.slnx
dotnet publish src/Extrode.Pare.Cli -c Release -r osx-arm64 -o tmp/aot   # NativeAOT CLI
```

## CLI

```
pare src                      # src/**/*.ts -> .js next to each source
pare src --outdir dist        # mirror into dist/
pare "src/**/*.ts" --check-only
pare --stdin < in.ts > out.js
```

Inputs are files, directories (searched recursively) or globs (`*`, `?`, `**`, for shells that
do not expand them). `.ts`, `.mts`, `.cts` become `.js`, `.mjs`, `.cjs`. `.d.ts` files and
`node_modules` are skipped. Exit codes: 0 ok, 1 rejected syntax, 2 I/O or usage error.

## Library

```csharp
bool ok = TypeStripper.Strip(source, output, diagnostics);
bool ok = TypeStripper.Strip(source, output, diagnostics, StripOptions.RewriteRelativeImportExtensions);
```

`source` is `ReadOnlySpan<char>`, `output` an `IBufferWriter<char>`, `diagnostics` an
`IDiagnosticSink`. Why UTF-16: [docs/decisions/2026-10-14-001-utf16-input.md](docs/decisions/2026-10-14-001-utf16-input.md).

## Non-whitespace changes

The output differs from the input only by spaces, except for:

- relative module specifiers `./x.ts` rewritten to `./x.js` (`.mts`/`.cts` likewise) in
  `import`, `export ... from` and `import("...")`: on in the CLI, opt-in in the library
- ASI fixes: a `)` moved to the end of an erased multi-line return type, a `(` moved to the
  start of erased type parameters followed by a line break, and a `;` written at the start of
  an erased statement or class member when the previous line could otherwise merge with the next
