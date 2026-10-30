# Changelog

## [Unreleased]

- Docs rewritten as a site: overview, a step-by-step first web app and first static site, guides and
  a reference, built with `scripts/build-docs.sh`. `docs/error-codes.md` moved to
  `docs/06-reference/error-codes.md`; the README is shorter.

## [0.6.1] - 2026-10-30

- No PARE200 for a compiled source whose output keeps its name (`clock.svelte.js` to
  `clock.svelte.js`, `index.html` to `index.html`): 0.6.0 reported the source itself as a stale
  output and told the user to delete it.

## [0.6.0] - 2026-10-17

- Other compile-to-JavaScript steps run on Pare's plumbing (decision 005): a package declares
  `<PareCompileSource Include="**/*.svelte" PareCompiler="husk" PareOutputExtension=".svelte.js" />`
  and adds its target to `PareCompileDependsOn`; Pare gives each source `PareOutputPath` and
  `PareRelativePath` by the `.ts` rules and serves, publishes, embeds and deletes the outputs.
  New `error PARE204` for an item without a `PareCompiler` name or a `PareOutputExtension`. The
  development map lists them under `compiled`, which 0.5's `AddPareDevelopment` ignores.
- `IPareCompiler` in `Extrode.Pare.AspNetCore`: registered in DI, `AddPareDevelopment` runs its
  `CompileAsync` on save for its sources, one at a time per compiler. `AddPareDevelopment` called
  twice now adds one watcher, and the second call's options apply too.
- On save, output whose text is unchanged gets a new timestamp, so the next start does not
  strip or compile its source again.
- PARE200, PARE201 and PARE202 messages no longer say "TypeScript": they cover compiled sources too.

## [0.5.0] - 2026-10-17

- `libpare`: the stripper as a C shared library with `pare.h`, for other languages over FFI.
  UTF-8 in and out, diagnostics with line, UTF-16 column and byte offsets, thread-safe.
  Archives `libpare-<v>-<rid>` for the CLI's 8 RIDs from `pack-cli.sh` and `pack-cli.ps1`.

- Faster: `checker.ts` 13.3 -> 9.4 ms (235 -> 333 MB/s), the real-world corpus 75 -> 60 ms
  (218 -> 273 MB/s). Output and diagnostics are unchanged on all 15,462 corpus files. ASCII fast
  path for identifiers, trivia skipped on a local position, the stack checked every 16th
  recursion. Nesting too deep for the stack still fails with a syntax error, now with a test.
- CLI: built with `OptimizationPreference=Speed`, and reads each directory once when walking
  (it read each twice). `pare .corpus/realworld --check-only` 0.60 -> 0.35 s.

## [0.4.0] - 2026-10-17

- `PareEmbed=true` (with `PareEmbedPrefix`) embeds every output in the assembly, named after its
  path below the project, written under `obj/`; for projects that are not web projects. Outputs
  are not `Content` or static web assets there, and a deleted source drops its resource.
- `@(PareOutput)` is public: every output, with `PareSource` and `PareRelativePath`. A project's
  `AfterTargets="PareStripTypeScript"` target sees it on every build. `PareStripTypeScript` now
  has no body and depends on the strip (`_PareStrip`) and the registration; `PareTypeCheck` runs
  as its dependency instead of through `BeforeTargets`.
- `string? TypeStripper.Strip(string source, out IReadOnlyList<Diagnostic> diagnostics)`. The
  library API is documented as stable for 0.x.
- `scripts/get-tsc.cs --dest <dir>` works outside the Pare repository; README section on type
  checking in CI without npm.
- Sample: a Jint module loader that strips `.ts` on import (`JintModuleTests`).
- README: restoring vendored `.nupkg` files offline, non-web projects.
- `PareServeAt` serves TypeScript outside `wwwroot`:
  `<TypeScriptSource Include="Scripts/**/*.ts" PareServeAt="js" />` serves `Scripts/lib/fmt.ts`
  at `/js/lib/fmt.js`, and `.mts` as `.mjs`. `PareServeAt="/"` serves Blazor `**/*.razor.ts`
  beside their components.
