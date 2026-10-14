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

- Redundant guards found by the mutation sweep (removing each changes no behavior): `Reject`'s
  `if (!_ambient)` in `Parser.cs` (`ParseDeclare` drops the diagnostics with `DropSince`); the
  `.d.ts` Exclude in `Extrode.Pare.Build.props` (`_PareComputeOutputs` filters `.d` already);
  `ThrowIfGreaterThan` in `SourceLocation.FromOffset` (the slice throws the same exception).
  `EditWriter`'s replace-after-erase guard is unreachable through the parser today but has a
  direct unit test. Decide: keep as defense, or remove.
- MSBuild default items cover `wwwroot/**/*.ts` and `*.mts` but not `*.cts`. Decide whether
  CommonJS sources belong in a browser `wwwroot`.
- MSBuild: `.ts` sources under wwwroot are published as static assets next to the `.js`.
  Decide whether the package should drop `@(TypeScriptSource)` from Content by default.
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

- (nothing yet)
