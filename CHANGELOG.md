# Changelog

## [Unreleased]

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
