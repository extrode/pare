# Contributing to Pare

Bug reports and pull requests are welcome, with two rules that are easier to read before you write
code than after.

## Read this first

### 1. Contributions need a signed CLA

Pare is under the **Islamic Software License - Permissive (ISL-P) v1.2**, with the Output
Exception (`LICENSE.md`). ISL-P lets you modify it. The
[Islamic Software License Contributor License Agreement (ISL-CLA)](CLA.md) is what lets Extrode
ship your contribution in its own builds under other terms too, and a contribution is not accepted
without it.

> The ISL-CLA is currently a **draft pending legal review**, so contributions are not yet being
> accepted. Issues and discussion are welcome now.

#### How Pare adopts the ISL-CLA

Pare adopts the **ISL-CLA, Version 1.1**, by reference. The text is reproduced whole and
unmodified in [CLA.md](CLA.md) and published at
[islamiclicense.org/isl-cla](https://islamiclicense.org/isl-cla/1.1/CLA.md). As that Agreement
requires, the Project states here:

| | Statement |
|---|---|
| (a) Project License | ISL-P, Version 1.2, with the Output Exception, as applied by Extrode in `LICENSE.md`. The variant is not ISL-R, so ISL-CLA Section 2 has no effect. |
| (b) Section 6, No AI-Generated Contributions | **Adopted.** |
| (c) Governing law | The laws of the Commonwealth of Virginia, United States of America. |
| (d) Licensor | Extrode, the party named in Section 1.2 of `LICENSE.md`. |
| (e) Forum | The state and federal courts sitting in the Commonwealth of Virginia. |

**How to sign.** ISL-CLA Section 9.6: add a row to [CONTRIBUTORS.md](CONTRIBUTORS.md) in the
first pull request that carries your contribution, from your own account; or, if you contribute by
patch or email, send a written statement that you have read the ISL-CLA, agree to it, and at which
version, and a maintainer records the row for you.

### 2. No AI-generated contributions

**Contributions must be written by a human being.** Code produced in whole or in part by an AI
assistant or language model is not accepted — see [CLA.md](CLA.md) Section 6, which Pare adopts,
for the full rule, the scope, and the reasoning.

**Describing a change in writing is the preferred way to contribute.** A precise bug report with
reproduction steps, or a written argument for a design, is more useful to this project than a patch,
and we will implement it ourselves. What we cannot accept is generated code whose copyright
provenance we are unable to establish.

| Welcome | Not accepted |
|---|---|
| Bug reports and reproduction steps in your own words | Patches written by an AI assistant |
| A prose description of a proposed design | AI-generated tests or documentation |
| A written explanation of a defect and where it is | Code you cannot explain line by line |

Every pull request must carry the certification at the end of this file, and it must
be true.

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

Keep a results file on a full run (`Extrode.Pare.Tests -xml tmp/res/unit.xml`) so a rare failure
has a name when it happens again.

## Releasing

Maintainers only. Pushing a `v*` tag runs `.github/workflows/release.yml`: the whole CI suite,
then the `archives` jobs (NativeAOT CLI and `libpare` archives for every RID on native runners,
`scripts/pack-cli.sh --rid` and `scripts/pack-cli.ps1`), then the release job, which publishes the
four NuGet packages through trusted publishing and creates the GitHub release with the archives
and `SHA256SUMS`. Run the workflow by hand (`workflow_dispatch`) to build the archives without
publishing anything. `scripts/pack-cli.sh` alone packs everything locally (macOS needs Docker for
the Linux RIDs).

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

## Pull request certification

Every pull request carries this text, and it must be true:

```
I have read and agree to the Islamic Software License Contributor License
Agreement, Version 1.1 (CLA.md), as adopted by Pare in CONTRIBUTING.md,
and my entry is in CONTRIBUTORS.md. I personally authored this contribution.
No part of it was generated by an AI or machine-learning system.
```

## Licence

Pare is under the ISL-P 1.2 licence with the Output Exception ([LICENSE.md](LICENSE.md)).
