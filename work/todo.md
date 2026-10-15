# Todo — pare

The unscheduled backlog: everything known to be worth doing that nobody has committed to a date.
Items enter here from anywhere (a review comment, an out-of-scope note, a bug found in passing)
and leave by being promoted into `status/next.md` or dropped with a reason.

Keep it honest rather than tidy. A backlog that only contains things someone intends to do is a
wish list; the value is in recording the ones nobody has got to yet.

## Now-ish

Small, clear, and could be picked up without a design decision.

- (nothing yet)

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

- `PareServeAt` sources: `AddPareDevelopment` doesn't re-strip them on save (needs every source
  and URL in the development map). docs/decisions/2026-10-15-002-ts-outside-wwwroot.md.
- `PareOutDir` with a `.razor.ts` source: the `.js` under the out dir becomes Content with no
  component beside it, so the SDK fails the build with BLAZOR106 (seen 2026-10-15, not fixed).
- Razor class libraries: tested and in README. `AddPareDevelopment` doesn't re-strip library
  sources on save (option B in docs/decisions/2026-10-15-003-razor-class-libraries.md).
- README `## CLI` install line assumes nuget.org; until then it needs `--add-source` (as
  docs/cli-quickstart.md says).
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

## Dropped

Not doing, with the reason. Kept so the same idea does not get re-raised every quarter.

- Removing `ThrowIfGreaterThan` (SourceLocation), the `.d.ts` Exclude (props) and EditWriter's
  replace-after-erase guard: each is redundant today but guards a boundary other code can cross
  (public API, user-supplied items, edit order). Kept. (2026-10-15)
- Fixing quadratic time on pathological nesting (`(a = (a = ...` 5000 deep: 6.7 s): same shape as
  TypeScript's parser, inputs are the user's own files, `Guard()` already stops deep recursion.
  M7 adds a bench row so a regression shows. (2026-10-15)
- Warning or refusing when a hand-written `foo.js` sits next to `foo.ts`: indistinguishable from
  a generated one without a marker, which would break the position guarantee. Documented
  instead; output under `obj/` (v0.2) would remove the problem. (2026-10-15)
- `.cts` in the MSBuild default items: CommonJS does not run in a browser `wwwroot`. Explicit
  items work. (2026-10-15)
