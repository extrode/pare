# Changelog

## [Unreleased]

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
