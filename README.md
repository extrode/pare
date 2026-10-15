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

```sh
dotnet tool install -g Extrode.Pare.Cli
```

Quickstart with a static-site sample, watch recipes and exit codes: [docs/cli-quickstart.md](docs/cli-quickstart.md).

The tool package installs the NativeAOT binary for osx-arm64, osx-x64, linux-arm64, linux-x64,
linux-musl-arm64, linux-musl-x64, win-x64 and win-arm64, and a framework-dependent build (needs
the .NET 10 runtime) on any other platform. The same binaries come as `pare-<version>-<rid>.tar.gz`
archives (`.zip` on Windows) with no .NET needed. `scripts/pack-cli.sh` writes them to `tmp/dist`:
macOS natively, Linux in Docker (`mcr.microsoft.com/dotnet/sdk:10.0` and `-alpine`), then runs
each Linux build 20 times and installs the tool on its own platform. Windows NativeAOT needs a
Windows machine with Visual Studio's C++ tools: `pwsh scripts/pack-cli.ps1` packs win-x64, and
win-arm64 when the ARM64 C++ tools are installed, and checks the host's build the same way
(win-arm64 is cross-compiled on x64 and checked on a Windows 11 ARM64 machine).
`-SkipPack` runs only that check, on archives already in `tmp/dist`, for a machine without Visual
Studio.

```
pare src                      # src/**/*.ts -> .js next to each source
pare src --outdir dist        # mirror into dist/
pare "src/**/*.ts" --check-only
pare --stdin < in.ts > out.js
```

Inputs are files, directories (searched recursively) or globs (`*`, `?`, `**`, for shells that
do not expand them). `.ts`, `.mts`, `.cts` become `.js`, `.mjs`, `.cjs`. `.d.ts` files and
`node_modules` are skipped. Exit codes: 0 ok, 1 rejected syntax ([error codes](docs/error-codes.md)),
2 I/O or usage error.
An existing `foo.js` next to `foo.ts` is replaced, like `tsc` and `esbuild` do: keep hand-written
JavaScript out of the source folders, or use `--outdir`.

## MSBuild

```xml
<PackageReference Include="Extrode.Pare.Build" Version="0.3.0" PrivateAssets="all" />
```

Before `Build`, every `wwwroot/**/*.ts` and `*.mts` (not `.d.ts`, not `node_modules`) is stripped.
The step is incremental, runs in-process and needs no Node. Rejections fail the build as
`file(line,col): error PARE100: ...` ([what each code means](docs/error-codes.md)). Runs under
`dotnet build` (CLI, CI, Rider, VS Code) and Visual Studio 2026 (MSBuild 18), where the task runs in
MSBuild's .NET task host, a separate process. Visual Studio 2022 cannot build `net10.0` projects.

