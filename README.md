# Pare

A TypeScript type stripper in C# for .NET 10, NativeAOT-compatible. It overwrites type-only
syntax with spaces, so the output is JavaScript with every remaining character at its original
line and column. No source maps, no Node, no Bun, no Deno.

Pare does not type check. Use TypeScript 7's native `tsc --noEmit` (formerly `tsgo`) for that;
`dotnet run scripts/get-tsc.cs` downloads it to `.tools/typescript/tsc` without npm.

Status: the library, the `pare` CLI and the MSBuild package work; `samples/WebApp` uses the package.
On the TypeScript test corpus, output matches TypeScript 7 `tsc` emit (compared as syntax trees) for
99.96% of the files Pare strips; the rest are triaged differences on tsc's side.
See [work/progress.md](work/progress.md).

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

The `.ts` and `.d.ts` files are removed from `Content`, so they are not static web assets and are
not published; `PareKeepSources=true` keeps them. (In Development, `MapStaticAssets` still serves
any file on disk, with a warning; `UseStaticFiles` serves everything under `wwwroot`.) An existing `foo.js` next to `foo.ts` is replaced on the next
build that strips `foo.ts`. `.cts` sources are not in the default glob, since CommonJS does not run
in a browser; an explicit `<TypeScriptSource Include="x.cts" />` produces `x.cjs`.

Dev loop: run `dotnet watch --no-hot-reload`. Saving a `.ts` file rebuilds and restarts the app
(about 3 s on the sample); reload the page. In its default hot reload mode `dotnet watch` treats
`wwwroot` files as static assets and runs no build, so the `.js` goes stale until the next build.

| Property | Default | |
|---|---|---|
| `EnablePareDefaultItems` | `true` | `false` drops the default glob; add `<TypeScriptSource Include="..." />` yourself |
| `PareOutDir` | empty | write under this directory, keeping relative paths |
| `PareTypeCheck` | `false` | `true` runs `tsc --noEmit -p $(PareTsconfig)` first (TypeScript 7's native `tsc`) |
| `PareTscPath` | `tsc` | e.g. `$(MSBuildThisFileDirectory).tools/typescript/tsc` |
| `PareTsconfig` | `tsconfig.json` in the project | never copied to the build or publish output |
| `PareKeepSources` | `false` | `true` serves and publishes the `.ts` sources next to the output |

## Sample

`samples/WebApp`: ASP.NET Core NativeAOT, an Alpine component and modules written in TypeScript,
an htmx call to a minimal API. It references the package from a local feed, as a consumer would:

```sh
scripts/pack-local.sh               # pack into tmp/feed as 0.1.0-dev.<time>, re-restore the sample
dotnet run --project samples/WebApp   # http://localhost:5180, Development
dotnet watch --no-hot-reload --project samples/WebApp   # re-strips on each .ts save
dotnet publish samples/WebApp -c Release -r osx-arm64 -o tmp/sample-aot
```

Generated `.js` is gitignored next to its source (`samples/WebApp/.gitignore`). The package
keeps `$(PareTsconfig)` out of the build and publish output; the sample marks its `nuget.config`
`CopyToPublishDirectory="Never"` by hand.
The launch profile avoids port 5000, which macOS AirPlay Receiver also listens on.

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

## License

Pare is licensed under the Islamic Software License - Permissive (ISL-P) v1.2 WITH the Output
Exception (ISL-OE) v1.2: a source-available, ethically restricted license rooted in Islamic
principles. It is not an OSI-approved open-source license. The Output Exception lets you ship the
JavaScript Pare generates as part of your own work. See [LICENSE.md](LICENSE.md), including the
ethical use restrictions in Sections 4 and 5, and [NOTICE.md](NOTICE.md).
