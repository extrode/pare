# Progress

Replaces the handoff's root `PROGRESS.md` (root holds only README, AGENTS, CHANGELOG by repo
convention). Update at the end of every session.

## Done

- **M0 (2026-10-14):** solution `Pare.slnx`, core library `Extrode.Pare`, CLI `pare`
  (`--stdin`, `--help`, `--version`), xUnit v3 tests (30 pass), CI workflow for Linux, Windows,
  macOS including NativeAOT publish and a smoke test. Local `osx-arm64` AOT publish: 1.1 MB, no
  AOT/trim warnings. UTF-16 decision: [docs/decisions/2026-10-14-001-utf16-input.md](../docs/decisions/2026-10-14-001-utf16-input.md).

- **M1 (2026-10-14):** lexer in `src/Extrode.Pare/Lexing/`. Design follows TypeScript's
  `scanner.ts`: `Next()` is context-free; the parser calls `ReScanSlashAsRegex`,
  `ReScanTemplateContinuation` and `ReScanGreaterThan` where only the grammar knows the goal.
  So the lexer holds no brace stack and `LexerState` is 4 ints + 3 bytes, copied to save and
  restore. The handoff's "previous token decides regex" and "brace-depth stack" live only in the
  test harness `tests/Extrode.Pare.Tests/Lexing/Tokenizer.cs`, used for the corpus round-trip.
  `scripts/get-ts-corpus.cs` pins microsoft/TypeScript `9eedcb0` (fetch ~15 s, 91 MB).
  Corpus round-trip (`tests/Extrode.Pare.Conformance`): 12,503 cases split into 17,582 script
  files (794 tsx/jsx/json units skipped), ~1.2 s debug. Error tokens: 88 files where TypeScript's
  baseline also has a scanner error, 8 named deviations, 0 unexpected.

- **M2 + M3 (2026-10-14):** one TS-aware recognizer in `src/Extrode.Pare/Parsing/` (built
  together: a JS-only recognizer would have been rewritten for M3). A `ref struct` recursive
  descent parser modelled on TypeScript's `parser.ts`. It records erase ranges (`Edit`) and
  rejections in pooled lists; `EditWriter` blanks the ranges and keeps line terminators.
  Speculation (`<` type arguments, parenthesized arrows) saves and restores lexer state and list
  counts, with TS's `notParenthesizedArrow` memo. A syntax error jumps the lexer to EOF, so every
  loop unwinds without exceptions. ASI fixes: `;` written at the start of an erased statement or
  member unless the previous token ends in `;` or `{`; `)` moved to the end of a multi-line
  return type; `(` moved to the start of type parameters that precede a line break.
  All section 6 rejections are reported in one pass. Unit tests: one per row of handoff
  sections 5, 6, 7 (`tests/Extrode.Pare.Tests/StripTests.cs`, 227 tests total).

- **M6 CLI (2026-10-14):** `pare <files|dirs|globs...> [--outdir dir] [--check-only]`.
  Output goes next to each source or is mirrored under `--outdir` from the inputs' common
  directory. Every file is processed even after a rejection. Globs are matched by a small
  hand-written matcher (`Glob.cs`), since no packages are allowed. The specifier rewrite lives in
  the core as `StripOptions.RewriteRelativeImportExtensions`, so the MSBuild task can reuse it:
  one same-length `Edit` with a `Replace` char per specifier. AOT binary: 1.2 MB, no trim
  warnings.

- **M6 MSBuild (2026-10-14):** `Extrode.Pare.Build` package: `StripTypeScript` task in
  `tasks/net10.0/` next to `Extrode.Pare.dll`, `build/Extrode.Pare.Build.{props,targets}`.
  Default items are `wwwroot/**/*.ts;*.mts`, set in `.props`, where they still see properties
  set in the project body. The target runs before `BeforeBuild`. Inputs/Outputs are the source,
  the task dll and the `.targets` file. Outputs are re-added to `Content` in a target that always
  runs. Output paths stay project-relative because static web assets match `wwwroot/**` against
  the item spec: absolute paths left the output out of the assets on the first build (bug found
  by the Web SDK test, fixed). Optional tsgo check through `Exec`. Verified end to end:
  packed, restored from a local feed into a fresh Web app, published with `.js`, `.br` and `.gz`.
  The MSBuild packages (18.9.6, compile-only) were approved by the user. The task targets
  net10.0 only, so it does not load in Visual Studio's .NET Framework MSBuild (user's call).

- **Mutation sweep (2026-10-15):** every test row checked by breaking the code. About 305
  single-line mutations across lexer, parser, writer, CLI, glob, MSBuild task, targets and props;
  each one rebuilt, ran the affected classes, and was reverted with `git checkout --`. 303 of 305
  test rows fail under at least one mutation. The two that cannot are equivalent mutants: the
  `./a.js` specifier row (any extension mutation writes `j` over `j`) and
  `FromOffset_rejects_offsets_outside_the_source(4)` (the slice throws the same exception as the
  guard). Gaps found and closed: 16 new tests or rows (class `extends B<T>`, type predicates,
  `catch (e: T)`, `export as namespace`, `import type x = require`, template continuations,
  `EditWriter` replace inside an erased range, CLI exact usage messages and stdin rejections, file
  mode outdir base, BOM, `.d.ts` arguments, all-or-nothing input check, glob rows, `.mts` in the
  MSBuild build). Harness: `tmp/mutate/` (gitignored, not kept). Tests: 305 unit, 2 conformance.

