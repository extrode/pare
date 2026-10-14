# Pare

A TypeScript type stripper in C# for .NET 10, NativeAOT-compatible. It overwrites type-only
syntax with spaces, so the output is JavaScript with every remaining character at its original
line and column. No source maps, no Node, no Bun, no Deno.

Pare does not type check. Use `tsgo --noEmit` for that.

Status: the library, the `pare` CLI and the MSBuild package work. A sample app is next. See [work/progress.md](work/progress.md).

## Build

```sh
dotnet build Pare.slnx
dotnet test --solution Pare.slnx
dotnet publish src/Extrode.Pare.Cli -c Release -r osx-arm64 -o tmp/aot   # NativeAOT CLI
dotnet pack src/Extrode.Pare.Build -c Release -o tmp/pack                # MSBuild package
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
An existing `foo.js` next to `foo.ts` is replaced, like `tsc` and `esbuild` do: keep hand-written
JavaScript out of the source folders, or use `--outdir`.

## MSBuild

```xml
<PackageReference Include="Extrode.Pare.Build" Version="0.1.0" PrivateAssets="all" />
```

Before `Build`, every `wwwroot/**/*.ts` and `*.mts` (not `.d.ts`, not `node_modules`) is stripped
to a `.js` next to it. The step is incremental, runs in-process and needs no Node. In web projects
the output becomes a static web asset from the first build on. Rejections fail the build as
`file(line,col): error PARE100: ...`. Runs under `dotnet build` (CLI, CI, Rider, VS Code), not
inside Visual Studio's .NET Framework MSBuild.

The `.ts` and `.d.ts` files are removed from `Content`, so they are neither served nor published;
`PareKeepSources=true` keeps them. An existing `foo.js` next to `foo.ts` is replaced on the next
build that strips `foo.ts`. `.cts` sources are not in the default glob, since CommonJS does not run
in a browser; an explicit `<TypeScriptSource Include="x.cts" />` produces `x.cjs`.

| Property | Default | |
|---|---|---|
| `EnablePareDefaultItems` | `true` | `false` drops the default glob; add `<TypeScriptSource Include="..." />` yourself |
| `PareOutDir` | empty | write under this directory, keeping relative paths |
| `PareTypeCheck` | `false` | `true` runs `tsgo --noEmit -p $(PareTsconfig)` first |
| `PareTsgoPath` | `tsgo` | |
| `PareTsconfig` | `tsconfig.json` in the project | |
| `PareKeepSources` | `false` | `true` serves and publishes the `.ts` sources next to the output |

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
  `import`, `export ... from` and `import("...")`: on in the CLI and MSBuild, opt-in in the library
- ASI fixes: a `)` moved to the end of an erased multi-line return type, a `(` moved to the
  start of erased type parameters followed by a line break, and a `;` written at the start of
  an erased statement or class member when the previous line could otherwise merge with the next,
  or at the end of an erased `as`/`satisfies` type when the next line starts with `(`, `[` or a
  template
