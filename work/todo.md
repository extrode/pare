# Todo — pare

The unscheduled backlog: everything known to be worth doing that nobody has committed to a date.
Items enter here from anywhere (a review comment, an out-of-scope note, a bug found in passing)
and leave by being promoted into `status/next.md` or dropped with a reason.

Keep it honest rather than tidy. A backlog that only contains things someone intends to do is a
wish list; the value is in recording the ones nobody has got to yet.

## Later

Real, but needs a decision, a dependency, or more information first. Say which.

- .NET bug, not reported and will not be (decided 2026-10-31; the README workaround covers users): NativeAOT on linux-musl-arm64,
  `RuntimeHelpers.TryEnsureSufficientExecutionStack()` on the main thread returns false in 24 of
  60 runs (SDK 10.0.402, Alpine 3.24.2; JIT and glibc arm64 always true). Library callers there
  need a thread they created; README says so. Issue draft and repro: `tmp/issues/musl-stack/`.
- .NET SDK bug, reported as dotnet/sdk#54309, fixed on `main` by dotnet/sdk#54536 (2026-07-20), no
  10.0.x backport found: `dotnet watch` 10.0.401 on macOS, started outside the project
  directory (`--project`), fails to read `obj\Debug/net10.0/staticwebassets.development.json`
  (`IntermediateOutputPath` keeps MSBuild's backslash) and sends no static-asset refresh. README
  says to run it from the project directory. Re-check on a newer 10.0.x SDK.
- `PareCompileSource` outside `wwwroot` is a `Watch` item: not checked whether `dotnet watch` in hot
  reload mode treats saving one as an unsupported change and restarts the app. (2026-10-17)
- Review 2026-10-31 (f102d65..00ceb0f), unverified: `RemoveDuplicates` keeps the first declaration's
  metadata (a package's `wwwroot/**/*.ts` glob beats the project's `PareServeAt="lib"` for the same
  file), and on Windows a wildcard item (`wwwroot\js\a.ts`) and an explicit one (`wwwroot/js/a.ts`)
  may not compare equal, so that duplicate would still reach the SDK.
- CI flakes (5 of 30 `ci` runs to 2026-10-31, a different test each time): `Saving_a_mapped_source_by_rename_strips_it_again`
  timed out on windows-latest (cause unknown; a timeout now prints the watcher log), Playwright
  30 s timeouts on Windows, the `NativeLibraryTests` fixture once on macOS,
  `Stopping_cancels_every_watcher...` timing on Windows. The macOS log-line race is fixed.
- `ci.yml` runs `win-arm64` on `windows-11-arm` (added 2026-10-31) with `continue-on-error`:
  Playwright's browsers run under x64 emulation there. Drop `experimental` once it has passed a
  few times, so a win-arm64 break blocks a release.
- Windows, `\\?\C:\src\*.ts` glob: check on wp1 (see the audit note below).

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
- Closed 2026-10-31 (todo sweep): the development map is written by a JSON task and a bad map
  warns instead of failing; PARE203/204/205 report every item in one build; a source declared
  twice is one source; `pack-cli.sh` stops when Docker is down and takes `--rid`; the bench runs
  in process when worktree copies exist; `release.yml` builds the archives (dispatched 2026-10-31: all 6 jobs, 16 archives). Pinned action SHAs
  were checked against their tags.
- Dropped 2026-10-31: lexer leniency vs TypeScript (M4: escapes, numeric separators, leading-zero
  decimals, digit ranges) and the Unicode tables. Tokenization is unaffected and a browser or tsc
  rejects such input; documented in what-pare-does-not-do.md.
- Dropped 2026-10-31: 404 for `.ts` sources in Development (the SDK serves all of `wwwroot`; documented in hot-reload.md).
- Dropped 2026-10-31: a new `.ts` outside `wwwroot` matching a `PareServeAt` glob is not stripped
  until the next build. MSBuild does not expose the original glob, so a watcher would guess; the
  log message already says a new source is served after the next build.