- **Fable review (2026-10-15):** independent review of all of `dev`. Three defects, all fixed
  with tests and break-checked: (1) `y as T` followed by a line starting `(`, `[` or a template
  was blanked to `y`, which JS reads as a call, index or tagged template; the erase now ends in
  `;` there (TypeScript ends the type at the line break, `parsePostfixTypeOrHigher`). (2) `pare`
  crashed with exit 134 on an unreadable directory; it now reports it and exits 2. (3) A
  non-ambient namespace holding only `declare` values (`namespace N { declare let x: T }`) was
  erased; TypeScript instantiates it (`getModuleInstanceStateWorker` ignores `declare`), Node
  rejects it, Pare now reports PARE101. Tests: 319 unit, 2 conformance.

- **v0.1 defaults (2026-10-15):** decisions from a fable consult, accepted by the user. The
  MSBuild package now removes `@(TypeScriptSource)` and `wwwroot/**/*.d.ts` from `Content` in
  `_PareRegisterOutputs` (target time, so import order against the Web SDK does not matter);
  `PareKeepSources=true` keeps them. Checked with a real `dotnet pack` + local feed +
  `dotnet publish`: published `wwwroot` holds only `.js` (+ `.br`/`.gz`), and the output runs in
  Node. Overwriting a hand-written sibling `.js` and `.cts` outside the default glob are documented,
  not changed. `Reject`'s ambient check removed; the other mutation-redundant guards kept (reasons
  in `work/todo.md` under Dropped). Tests: 321 unit, 2 conformance.

- **M6 sample (2026-10-15):** `samples/WebApp` (ASP.NET Core NativeAOT; Alpine 3.14.8 and htmx
  2.0.4 from jsDelivr, pinned, htmx with SRI; typed modules `app.ts`, `counter.ts`, `format.ts`,
  a minimal `alpine.d.ts`). It is outside `Pare.slnx` and stops the repo's `Directory.*.props`,
  so it builds like a consumer: `PackageReference Extrode.Pare.Build 0.1.0-*` from `tmp/feed`
  (`scripts/pack-local.sh` packs `0.1.0-dev.<unix time>`, so no stale NuGet cache). Checked by
  hand: `dotnet run` serves the page, the three modules and the htmx fragment; `counter.js` runs in
  Node (`0 clicks`, `1 click`, `2 clicks`); NativeAOT publish is 10 MB with no warnings, publishes
  only `.js` under `wwwroot`, and `/js/app.ts` is 404. Test
  `Sample_app_builds_serves_and_runs_in_a_browser` packs the package (`--no-build`), copies
  the sample to a temp dir, builds it with a private package folder, runs it and fetches `/` and
  `/js/app.js` with Accept-Encoding.
- **Sample blank page (2026-10-15):** in a browser the first sample showed nothing. Two causes:
  `dotnet run` used port 5000, which macOS AirPlay Receiver also holds (now 5180 via
  `Properties/launchSettings.json`); and `CreateSlimBuilder` skips `UseStaticWebAssets`, so an
  unpublished run answered every Accept-Encoding request with 200 and an empty body (now called in
  `Program.cs`). The earlier checks used `curl` without Accept-Encoding and missed it. Browser
  check, headless Chromium from the Playwright cache driven over CDP by an ad-hoc script in `tmp/`
  (not in the toolchain): Alpine counter `0 clicks`, `1 click`, `2 clicks`, htmx fragment swapped,
  no console errors, under `dotnet run` (Development) and the NativeAOT publish. M6 done.
- **Sample browser test (2026-10-15):** Microsoft.Playwright 1.63.0 (approved by the user) in
  `Extrode.Pare.Integration`. `Sample_app_builds_serves_and_runs_in_a_browser` now also installs
  chromium-headless-shell (first run downloads it to `~/Library/Caches/ms-playwright`), clicks the
  Alpine counter to `2 clicks`, clicks the htmx button, and fails on any console error. Needs
  network (jsDelivr). Break-checks: removing `Alpine.start()` and pointing `hx-get` at a missing
  route both fail it.
- **Test split (2026-10-15):** the MSBuild/sample tests moved to `tests/Extrode.Pare.Integration`
  (12 tests, ~35 s). `Extrode.Pare.Tests` is 312 tests in under 1 s. `dotnet test --solution`
  runs all three projects. `$(PareTsconfig)` is now kept out of build and publish output
  (`Tsconfig_is_not_published`, 2 rows); the sample no longer does it by hand.

- **Dev loop (2026-10-15):** `dotnet watch` (SDK 10.0.401) on the sample, measured on a copy.
  Hot reload mode (default): a `.ts` save logs "No managed code changes to apply" and nothing
  builds; a `wwwroot` file is a static asset to it, matched by the `staticwebassets` dev manifest,
  even with a `Watch` item. `--no-hot-reload` alone: "Static asset changes applied", no build. With
  `<Watch Include="@(TypeScriptSource)" />` in the package targets, `--no-hot-reload` rebuilds and
  restarts: `.js` rewritten within 1 s of the save, app back in ~3 s, headless Chromium showed the
  edited label. A `.js` written by another process is served fresh at once under plain, gzip and
  br, so M8 (re-strip in-process in Development) would work with hot reload. Tests:
  `Sources_are_watch_items` (`msbuild -getItem:Watch`) and
  `Dotnet_watch_without_hot_reload_restrips_an_edited_source` (real `dotnet watch`, ~5 s).