- `AddPareDevelopment` re-strips `PareServeAt` sources on save, and the sources of Razor class
  libraries the app references as projects. The development map now lists each source and its
  output, and each project's map also reaches the build output of projects that reference it as
  `Extrode.Pare.development.<project>.json`. Maps from 0.3 still work.
- PARE202 warning: a source outside `wwwroot` with no `PareServeAt` is not served. Builds that
  include such sources now show it; add `PARE202` to `NoWarn` to keep the output next to the source.
- PARE203 error: a `PareServeAt` that is not a folder.
- PARE201: two TypeScript sources at the same URL now fail with a Pare error naming both,
  instead of crashing the static web assets SDK (MSB4018).
- Razor class libraries: tested and documented (`/_content/<Lib>/...`).
- CLI quickstart (`docs/cli-quickstart.md`) and the `samples/CliSite` static-site sample.
- Fixed: `PareOutDir` with a `.razor.ts` or `.cshtml.ts` source failed the build with BLAZOR106
  (a `.razor.js` with no component beside it). The output is written there and not served.

## [0.3.0] - 2026-10-15

### Upgrading from 0.2

- Web projects now get their `.js` under `obj/` by default; set `PareOutDir` to keep 0.2's
  output next to each source.
- Delete the `.js` files 0.2 generated in `wwwroot` (`warning PARE200` names each one until you
  do), and drop any `.gitignore` rules for them.
- A `.ts` file added while `dotnet watch` runs is served after the next build (Ctrl+R); edits to
  existing files reload as before.

### Changes

- Web projects: `Extrode.Pare.Build` writes the output of `wwwroot/**/*.ts` under
  `obj/<configuration>/<tfm>/pare/` and registers it as static web assets (served, compressed and
  published as before), instead of next to each source. Breaking for anything that read the `.js`
  from `wwwroot` on disk. `PareOutDir` turns it off. A `foo.js` left next to `foo.ts` is not served
  and gets `warning PARE200`.
- `docs/error-codes.md`: every diagnostic (PARE001, PARE100-PARE107, PARE200) with an example
  that triggers it, why, and what to write instead. README links it.
- The build deletes the output of a deleted source; so does `AddPareDevelopment` while the app
  runs. `AddPareDevelopment` writes where the build does, read from
  `Extrode.Pare.development.json` in the build output. A source added during `dotnet watch` is
  served after the next build.
- `Extrode.Pare.Build` runs under Visual Studio 2026's MSBuild (MSBuild 18, .NET Framework) in the
  .NET task host, without MSBuild's out-of-process fallback warning.

## [0.2.0] - 2026-10-15

- `Extrode.Pare.AspNetCore`: `builder.AddPareDevelopment()` re-strips `wwwroot` TypeScript as it
  is saved while the app runs in Development, so `dotnet watch` hot reload reloads the page with
  fresh JavaScript. It also gives `CreateSlimBuilder` apps `dotnet watch`'s browser refresh.
- The core library packs as `Extrode.Pare`.
- `pare` CLI as a .NET tool: `dotnet tool install -g Extrode.Pare.Cli` installs the NativeAOT
  binary for osx, linux, linux-musl and win (arm64 and x64), and a framework-dependent build on other
  platforms. `scripts/pack-cli.sh` packs the tool packages and `pare-<version>-<rid>.tar.gz`
  archives, building Linux in Docker.
- Fixed: on linux-musl-arm64 (Alpine), about one `pare` process in four rejected every input with
  `PARE001` at 1:1. .NET misreads the main thread's stack size there, so the parser's depth guard
  failed at once; the CLI now runs on a thread with an 8 MB stack.

## [0.1.0] - 2026-10-15

- License: Islamic Software License - Permissive (ISL-P) 1.2 WITH the Output Exception (ISL-OE)
  1.2 (`LICENSE.md`, `NOTICE.md`). The package carries both files, the README and author metadata.
