# Todo — pare

The unscheduled backlog: everything known to be worth doing that nobody has committed to a date.
Items enter here from anywhere (a review comment, an out-of-scope note, a bug found in passing)
and leave by being promoted into `status/next.md` or dropped with a reason.

Keep it honest rather than tidy. A backlog that only contains things someone intends to do is a
wish list; the value is in recording the ones nobody has got to yet.

## Now-ish

Small, clear, and could be picked up without a design decision.

- BenchmarkDotNet's out-of-process run fails ("Found more than one matching project file") while
  `.claude/worktrees/agent-*` hold copies of `Extrode.Pare.Bench.csproj`; run with `--inProcess`,
  or `scripts/cleanup/stale-agent-worktrees.sh -e` (removes the merged ones; all 14 gone 2026-10-17).

## Later

- .NET bug, not reported: NativeAOT on linux-musl-arm64,
  `RuntimeHelpers.TryEnsureSufficientExecutionStack()` on the main thread returns false in 24 of
  60 runs (SDK 10.0.402, Alpine 3.24.2; JIT and glibc arm64 always true). Library callers there
  need a thread they created; README says so. Issue draft and repro: `tmp/issues/musl-stack/`.

- .NET SDK bug, reported as dotnet/sdk#54309, fixed on `main` by dotnet/sdk#54536 (2026-07-20), no
  10.0.x backport found: `dotnet watch` 10.0.401 on macOS, started outside the project
  directory (`--project`), fails to read `obj\Debug/net10.0/staticwebassets.development.json`
  (`IntermediateOutputPath` keeps MSBuild's backslash) and sends no static-asset refresh. A plain
  `Microsoft.NET.Sdk.Web` project shows it too; from the project directory it works, saves via
  rename included. README says to run it from the project directory.

- A new `.ts` outside `wwwroot` that a `PareServeAt` glob would match is not stripped by
  `AddPareDevelopment` until the next build (the development map lists only built sources).
- `release.yml` publishes the four nupkgs (the CLI as one framework-dependent tool); the NativeAOT
  CLI and libpare archives are uploaded by hand (`scripts/pack-cli.sh`, `gh release upload`). The
  docs point at the archives; decide whether the workflow publishes them. Tool install with
  `--add-source`: docs/03-guides/offline-restore.md.
- PARE203 stops at the first bad `PareServeAt` (MSBuild ends a batched `Error` at its first
  batch), so its 9 test cases need 9 builds (~14 s). Reporting every bad item in one build would
  let them share one. The serving tests share one build since 2026-10-15 (subset 68 s to 42 s).
- In Development the SDK serves every `wwwroot` file through a `**` pattern, so `.ts` sources
  return 200 (app and RCL); publish has none. Decide whether `AddPareDevelopment` should 404 them.

Real, but needs a decision, a dependency, or more information first. Say which.

- Lexer leniency vs TypeScript (M4): no validation of escapes inside strings, templates and
  regex (TS1125, TS1198, TS1199), numeric separator placement (TS6188, TS6189), leading-zero
  decimals in strict code (TS1489), binary/octal digit ranges (TS1177, TS1178). Tokenization is
  unaffected; a browser rejects such input. Decide whether `Strip` should reject them. Run the
  corpus test with `PARE_CORPUS_VERBOSE=1` to list them (`MISSED` lines).
- Unicode tables: identifier classes come from .NET's `CharUnicodeInfo`, whose Unicode version
  may differ from TypeScript's and browsers'. Only matters for exotic identifiers.

- A `"` in a source or output path makes the development map invalid JSON: paths are written
  with only `\` escaped, and `ReadMap` does not catch `JsonException`, so the app fails to start in
  Development. (2026-10-17)
- `PareCompileSource` outside `wwwroot` is a `Watch` item: not checked whether `dotnet watch` in hot
  reload mode treats saving one as an unsupported change and restarts the app. (2026-10-17)
- PARE201 lists the same source twice when one `PareCompileSource` is declared twice (a package's
  `.props` plus the project file). (2026-10-17)
- `scripts/pack-cli.sh` skips the Linux RIDs without a message when `docker info` fails, and still
  ends "packed"; 0.6.0 went out with 17 of 29 assets because of it. Fail, or print what was
  skipped, unless `--no-docker`. (2026-10-17)

## Dropped

Not doing, with the reason. Kept so the same idea does not get re-raised every quarter.

- Removing `ThrowIfGreaterThan` (SourceLocation), the `.d.ts` Exclude (props) and EditWriter's
  replace-after-erase guard: each is redundant today but guards a boundary other code can cross
  (public API, user-supplied items, edit order). Kept. (2026-10-15)
- Fixing quadratic time on pathological nesting (`(a = (a = ...` 5000 deep: 6.7 s): same shape as
  TypeScript's parser, inputs are the user's own files, `Guard()` stops deep recursion (not for arrow defaults before 0.7.0, which overflowed; fixed by the sticky `_tooDeep` in Restore).
  M7 adds a bench row so a regression shows. (2026-10-15)
- Warning or refusing when a hand-written `foo.js` sits next to `foo.ts`: indistinguishable from
  a generated one without a marker, which would break the position guarantee. Documented
  instead; output under `obj/` (v0.2) would remove the problem. (2026-10-15)
- `.cts` in the MSBuild default items: CommonJS does not run in a browser `wwwroot`. Explicit
  items work. (2026-10-15)

- `AllocationTests` retries up to 5 times (added 2026-10-30 for a Windows CI flake): an allocation
  that happens on some calls but not others (a buffer grown every Nth call) could pass on a clean
  attempt. A per-call allocation still fails (break-checked). Review range fe99295..85df724.


- Audit 2026-10-31 (two opus passes; fixed in 0.7.0 unless listed here):
  - Closed, no change: `namespace N { export {}; }` gives PARE101, but TypeScript itself rejects an export declaration inside a namespace (TS1194), so there is no valid input to erase.
  - Dropped: the `Edit.cs` insertion sort. Measured at the deepest input the 8 MB guard allows (14000 nested generic arrows): 42000 edits, 294M moves, 0.27 s, against about 0.05 s for a flat file with as many edits; at 1000 levels 1.5M moves, under 10 ms. Far cheaper than the accepted parser quadratic above.
  - Windows, `\\?\C:\src\*.ts`: the `?` segment counts as a wildcard, so it matches nothing. Nobody types device paths into a CLI glob. Check on wp1 when Windows is next touched.
  - `\\srv\*\x.ts` (wildcard in the share) matches nothing and will not: no API lists a server's shares. Documented in cli.md.
  - One full unit run failed once (439 tests) and never again in 60 reruns of the suite and of the watcher tests. Suspects: `Compiled_sources_that_are_up_to_date...` and `Unchanged_text_on_save...`, which count compiler calls while a live watcher runs. Keep a results file (`-xml`) on full runs so the next failure has a name.
  - Not reproduced (the last also on wp1 under code page 437, 2026-10-31; the `--stdin` UTF-8 change was reverted): `--stdin` mangling non-ASCII on Windows; an upper-case `App.TS` in `TypeScriptSource` was said to be its own output; a build leaves it alone and writes nothing.
- Research 2026-10-31, decided: pre-commit hook and GitHub Action are `pare src --check-only`, so doc snippets only, no artifact. Deferred: opt-in identity source map (positions are kept already), node_modules policy switch (nobody asked). Dropped: npm wrapper (against the no-Node point), VS Code extension, WASM, a diagnostic for `import { T }` without `type` (no type information). Already in Pare: erasableSyntaxOnly parity, decorator policy, incremental target, static web asset registration.
