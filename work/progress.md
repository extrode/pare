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

## Next

Order from the 2026-10-15 fable consult:

1. ~~Browser check of `samples/WebApp`~~: done 2026-10-15 (headless Chromium).
2. **v0.1 release prep:** license done 2026-10-15 (user: pick from islamiclicense.org; ISL-P 1.2
   WITH ISL-OE 1.2, Licensor `Extrode (extrode.com)`), package metadata done. Left: tag 0.1.0
   (release on `main`: user's call).
3. ~~M5 tsgo differential and `scripts/get-tsc.cs`~~: done 2026-10-15, 99.96%, all triaged.
4. ~~**Dev loop:**~~ done 2026-10-15 for `dotnet watch --no-hot-reload` (see below). Default
   hot reload mode still needs M8 (user's call: `Watch` items now, M8 later).
5. **M7 perf**, with bench rows for the pathological nesting shapes.
6. **M4 lexer validation:** only if M5 shows a divergence that matters.

Not in the plan yet: stale `.js` left after deleting a `.ts`; output under `obj/` as static web
assets (v0.2, retires the publish and overwrite questions); a core `Extrode.Pare` library package
(needed by M8); CLI distribution (`PackAsTool` or binaries); a PARE1xx error-code page;
`.gitignore` guidance for generated `.js`; Windows has never run.

## Open questions

- CI has not run: the repo has no remote yet.
- Lexer leniency vs TypeScript (escape and numeric-literal validation): see `work/todo.md`.

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