- **CLI distribution (2026-10-15, user's call: both):** `Extrode.Pare.Cli` is a .NET tool with
  `ToolPackageRuntimeIdentifiers` (SDK 10): a pointer package, one NativeAOT package per RID
  (osx, linux, linux-musl; arm64 and x64) and an `any` framework-dependent fallback.
  `scripts/pack-cli.sh` (~4 min) packs them plus `pare-<v>-<rid>.tar.gz`: macOS natively, Linux in
  Docker. glibc x64 builds under amd64 emulation; musl x64 is cross-compiled on arm64 Alpine against
  an x64 sysroot (`-p:SysRoot -p:LinkerFlavor=lld`, needs `llvm` for `llvm-objcopy`) because ilc
  aborts (134) on emulated Alpine. Each Linux build is run 20 times and tool-installed on its
  platform. A listed RID whose package is missing fails the install (no fallback to `any`), so
  win-* must be packed on Windows before a release. Found on the way: on linux-musl-arm64,
  `TryEnsureSufficientExecutionStack` on the main thread is false in ~1 process in 4 (17/60),
  so `pare` rejected everything; the CLI now runs on an 8 MB thread (0/200 failures on every
  build). Test: `Cli_tool_installs_the_native_binary_for_this_platform` (~11 s).

- **M7 perf (2026-10-15):** `tests/Extrode.Pare.Bench` (BenchmarkDotNet 0.15.8, approved).
  `scripts/get-bench-corpus.cs` sparse-clones TypeScript 5.9.3 `src/compiler`, Vue, Hono, Deno
  std, Svelte and Zod at pinned commits (2,504 files, ~25 MB; 2,392 strip). BenchmarkDotNet:
  checker.ts 13.3 ms (235 MB/s), corpus 75 ms (218 MB/s), `(a = ` x1000 38 ms, 0 B allocated on
  all three; `AllocationTests` asserts 0 B per call. `compare` mode runs Node 24
  `stripTypeScriptTypes`, oxc-transform, ts-blank-space, esbuild (pinned in
  `compare/package.json`) and `deno bundle` per extension, and compares bytes, then Acornima trees
  (`AstDump`, shared with the differential test; printers skip `Raw`, shorthand and `void 0`).
  Node and oxc: 0 differences on 2,388 files. ts-blank-space: 1 (its bug). esbuild 416 and Deno
  1,362: renamed bindings, strings to templates, regrouped exports, all printer rewrites. 4 files
  need type information (a type imported or re-exported without `type`); every stripper keeps
  them. Pare in-process 184-229 MB/s vs oxc 81, Node 38, ts-blank-space 25, esbuild 10. Pare
  bugs found and fixed: `declare` before a non-declaration keyword (`declare namespace = x`) was
  erased; a `get`/`set`/`static` field with an erased type merged with the next member.
- **Real-world check (2026-10-15):** `scripts/run-std-tests.cs` copies std (symlinks kept),
  strips it with `tmp/aot/pare` (~0.5 s; 5 files rejected: parameter properties, `<T>x`, two
  enums), maps `@std/*` from each member's `deno.json` exports to `.js`, and runs `deno test -A
  --no-check` per package on `*_test.ts` and `*_test.js`. 38/42 packages identical (4,997 tests
  pass on both, 5,521 on the .js in all); csv (`Function.prototype.toString`), testing (snapshot file named after the test
  file), http (`serveDir` refuses the `.corpus` dot path for the TS run) and expect (rejected
  files) explained in the script. Exit 1 on an unexplained difference.
- **Deno transpiler comparison (2026-10-15):** `compare` now reads Deno's own swc emit
  (`deno cache --no-check --sloppy-imports` writes `$DENO_DIR/gen/file/<path>.js` without running
  anything) instead of `deno bundle` (esbuild). An import map stubs every specifier that is not a
  JS/TS file on disk; unresolved ones Deno reports are added and the run repeats. Result: 2,337 of
  2,388 same tree, 51 same once imports and numeric name suffixes are ignored (`AstDump` `loose`:
  Deno drops unused imports, even `import {} from "m"`, and renames shadowed bindings), 0 differ.
- **Sample app tests (2026-10-15):** user asked for thorough app testing, Deno as well as Node.
  `Extrode.Pare.Integration` now: Development build in Chromium, Firefox 155 and WebKit 26.6 (one
  theory row each); Release publish (no `.ts`/`.d.ts`/`tsconfig.json`, 404 for them, brotli
  `text/javascript`) in all three; NativeAOT publish (`-r <current rid>`, ILCompiler from the
  NuGet cache as a fallback folder or nuget.org, no IL warnings, no `WebApp.dll`) in all three;
  stripped `counter.js` run in Node and Deno. Each browser run fails on console errors, failed
  requests or 4xx/5xx, and imports `format.js`/`counter.js` in the page to check Pare's output
  and positions. Break-checks: no `.ts`-to-`.js` rewrite fails all 6 browser/runtime rows; keeping
  sources in Content fails the publish test.
- **M8 hot reload (2026-10-15, user's call: package):** `src/Extrode.Pare.AspNetCore`,
  `builder.AddPareDevelopment()`. In Development a hosted service strips stale `wwwroot` sources at
  start and re-strips on save (FileSystemWatcher, 50 ms settle, write only on change, rejections
  logged and old `.js` kept). Found: `CreateSlimBuilder` runs no hosting startups, so `dotnet
  watch` never injected its refresh script into the sample; the package applies
  `Microsoft.AspNetCore.Watch.BrowserRefresh.HostingStartup` by reflection when that assembly is
  loaded (injected once even with `CreateBuilder`; no IL warning in the NativeAOT publish).
  `PublishAot` projects need `StartupHookSupport` in Debug for hot reload. Two SDK 10.0.401 traps:
  `dotnet watch --project` from another directory fails to read the static web assets manifest
  (`obj\Debug/...` path) and never refreshes; the refresh script connects 500 ms after load, so an
  edit before that is lost. Tests: 7 in `DevelopmentWatcherTests`, and
  `Dotnet_watch_hot_reload_refreshes_the_browser_with_the_restripped_source` (dotnet watch +
  Chromium, ~15 s; break-checked without the refresh hook and without the sample's call). Core
  now packs as `Extrode.Pare`. 375 tests pass.
- **Windows (2026-10-15, wp1 over ssh):** first run on Windows (10.0.26200 x64, SDK 10.0.401,
  repo copied by `git bundle`). Unit tests 348 pass (1 Unix-permissions skip). Integration
  without the 4 browser tests: 15 run, 2 skip (fake `tsc` is a shell script); 3 failed on test
  assumptions, now fixed: the tool shim is `pare.cmd`, `msbuild -getItem` JSON escapes
  backslashes, and a stray `%TEMP%\package.json` with `"type": "commonjs"` made Node load the
  sample's `.js` as CommonJS. `scripts/pack-cli.ps1` packs win-x64 (NativeAOT, 1.35 MB `pare.exe`,
  ~8 s), zips it, runs it 20 times byte-exact and tool-installs it (break-checked with a wrong
  expected output). win-arm64 needs VS's ARM64 C++ tools (not installed on wp1).
  Later the same day: installed VS's ARM64 C++ tools on wp1 (user approved); `pack-cli.ps1` packs
  win-x64 and win-arm64 in 15 s, arm64 `pare.exe` 1.37 MB, PE machine 0xAA64, not run (wp1 is
  x64). Playwright 1.63 browsers installed on wp1; all 24 integration tests run there (2 skip:
  fake `tsc`). The hot reload test needed two test fixes: `\` in the expected log line, and a
  delete retry because Windows keeps the killed app's `.exe` locked briefly.
  win-arm64 run on Windows 11 Pro ARM64 (build 26300) in a QEMU 11.1.2 VM on the M1 Max (`hvf`,
  unattended install, SSH on 127.0.0.1:2222; setup in gitignored `tmp/winvm`): `pack-cli.ps1
  -SkipPack` verified win-arm64 (20 byte-exact runs, tool install picks win-arm64), unit tests
  348 (1 Unix skip) on SDK 10.0.402 win-arm64. UTM 4.7.5 hangs or crashes on macOS 26.6.2, so
  plain QEMU replaced it (user approved).
- **obj/ output (2026-10-15, plan `docs/plans/2026-10-15-001-obj-output.md`):** in a project with
  static web assets and no `PareOutDir`, wwwroot outputs go to `$(IntermediateOutputPath)pare/`
  and are registered in the SDK's inputs stage like scoped CSS (`DefineStaticWebAssets`
  `SourceType="Computed"`), so they are compressed, fingerprinted and published. No
  `OriginalItemSpec` on the candidates: the SDK's compression reads that file, and pointing it at
  the `.ts` published `.br`/`.gz` of the source (found by the publish browser test; the published
  test now decompresses every copy). A sibling `.js` gets PARE200 and leaves Content (else the SDK
  fails with "Conflicting assets"). Stale outputs: the SDK's incremental clean already drops what
  an earlier build wrote; `_PareDeleteStaleOutputs` also drops what the watcher wrote. The build
  writes `bin/.../Extrode.Pare.development.json` (`sourceRoot`, `outputRoot`; not when
  publishing); `AddPareDevelopment` reads it and writes there, deletes a deleted source's output,
  and logs that a new source is served after the next build. `dotnet watch` hot reload refreshes
  the browser from the obj file (integration test). Deviation from the plan: the map holds the two
  roots, not every source/output pair, and `PareDevelopmentOptions.OutDir` stays (not obsolete)
  for `PareOutDir` projects. Tests: 353 unit, 28 integration.

- **0.2.0 release (2026-10-15):** version 0.2.0 (`chore/release-0.2.0`), tag `v0.2.0`, private
  repo `extrode/pare` (user's call: private now, public once CI is green; nuget.org later).
  Integration tests now pack with the repo's version (`TestVersion`), since the sample references
  `<version>-*`. First CI run: the Release-configured sample test looked for `bin/Release` though
  the sample builds Debug (fixed), and Ubuntu lacked WebKit/Firefox libraries (`playwright.ps1
  install-deps` step). CI also sets up Deno 2.x, fetches tsc and uses checkout v7, setup-dotnet v6.
  CI green on ubuntu, macos and windows: 375 tests, 0 skipped on Linux/macOS, 3 on Windows (Unix
  permissions, fake `tsc`). GitHub release `v0.2.0` carries 21 assets: per-RID tool packages and
  archives for osx, linux, linux-musl, win (arm64, x64), the pointer and `any` packages, and the
  `Extrode.Pare`, `.Build`, `.AspNetCore` packages.

- **Visual Studio MSBuild (2026-10-15, plan `docs/plans/2026-10-15-002-vs-msbuild.md`):** two
  `UsingTask`s: in process under `dotnet build` (`MSBuildRuntimeType` Core), `Runtime="NET"
  TaskFactory="TaskHostFactory"` otherwise. Before it, VS 18.10's `MSBuild.exe` worked through its
  own out-of-process fallback and warned on every build. The .NET 10 SDK needs MSBuild 18, so VS
  2022 cannot build net10.0 at all. `Visual_Studio_MSBuild_strips_with_the_dotnet_task_host` runs
  `MSBuild.exe` (vswhere, major version 18 or later; skips otherwise, e.g. macOS, Linux): no
  fallback warning, `obj` output registered as a static web asset, PARE100 located in `b.ts`.
  Passes on wp1 (VS 18 Community); break-checked there by dropping `Runtime="NET"` and by dropping
  the `Core` condition (both bring back the fallback warning). CI's `windows-latest` is Windows
  2025 with VS Enterprise 2026 18.10, so it runs there too. Not checked: the VS IDE itself.

- **0.3.0 cut (2026-10-15):** version 0.3.0 (`chore/release-0.3.0`), CHANGELOG has an "Upgrading
  from 0.2" section (obj/ default, delete old `wwwroot` `.js`, PARE200, `.gitignore`, new files
  under `dotnet watch`). 406 tests, 0 failed, 1 skipped on macOS (VS MSBuild test). Packed: 8
  per-RID tool packages and archives (osx, linux, linux-musl via Docker on the Mac; win-x64 and
  win-arm64 on wp1, win-x64 verified there), pointer and `any` packages in `tmp/dist`,
  `tmp/dist-win`; `Extrode.Pare`, `.Build`, `.AspNetCore` 0.3.0 in `tmp/dist-lib`. Release notes
  in `tmp/release-notes-0.3.0.md`. Tag `v0.3.0` local; the user pushes, then the GitHub release.

- **After 0.3.0 (2026-10-15):** CLI quickstart `docs/cli-quickstart.md` with sample
  `samples/CliSite` (static site, browser test in Chromium/Firefox/WebKit, Node and Deno run;
  every doc command run against the 0.3.0 archive). Decision memos
  `docs/decisions/2026-10-15-002-ts-outside-wwwroot.md` and `-003-razor-class-libraries.md`
  (both measured). Razor class libraries already worked in 0.3.0; now tested and in README.
  `PareRelativePath` fixes: PARE201 for two sources mapped to one URL (was SDK crash MSB4018),
  backslashes normalized (Windows-only bug, verified on wp1). 416 tests, 0 failed, 1 skipped.
  `PareServeAt` (public, folder-only URL mapping; name from the Fable consult), PARE202 warning for
  unserved sources outside wwwroot, PARE203 for a value that is not a folder. 442 tests, 0 failed,
  1 skipped on macOS; new tests green on wp1; break-checked (4 behaviors, 7 failures).

- **Dev map and fixes (2026-10-15):** `AddPareDevelopment` re-strips `PareServeAt` and Razor
  class library sources on save: the development map lists every mapped source and output, and
  each project's map is copied to referencing projects' output as
  `Extrode.Pare.development.<Project>.json` (0.3.0 maps still read). Checked under real
  `dotnet watch`. A new `.ts` outside `wwwroot` waits for the next build (todo). Fixed BLAZOR106
  with `PareOutDir` and `.razor.ts`/`.cshtml.ts` (out-dir copies removed from Content and None).
  `PareServeAt` serving tests share one built project (13 builds and 2 publishes down to 2 and 1).
  Merged dev 9736908: macOS 384 unit, 63 integration (1 skip), 3 conformance, 0 failed; wp1 384
  unit (1 skip), 63 integration (2 skip), 0 failed.

- **Rein handoff, 0.4.0 (2026-10-17, plan `docs/plans/2026-10-17-003-embedded-output.md`):**
  handoff `docs/handoffs/2026-10-17-rein-web-ui-needs.md`, reply `-pare-0.4.0-for-rein.md`. User's
  calls: both N1 shapes, 0.4.0 as vendored `.nupkg`, string overload + 0.x stability, N4 and N5.
  `PareEmbed`/`PareEmbedPrefix` embed outputs (written under `obj/.../pare/`, named by path below
  the project, `WithCulture=false` so `x.de.js` stays in the main assembly). Public `@(PareOutput)`
  (`PareSource`, `PareRelativePath`); `PareStripTypeScript` is now a bodiless target depending on
  `PareTypeCheck;_PareStrip;_PareRegisterOutputs`, because a project's own `AfterTargets` target is
  declared before the package's and would run first (break-check: 5 tests fail without it).
  A Pare-side stale delete for embed was redundant (SDK incremental clean), dropped. Found:
  `$(IntermediateOutputPath)` is empty in a project body (`PareOutDir` from it gives `pare/`);
  README recommends `$(BaseIntermediateOutputPath)`. `TypeStripper.Strip(string, out ...)`.
  `get-tsc.cs --dest` (refuses a non-empty dir it did not fill); a copied script inside a project
  dir gets compiled by it (documented). Jint 4.16.4 (approved) sample `JintModuleTests`: Jint's
  `ModuleLoader` base swallows load exceptions, so the sample implements `IModuleLoader`.
  463 tests, 0 failed, 1 skipped (VS MSBuild) on macOS; not run on wp1. Packed `Extrode.Pare`,
  `.Build`, `.AspNetCore` 0.4.0 into `tmp/dist-lib`; checked from a vendored `packages/` feed in a
  fresh console app (resources `web/home.js`, `web/lib/util.js`).
- **0.4.0 released (2026-10-17):** tag `v0.4.0` on main 30ddbb0, GitHub release with 21 assets
  (8 CLI archives: osx, linux, linux-musl via Docker on the Mac, win-x64 and win-arm64 on wp1 from
  a bundle; tool packages; the 3 library packages). `docs/cli-quickstart.md` at 0.4.0: every
  command re-run against the release downloads; sample output byte-identical to 0.3.0's.
  wp1 at v0.4.0 (SDK 10.0.402): 463 tests, 0 failed, 6 skipped (3 corpus missing, Unix
  permissions, 2 fake-`tsc` shell scripts); the embed, `get-tsc --dest`, Jint and VS MSBuild
  tests ran there.
- **Perf pass (2026-10-17, `perf/hot-path`):** BenchmarkDotNet in-process (out-of-process
  fails while `.claude/worktrees` hold copies of the bench project, see todo): `checker.ts` 13.2
  -> 9.4 ms, real-world corpus 74.1 -> 59.8 ms, 0 B allocated. Regression guard: a snapshot
  (ok, output hash, diagnostics) of all 15,462 `.corpus` files, plain, with the import rewrite and
  cut at 1/3 and 2/3 (61,848 results), identical after every change. Kept: ASCII identifier
  fast path (-19%), trivia on a local position (-4% corpus, -10% checker), stack check every
  16th recursion (-6% under NativeAOT only; first `Guard` test), CLI `OptimizationPreference=Speed`
  (-7% AOT), CLI walk reads each directory once (0.60 -> 0.35 s over the corpus; svelte has
  3,816 test directories). Tried and reverted, no gain: vectorized identifier scan
  (`IndexOfAnyExcept`, slower: identifiers are short), keyword length/first-letter prefilter,
  local span in the identifier loop. Measured: 1.21 lexer scans per token (Peek and speculation),
  so a token cache would gain little; NativeAOT ~18% slower than the JIT on the same driver.
  `(a = ` nesting stays quadratic (tsc and ts-blank-space too, both slower than Pare at 500 deep;
  only swc is linear, by cover grammar): still Dropped in todo.
- **libpare (2026-10-17, `feat/native-lib`):** `src/Extrode.Pare.Native`, NativeAOT
  `NativeLib=Shared`, C API in `include/pare.h` ([plan](../docs/plans/2026-10-17-004-native-lib.md),
  reviewed by Fable before code). osx-arm64 `libpare.dylib` 1.0 MB. UTF-8 in and out, BOM dropped
  like the CLI, diagnostics located in one forward pass. 16 unit tests (`NativeStripTests`), 6
  integration tests (publish, P/Invoke, and `native/smoke.c` built with `cc`: main thread, a
  512 KB pthread at 200,000-deep nesting, 4 threads x 1000 calls). Break-checked: byte offsets,
  BOM, options, UTF-8 strictness, CRLF, `pare_free` zeroing, code range, size limit.
- **0.5.0 cut (2026-10-17):** tag `v0.5.0` on main 0abc404 (local; push and GitHub release are
  the user's). 29 assets in `tmp/dist` and `tmp/dist-lib` (8 CLI + 8 libpare archives, 10 tool
  packages, 3 library packages), notes in `tmp/release-notes-0.5.0.md`. macOS: 495 tests, 494
  passed, 1 skipped. wp1 (SDK 10.0.402): 495, 487 passed, 8 skipped (the 6 known, the C test, the
  symlinked-directory test). Benchmark re-run that day discarded: load average 8.7 from other
  work (whisper-cli batch, kiln tests), BenchmarkDotNet StdDev ~9% of the mean; re-run on a quiet
  machine before quoting new numbers. Re-run later that day, StdDev 1-2%: `checker.ts` 9.3 ms
  (335 MB/s), corpus 60.2 ms (272 MB/s), nested 32.3 ms; compare Pare 279 MB/s, oxc 3.2x,
  Node 7.0x slower; output counts unchanged. README Performance updated (it said Pare 0.1.0).
  Published as a GitHub prerelease with all 29 assets; quickstart bumped to 0.5.0.
- **Svelte tier 1 spike (2026-10-17):** ClearScript 7.5.1.1 approved for the spike. Svelte
  5.57.2's compiler in V8: byte-identical to Node on 4,587 `.svelte` files, 1.6x slower warm
  (0.4 ms a component), loads in a `dotnet build` task and under VS `MSBuild.exe`'s .NET task
  host on wp1, output runs in Chromium behind an import map. No linux-musl native package; 44 MB
  per RID. Results in `docs/ecosystem-comparison.md`; scratch in `tmp/svelte-spike/`,
  the Svelte spike cleanup script (in git history). Go: Svelte support is a separate product, Husk (`../husk`,
  created the same day with the spike as `prototypes/001-engine-spike`, the ClearScript vs Jint
  comparison and the Cloudflare/Deno survey); decision 004. Pare's part: a public MSBuild
  extension point (`work/todo.md`).
- **0.6.0 released (2026-10-17):** the compile extension point (decision 005), tag `v0.6.0` on
  main b46ece4, GitHub prerelease. 529 tests on macOS, 528 passed, 1 skipped (VS MSBuild); not
  run on wp1. 29 assets. First published with 17: `pack-cli.sh` skips its Docker step without a
  word when Docker is not running; the 12 linux and linux-musl assets were packed afterwards from
  a worktree at `v0.6.0`, each verified in its container, and uploaded. Quickstart at
  0.6.0: every command re-run against the osx-arm64 archive and the tool packages; the sample
  page checked in Chromium and Firefox.
- **0.6.1 released (2026-10-30):** tag `v0.6.1`, the PARE200 same-name fix. First release through
  `.github/workflows/release.yml` (NuGet trusted publishing, `packages` environment with a
  required reviewer, `vars.NUGET_USER`). `Extrode.Pare`, `.Build` and `.AspNetCore` are on
  nuget.org at 0.6.1; the repo is public. The first push got 403 on `Extrode.Pare` (the policy
  glob `Extrode.Pare.*` needs the dot); fixed on nuget.org, job re-run. The workflow does not
  publish `Extrode.Pare.Cli` or the CLI and libpare archives: those are packed with
  `scripts/pack-cli.sh` (needs Docker) and uploaded by hand, as for 0.6.0. Not done at 0.6.1.
  Docs rewritten as a site (`docs/0*-*`, `scripts/build-docs.sh`), README rewritten, SECURITY.md
  and CONTRIBUTING.md added. Husk handoff answered in `../husk`.
- **Audit (2026-10-30):** 529 tests pass on macOS (1 skip: Visual Studio MSBuild). The published
  0.6.1 packages restore from nuget.org into a scratch web app: build, publish (`.js`, `.br`,
  `.gz`, no `.ts`) and serve all work. Found and fixed: (1) the 0.6.0 macOS archives, and any
  built with Homebrew's dotnet, load five Homebrew libraries (openssl@3, brotli) by absolute
  path and stop at launch on a Mac without them; `scripts/check-macos-libs.sh` now stops
  `pack-cli.sh` on that, and the 0.6.1 macOS archives were rebuilt with Microsoft's SDK
  (system libraries only); (2) the first GitHub CI runs failed on Windows: 7 PARE204 tests and
  `Compiled_sources_are_served...` expected backslashes the task does not emit (test fix, passes
  8/8 on wp1), and `AllocationTests` failed once in three runs with 808 bytes (now retried up to
  five times; break-checked with an injected allocation); (3) the README showed
  `AddPareDevelopment()` without its `using`. 0.6.1 archives built: osx-arm64, osx-x64,
  win-x64, win-arm64 (tmp/dist). Linux archives need Docker (not running).
- **0.6.2 prepared (2026-10-30):** version bump only; the library and build task are unchanged
  from 0.6.1. It carries the icon (P-shaped peeler, teal, `docs/assets/logo/`) on the four NuGet
  packages, the README logo, the ISL-CLA files and `pages.yml`. Tag `v0.6.2` and the push to `main`
  are the user's. Needs `pack-cli.sh` (Docker) for the archives, as for 0.6.1.
- **0.6.2 released (2026-10-30):** tag `v0.6.2` on main 5558167; `Extrode.Pare`, `.Build` and
  `.AspNetCore` 0.6.2 on nuget.org; 19 release assets (3 nupkgs, 12 macOS/Linux archives, 4 Windows
  zips built on wp1 from a bundle of the tag). `pack-cli.sh` now copies `docs/assets/logo` into
  its Docker build (the icon made `Directory.Build.props` reference it). The Windows checkout is
  `C:\home\code\extrode.com\pare` on wp1. CI on Windows had a jsDelivr page-load timeout; the
  integration tests now serve the sample's CDN imports from `tests/Extrode.Pare.Integration/fixtures/cdn`.
- **CLI on nuget.org (2026-10-30, unreleased):** user's call after an Opus second opinion
  (tmp/cli-nuget-opinion.md, not kept). `release.yml` packs `Extrode.Pare.Cli` with
  `-p:PareCliFrameworkDependent=true`: one framework-dependent tool, no RID packages, needs the .NET
  10 runtime. The NativeAOT CLI and libpare stay archives. `CliToolPackageTests` packs it, installs
  it to a temp tool path and runs it; break-checked (csproj condition, workflow line). The README
  and getting-started pages still say the package is not on nuget.org; change them when the first
  release carrying this is tagged (see `work/todo.md`). Not tested: `dotnet tool install` from
  nuget.org itself, and the `extrode` org as package owner (user confirmed the org exists).
- **0.6.3 prepared (2026-10-31):** first release carrying the framework-dependent `Extrode.Pare.Cli`
  on nuget.org. Version bump, README, getting-started and CLI guide no longer say the package is
  missing (test `Readmes_do_not_say_the_cli_package_is_missing_from_nuget`, break-checked),
  `dist/docs-site` rebuilt. Library and build task unchanged. All tests pass on macOS (1 skip: VS
  MSBuild). Tag `v0.6.3`, the push and the archives (`pack-cli.sh`, Docker; wp1 for Windows) are
  the user's.
- **0.6.3 released (2026-10-31):** tag `v0.6.3` on main 0c8ee8c; the three libraries and the
  framework-dependent `Extrode.Pare.Cli` 0.6.3 are on nuget.org and in GitHub Packages
  (`release.yml` gained the GitHub Packages push). `dotnet tool install Extrode.Pare.Cli --version
  0.6.3` from nuget.org installs and strips a file. 16 archives in `tmp/assets-0.6.3` (12 mac/Linux
  from `pack-cli.sh` with Microsoft's SDK 10.0.403 in `tmp/msdotnet`, since Homebrew's dotnet trips
  `check-macos-libs.sh`; 4 Windows zips from wp1, which was fast-forwarded to dev). Upload with
  `gh release upload v0.6.3 tmp/assets-0.6.3/*` is the user's. Windows tests not re-run.

- **0.7.0 prepared (2026-10-31):** two audits and an Opus decision pass after 0.6.3. Library: sticky
  `_tooDeep` so deep arrow defaults give PARE001 instead of a stack overflow. CLI: `--outdir` base is
  the input roots, empty `--outdir` is a usage error, symlink loops skipped. Build: PARE205. Watcher:
  moved directories, save during `Start`, one shared stop deadline. `release.yml` needs `ci` and runs for
  tag pushes only. Version bumped to 0.7.0 (minor: `--outdir` output moves, PARE205 fails builds that
  passed). Not done before tagging: Bench run on the hot-path change, full Conformance, wp1 Windows
  tests (glob, drive, UNC), a compare of the pinned action SHAs with their tags, `pack-cli.sh` and
  wp1 archives, `gh release upload`. Tag, push and `main` are the user's.
- **Audit and CI docs (2026-10-31):** 565 tests on macOS, 564 passed, 1 skipped (VS MSBuild), 0
  failed. Error codes in `docs/06-reference/error-codes.md` cover every code in `src`. New guide
  `docs/03-guides/ci-and-hooks.md` (GitHub Actions, git and pre-commit hook, Docker); the hook and
  the `--outdir` layout were run, the workflow and Dockerfile were not. Benchmark rows re-measured
  on 0.7.0 in 0.7.1.
- **0.7.1 prepared (2026-10-31):** the todo sweep. Build: the development map is written by a JSON
  task, PARE203/204/205 report every item in one build, a source declared twice is one source.
  `AddPareDevelopment` warns on an invalid or wrong-shape map. `release.yml` builds the NativeAOT
  CLI and libpare archives on native runners and attaches them with `SHA256SUMS`; `pack-cli.sh`
  takes `--rid` and stops when Docker is down. Benchmarks re-measured on 0.7.0 (Mac and wp1). Full
  suite green on the Mac and wp1 (the one Windows failure was a test path separator, fixed). One
  cleanup script, `scripts/cleanup/pare.sh`. Patch version: no API or output change. The musl stack
  bug stays unreported (decided).
- **0.7.1 released (2026-10-31):** tag `v0.7.1` on main 7c11461; the four packages on nuget.org
  and GitHub Packages; the release has 16 archives and `SHA256SUMS` built by `release.yml`. CI gained
  a `windows-11-arm` (win-arm64) job, non-blocking (`experimental`) until it has passed a few times;
  its first run passed 555 of 563 (8 skipped, as on win-x64). Watcher tests wait for the log line
  instead of racing it.
- **After 0.7.1 (2026-10-31, unreleased):** `CLAUDE.md` removed (`AGENTS.md` is read directly).
  `Extrode.Pare.Build` ships `buildTransitive/` for Husk (handoff
  `docs/handoffs/2026-10-31-husk-pare-0.7.1-and-build-transitive.md`). The `DistinctSources` task
  replaces `RemoveDuplicates`: the last declaration wins, and on Windows both separators name one
  source (both confirmed on wp1, tests run there). Test dependencies bumped (TypeScript stays 6.0.3
  for ts-blank-space). Checked: `PareCompileSource` outside `wwwroot` does not restart the app under
  hot reload; dotnet/sdk#54309 still fails on 10.0.403. Not checked: the `\\?\` glob on wp1 (ssh
  refuses device paths).

## Next

Order from the 2026-10-15 fable consult:

1. ~~Browser check of `samples/WebApp`~~: done 2026-10-15 (headless Chromium).
2. ~~**v0.1 release prep**~~: license done 2026-10-15 (user: pick from islamiclicense.org; ISL-P 1.2
   WITH ISL-OE 1.2, Licensor `Extrode (extrode.com)`), package metadata done. v0.1.0 tagged,
   0.2.0 released on GitHub, 0.3.0 cut (above).
3. ~~M5 tsgo differential and `scripts/get-tsc.cs`~~: done 2026-10-15, 99.96%, all triaged.
4. ~~**Dev loop:**~~ done 2026-10-15: `--no-hot-reload` via `Watch` items, default hot reload
   via M8 (see above).
5. ~~**M7 perf**~~: done 2026-10-15 (see above).
6. ~~M4 lexer validation~~: dropped 2026-10-31; the leniency is documented.

Not in the plan yet: nothing. (Error-code page `docs/error-codes.md`, stale `.js` and obj/ output: done.)

## Open questions

- Pushing `main` is the user's (a hook blocks it for agents).
- When to release the `buildTransitive` and `DistinctSources` changes (next version; Husk waits on it).

## Corpus pass rate

`StripCorpusTests` (2026-10-14, ~27 s debug): of 13,935 files with no TS1xxx baseline (jsx,
`.d.ts` and untyped `node_modules` skipped), 11,753 strip, 2,172 are rejected under section 6,
10 are named deviations, **0 syntax errors, 0 moved characters**. (2026-10-15: 11,737 strip,
2,188 rejected, after namespaces with bodiless functions became rejections.) All 1,048 plain `.js` files
come back byte-identical, apart from 2 named deviations that contain TS syntax. Every output is
parsed by Acornima. 229 fail, and each falls under either a named class of early error that
TypeScript reports from the checker (duplicate declarations, `super` misuse, private names
outside classes, ...) or an Acornima limit (decorators before `export`). The differential
check against tsgo is still M5.

## Deviations from the handoff

- Differential emit uses `module: preserve`, not handoff 10.4's `esnext`: `esnext` turns `.cts`
  into CommonJS and adds `export {}`; `preserve` keeps module syntax as written, like a stripper.
- tsgo: TypeScript 7 shipped stable (`typescript@7.0.2`, 2026-07-08). The Go compiler is now `tsc`
  in `@typescript/typescript-<os>-<cpu>` (`lib/tsc` plus the `lib/*.d.ts` beside it), not
  `@typescript/native-preview`. `scripts/get-tsc.cs` (the handoff named it `get-tsgo.cs`; renamed 2026-10-15) fetches 7.0.2
  into `.tools/typescript/`. The MSBuild property is `PareTscPath`, default `tsc` (renamed from
  the handoff's `PareTsgoPath`/`tsgo`, user's call 2026-10-15).

- Name: **Pare** (folder name) instead of TsBlank. Projects `Extrode.Pare.*`, CLI `pare`,
  static entry `TypeStripper.Strip` (a class named `Pare` would collide with the namespace).
- `PROGRESS.md` lives here as `work/progress.md`; the handoff is `docs/handoff.md`.
- Diagnostic codes print as `PARE001` (syntax), `PARE100`-`PARE107` (rejections).
- Corpus location: microsoft/TypeScript `main` is now the Go codebase (TypeScript 7). Test
  inputs are under `tsc/testdata/tests/cases/{compiler,conformance}`, error baselines under
  `tsc/testdata/baselines/reference/{compiler,conformance}`.
- Deliberate lexer deviation: U+0085 (NEL) is invalid, as in ECMAScript; TypeScript treats it
  as whitespace. Three corpus files hit this.
- Added rejection `ParameterDecorator`: parameter decorators only exist under legacy
  `experimentalDecorators`, so they are the detectable part of that handoff row.