In a web project (the Web SDK's static web assets), `wwwroot/js/app.ts` becomes
`obj/<configuration>/<tfm>/pare/js/app.js`, registered as the static web asset `js/app.js`: served
from the first build on, compressed and published like a file in `wwwroot`, and never in the source
tree, so nothing needs gitignoring. The build deletes the output of a deleted source. Elsewhere, and
for sources outside `wwwroot` without `PareServeAt` ([below](#typescript-outside-wwwroot)), the
`.js` goes next to its source.

- A `foo.js` next to `foo.ts` in `wwwroot` (left by Pare 0.2, or hand-written) is not served:
  `warning PARE200` names it, and the build serves the stripped `foo.ts`. Delete it.
- `.ts` and `.d.ts` files are removed from `Content`, so they are not static web assets and are not
  published; `PareKeepSources=true` keeps them. (In Development, `MapStaticAssets` still serves any
  file on disk, with a warning; `UseStaticFiles` serves everything under `wwwroot`.)
- Outside `obj/`, an existing `foo.js` next to `foo.ts` is replaced on the next build that strips
  `foo.ts`.
- `.cts` sources are not in the default glob, since CommonJS does not run in a browser; an explicit
  `<TypeScriptSource Include="x.cts" />` produces `x.cjs`.

Dev loop: in its default hot reload mode `dotnet watch` runs no build on a `.ts` save, so the
`.js` would go stale. `Extrode.Pare.AspNetCore` re-strips in the running app instead:

```xml
<PackageReference Include="Extrode.Pare.AspNetCore" Version="0.3.0" />
```

```csharp
builder.AddPareDevelopment();   // before builder.Build(); does nothing outside Development
```

In Development it strips stale sources when the app starts, then each `.ts`/`.mts` as it is
saved, to where the build writes (it reads `Extrode.Pare.development.json`, which the build puts
next to the app and never publishes); `dotnet watch` sees the new `.js` and reloads the page.
Rejections are logged in the `file:line:col: error PARE...` form and the old `.js` stays. Deleting
a source deletes its output under `obj/`. Pass `o => o.OutDir = "..."` when the project sets
`PareOutDir`. It also applies `dotnet watch`'s browser refresh to
`WebApplication.CreateSlimBuilder` apps, which never run the hosting startup that adds it.

- A `.ts` file added while `dotnet watch` runs is stripped, but the page gets 404 for it until the
  next build: static web assets are fixed at build time. The log says so; press Ctrl+R in
  `dotnet watch` to rebuild.
- A request for a deleted source's output answers 500 in Development until the next build.

- A `PublishAot` project needs `<StartupHookSupport Condition="'$(Configuration)' == 'Debug'">true</StartupHookSupport>`;
  without it `dotnet watch` says "Project does not support Hot Reload".
- Run `dotnet watch` from the project directory. With `--project` from elsewhere, .NET SDK
  10.0.401 on macOS fails to read `obj\Debug/.../staticwebassets.development.json` and never
  refreshes the page.
- Without the package, `dotnet watch --no-hot-reload` rebuilds and restarts on each save
  (about 3 s on the sample); reload the page yourself.

| Property | Default | |
|---|---|---|
| `EnablePareDefaultItems` | `true` | `false` drops the default glob; add `<TypeScriptSource Include="..." />` yourself |
| `PareOutDir` | empty | write under this directory, keeping relative paths, instead of `obj/` or next to each source; not served, `.razor.js` and `.cshtml.js` included |
| `PareTypeCheck` | `false` | `true` runs `tsc --noEmit -p $(PareTsconfig)` first (TypeScript 7's native `tsc`) |
| `PareTscPath` | `tsc` | e.g. `$(MSBuildThisFileDirectory).tools/typescript/tsc` |
| `PareTsconfig` | `tsconfig.json` in the project | never copied to the build or publish output |
| `PareKeepSources` | `false` | `true` serves and publishes the `.ts` sources next to the output |

### TypeScript outside wwwroot

Give the item a `PareServeAt`: the URL folder to serve it under. The file's name and extension
come from the source, and the folders matched by `**` are kept:

```xml
<TypeScriptSource Include="Scripts/**/*.ts;Scripts/**/*.mts" PareServeAt="js" />
```

| Source | Served at |
|---|---|
| `Scripts/app.ts` | `/js/app.js` |
| `Scripts/lib/fmt.ts` | `/js/lib/fmt.js` |
| `Scripts/worker.mts` | `/js/worker.mjs` |

Output goes under `obj/` like a `wwwroot` source's: served from the first build, compressed and
published. A Blazor component's script, served beside the component as the SDK serves a
hand-written `Counter.razor.js`:

```xml
<TypeScriptSource Include="**/*.razor.ts" Exclude="bin/**;obj/**;node_modules/**" PareServeAt="/" />
```

`Components/Pages/Counter.razor.ts` is served at `/Components/Pages/Counter.razor.js`.

- `/` is the root. Leading and trailing slashes are ignored, and `\` counts as `/`.
- The URL is the folder, then the part of the path matched by `**` in the `Include` that created
  the item, then the file name. Without `**` (`Scripts/*.ts`) there is no subfolder.
- In a Razor class library the folder is below `/_content/<Library>/`, like `wwwroot`.
- A `wwwroot` source with `PareServeAt` is served there instead of at its `wwwroot` path.
- Not a folder (`..`, `:`, `//`, or a file name such as `js/app.js`): `error PARE203`. Two sources
  at one URL: `error PARE201`.
- A source outside `wwwroot` without `PareServeAt` is not served: `warning PARE202`. Add
  `PARE202` to `NoWarn` if its `.js` is meant to stay next to it. Not reported for a `.razor.ts`
  or `.cshtml.ts`, whose `.js` beside the component the SDK serves.
- Not re-stripped on save yet: `AddPareDevelopment` covers `wwwroot` only. Under `dotnet watch`,
  rebuild after editing these sources (Ctrl+R).

### Razor class libraries

Reference `Extrode.Pare.Build` from the library; apps that use it need nothing. The library's
`wwwroot/js/widget.ts` is served to those apps as `/_content/<Library>/js/widget.js`, published
with compressed copies, and packed by `dotnet pack` as the stripped `.js` only, with no dependency on
Pare. `AddPareDevelopment` re-strips only the app's own sources on save, not the library's;
rebuild after editing a library source.

## Sample

`samples/WebApp`: ASP.NET Core NativeAOT, an Alpine component and modules written in TypeScript,
an htmx call to a minimal API. It references the package from a local feed, as a consumer would:

```sh
scripts/pack-local.sh               # pack into tmp/feed as 0.3.0-dev.<time>, re-restore the sample
dotnet run --project samples/WebApp   # http://localhost:5180, Development
dotnet watch            # from samples/WebApp: a .ts save re-strips and reloads the page
dotnet publish samples/WebApp -c Release -r osx-arm64 -o tmp/sample-aot
```

The package keeps `$(PareTsconfig)` out of the build and publish output; the sample marks its
`nuget.config` `CopyToPublishDirectory="Never"` by hand.
The launch profile avoids port 5000, which macOS AirPlay Receiver also listens on.

`tests/Extrode.Pare.Integration` checks the sample end to end, packing the package into a private
feed first:

- the Development build, the Release publish and the NativeAOT publish each served and clicked
  through in Chromium, Firefox and WebKit (Playwright). The counter and the htmx call must work,
  with no console error and no failed request, and the modules the browser loads must be Pare's
  output at the TypeScript positions.
- published output holds only `.js` with `.br`/`.gz` copies; `.ts`, `.d.ts` and `tsconfig.json`
  answer 404, and `counter.js` comes back brotli-compressed as `text/javascript`.
- the stripped `counter.js` imported and run in Node and in Deno.
- `dotnet watch` on the sample: a `.ts` edit is re-stripped and Chromium reloads to the new text
  with no restart.

The first run downloads the three browsers (about 250 MB) into Playwright's cache. Alpine and
htmx load from jsDelivr, so the tests need network.

## Library

```csharp
bool ok = TypeStripper.Strip(source, output, diagnostics);
bool ok = TypeStripper.Strip(source, output, diagnostics, StripOptions.RewriteRelativeImportExtensions);
```

`source` is `ReadOnlySpan<char>`, `output` an `IBufferWriter<char>`, `diagnostics` an
`IDiagnosticSink`. Deep nesting fails with a diagnostic, not a stack overflow; on linux-musl-arm64
NativeAOT call `Strip` from a thread you created, since .NET misreads the main thread's stack size
there and the check fails at once. Why UTF-16: [docs/decisions/2026-10-14-001-utf16-input.md](docs/decisions/2026-10-14-001-utf16-input.md).

## Non-whitespace changes

The output differs from the input only by spaces, except for:

- relative module specifiers `./x.ts` rewritten to `./x.js` (`.mts`/`.cts` likewise) in
  `import`, `export ... from` and `import("...")`: on in the CLI and MSBuild, opt-in in the library
- ASI fixes: a `)` moved to the end of an erased multi-line return type, a `(` moved to the
  start of erased type parameters followed by a line break, and a `;` written at the start of
  an erased statement or class member when the previous line could otherwise merge with the next,
  at the end of an erased `as`/`satisfies` type when the next line starts with `(`, `[` or a
  template, and at the end of the erased type of a field named `get`, `set` or `static`

## Real-world check

`scripts/run-std-tests.cs` runs Deno's standard library test suite on Pare's output. It strips a
copy of [denoland/std](https://github.com/denoland/std) (pinned commit, 1,600+ files) with the
`pare` CLI in about 0.5 s, points the `@std/*` imports at the `.js` files, and runs `deno test` on
each package's original `*_test.ts` and stripped `*_test.js`:

- 38 of 42 packages give the same result (4,997 passing tests); 5,521 tests pass on Pare's output in all.
- csv: one test compares `Function.prototype.toString()` with Deno's reprinted `(n)=>n`; a
  blanking stripper keeps `(n        ) => n`.
- testing: snapshot tests look for a snapshot file named after the `.ts` test file.
- http: one test passes only on the `.js` copy, because the TypeScript copy sits under a dot
  directory (`.corpus`) that `serveDir()` refuses.
- expect: not run. Pare rejects three of its files (parameter properties and a `<T>x` assertion,
  which generate runtime code), so `expect.js` is missing.

Failures common to both runs (cli, encoding, fmt, fs and others) come from the environment.

```sh
dotnet run scripts/get-bench-corpus.cs
dotnet publish src/Extrode.Pare.Cli -c Release -r osx-arm64 -o tmp/aot
dotnet run scripts/run-std-tests.cs            # or name packages: ... -- yaml semver
```

## Performance

Measured on an Apple M1 Max (osx-arm64), .NET 10.0, Release build.

BenchmarkDotNet 0.15.8, one `TypeStripper.Strip` call per source, output to a reused buffer:

| Input | Mean | MB/s | Allocated |
|---|---:|---:|---:|
| TypeScript 5.9.3 `checker.ts` (3.1 MB) | 13.3 ms | 235 | 0 B |
| Real-world corpus, 2,392 files (19.5 MB) | 75.0 ms | 218 | 0 B |
| `(a = ` nested 1,000 deep | 38.1 ms | 0.2 | 0 B |

Deep nesting is quadratic, as in TypeScript's own parser; real code never reaches it.

The same 2,392 files (TypeScript's compiler, Vue, Hono, Deno std, Svelte, Zod at pinned
commits) through other tools. Each tool's output is compared with Pare's byte for byte, then as
an Acornima syntax tree without locations. Printers (esbuild, oxc, Deno) reprint literals, so the
tree comparison ignores literal spelling, `{ a: a }` versus `{ a }` and `void 0` versus `undefined`.
For printers, a looser comparison also ignores imports (they drop unused ones) and a numeric suffix
on names (they rename a shadowed `x` to `x1`). Deno is Deno's own transpiler (swc, as `deno run`
uses it), read from its cache after `deno cache --no-check`.

| Tool | Same bytes | Same tree | Same but imports dropped or names renamed | Differ | Throughput | Pare is |
|---|---:|---:|---:|---:|---:|---:|
| Pare 0.1.0 | - | - | - | - | 184-229 MB/s | - |
| Node 24.16 `stripTypeScriptTypes` | 1,898 | 490 | - | 0 | 35-38 MB/s | 5-7x faster |
| oxc-transform 0.154.0 | 13 | 2,375 | 0 | 0 | 69-81 MB/s | 2-3x faster |
| ts-blank-space 0.9.0 | 1,967 | 420 | - | 1 | 25-27 MB/s | 7-9x faster |
| esbuild 0.28.2 | 11 | 1,961 | 296 | 120 | 9-10 MB/s | 19-26x faster |
| Deno 2.9.7 (swc) | 11 | 2,326 | 51 | 0 | - | - |

- Same tree but not same bytes: Node and ts-blank-space keep tabs and write UTF-8-width spaces for
  non-ASCII comment text, and Pare writes its ASI `;` in a few places they leave blank.
- ts-blank-space's one difference is a bug on its side: `get!: T` followed by another field on the
  next line becomes a getter, and its output does not parse. Pare had the same bug until this
  comparison found it.
- Deno's 51: it drops imports whose names are unused, including `import {} from "m"`, which still
  loads `m` (Pare keeps it, as `verbatimModuleSyntax` requires), and renames shadowed bindings.
- esbuild's 120 left: it turns strings with line breaks into template literals and folds some
  expressions; none is a Pare error.
- Four files are left out: their output does not parse for any stripper, because they import or
  re-export a type without `type`, which needs type information to remove.
- Whole process over all files: `pare` CLI (NativeAOT) 0.5-0.8 s, `deno cache --no-check` from a
  cold cache 1.3-2.1 s.

Throughput ranges are over separate runs on a laptop; BenchmarkDotNet's numbers above are the
steady ones. To reproduce:

```sh
dotnet run scripts/get-bench-corpus.cs                       # ~25 MB into .corpus/realworld
npm --prefix tests/Extrode.Pare.Bench/compare ci --ignore-scripts
dotnet publish src/Extrode.Pare.Cli -c Release -r osx-arm64 -o tmp/aot
dotnet run -c Release --project tests/Extrode.Pare.Bench -- compare --passes 5
dotnet run -c Release --project tests/Extrode.Pare.Bench -- --filter '*'
```

`compare` writes every tool's output under `tmp/compare/` and the differences to
`tmp/compare/differences.tsv`.

## License

Pare is licensed under the Islamic Software License - Permissive (ISL-P) v1.2 WITH the Output
Exception (ISL-OE) v1.2: a source-available, ethically restricted license rooted in Islamic
principles. It is not an OSI-approved open-source license. The Output Exception lets you ship the
JavaScript Pare generates as part of your own work. See [LICENSE.md](LICENSE.md), including the
ethical use restrictions in Sections 4 and 5, and [NOTICE.md](NOTICE.md).