- Solution scaffold, public API sketch (`TypeStripper.Strip`, `Diagnostic`, `IDiagnosticSink`),
  `pare --stdin` CLI publishing as NativeAOT, CI on Linux, Windows and macOS.
- Lexer for all JS and TS tokens: templates, regex and `>` rescans, contextual keywords,
  Unicode identifiers. `scripts/get-ts-corpus.cs` fetches the TypeScript corpus at a pinned
  commit; the lexer round-trips all 17,582 script files in it.
- Type stripping: a TS-aware recognizer erases every handoff section 5 construct, rejects the
  section 6 ones with `PARE1xx` diagnostics, and applies the ASI fixes (`;`, `)` and `(` moves).
  Corpus: 0 syntax errors on 13,935 files, plain `.js` byte-identical.
- `pare` CLI file mode: files, directories and globs, `--outdir`, `--check-only`; relative
  `./x.ts` specifiers rewritten to `./x.js` (`StripOptions.RewriteRelativeImportExtensions`).
- `Extrode.Pare.Build` NuGet package: incremental in-process strip of `wwwroot/**/*.ts` before
  `Build`, outputs registered as static web assets, `PareOutDir`, optional `tsgo` type check.
- MSBuild: `.ts` and `.d.ts` sources are no longer served or published; `PareKeepSources=true`
  keeps them.
- Differential test (M5): Pare's output matches TypeScript 7.0.2 `tsc` emit as an Acornima tree on
  99.96% of the stripped corpus, every other file triaged.
- Fixed: `declare` followed by a keyword that does not start a declaration (`declare namespace = x`,
  `declare type = 1`) is a syntax error, as in TypeScript; it was erased. Found by the cross-tool
  comparison.
- Fixed: a field named `get`, `set` or `static` whose erased `!`, `?` or type ended at a line break
  merged with the next member (`get!: T` then `post!: T` became a getter named `post`); Pare now
  writes `;` there. Found in Hono by the cross-tool comparison.
- Benchmarks (M7): `tests/Extrode.Pare.Bench` with BenchmarkDotNet (218 MB/s on 2,392 real-world
  files, 0 B allocated per call) and a `compare` mode against Node, oxc, ts-blank-space, esbuild
  and Deno's own transpiler; `scripts/get-bench-corpus.cs` fetches the corpus. A test asserts 0 B per `Strip` call.
- `scripts/run-std-tests.cs`: Deno std's own tests on Pare's output; 38 of 42 packages give the
  same result as the TypeScript sources, the other 4 differ for reasons outside Pare.
- Sample app tests: build, publish and NativeAOT publish clicked through in Chromium, Firefox and
  WebKit; stripped modules run in Node and Deno.
- Fixed: a namespace holding only a bodiless function declaration (`function f();`) is now
  rejected; TypeScript counts it as a value.
- `scripts/get-tsc.cs`: downloads TypeScript 7's native `tsc` (7.0.2) from the npm registry into
  `.tools/typescript`, checking the tarball's sha512.
- MSBuild: `$(PareTsconfig)` is no longer copied to the build or publish output (the Web SDK
  makes every `.json` Content).
- `samples/WebApp`: NativeAOT web app with Alpine and htmx modules in TypeScript, built from the
  packed package (`scripts/pack-local.sh`). Runs on port 5180 in Development; calls
  `UseStaticWebAssets` so an unpublished run serves compressed assets. A Playwright test clicks
  through it in headless Chromium.
- Fixed: `as`/`satisfies` before a line starting `(`, `[` or a template; namespaces with
  `declare` values now rejected; unreadable input directories exit 2 instead of crashing.
- MSBuild: `PareTsgoPath` renamed to `PareTscPath`, default `tsc` (TypeScript 7's native compiler
  installs as `tsc`).
- MSBuild: TypeScript sources are `Watch` items, so `dotnet watch --no-hot-reload` rebuilds on a
  `.ts` save. Hot reload mode does not build on `wwwroot` changes; M8 will cover it.
- Tests: mutation sweep; 16 rows added where breaking the code left the suite green.
