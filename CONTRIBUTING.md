# Contributing

Bug reports and pull requests are welcome. For anything bigger than a bug fix, open an issue
first so we can agree on the approach.

## Build and test

You need the .NET 10 SDK.

```sh
dotnet build Pare.slnx
dotnet test --solution Pare.slnx
```

The unit tests run in under a second. The integration tests run real `dotnet build` and
`dotnet publish` on temporary projects and drive three browsers through Playwright, so they take
a few minutes and need network. The TypeScript corpus and the `tsc` comparison tests skip
themselves until you fetch their inputs (`dotnet run scripts/get-ts-corpus.cs`,
`dotnet run scripts/get-tsc.cs`).

## Rules that matter

- **The hot path does not allocate.** No LINQ, regex, reflection, `dynamic` or boxing, and no
  AST objects: the recognizer records erase ranges.
- **Grammar follows TypeScript's `parser.ts`.** Name the function you matched in the code or the
  pull request.
- **The core library has no package references.** Adding a package anywhere else needs an issue
  first.
- **Tests come with the change**, including the failure path. A new rejection also needs its
  entry in [docs/06-reference/error-codes.md](docs/06-reference/error-codes.md); a test checks
  the two stay in step.
- C# sources stay ASCII.

## Licence

Pare is under the ISL-P 1.2 licence with the Output Exception ([LICENSE.md](LICENSE.md)). A
contribution is made under the same licence.
