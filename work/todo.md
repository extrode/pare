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

- Windows `pare` binaries (win-x64, win-arm64): listed in the tool's RIDs but never built; pack
  them on Windows (`dotnet pack src/Extrode.Pare.Cli -c Release -r win-x64`) before a release.
  Until then `dotnet tool install` on Windows fails rather than falling back to `any`.

- .NET bug, not reported: NativeAOT on linux-musl-arm64,
  `RuntimeHelpers.TryEnsureSufficientExecutionStack()` on the main thread returns false in about
  one process in four (repro: a console app printing it, run 60 times on `sdk:10.0-alpine`
  arm64). Library callers there need a thread they created; README says so.

- .NET SDK bug, not reported: `dotnet watch` 10.0.401 on macOS, started outside the project
  directory (`--project`), fails to read `obj\Debug/net10.0/staticwebassets.development.json`
  (`IntermediateOutputPath` keeps MSBuild's backslash) and sends no static-asset refresh. A plain
  `Microsoft.NET.Sdk.Web` project shows it too; from the project directory it works, saves via
  rename included. README says to run it from the project directory.

- MSBuild in Visual Studio: untested; would need the .NET task host (`Runtime="NET"` on
  `UsingTask`, MSBuild 18) or a netstandard2.0 core.

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
