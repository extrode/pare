# Pare

<p align="center">
  <img src="docs/assets/logo/pare-mark-320.png" alt="Pare" width="160">
</p>

Write TypeScript. Ship JavaScript. No Node.

Pare is a TypeScript type stripper written in C# for .NET 10. It reads your `.ts` files and
overwrites the types with spaces, so what is left is plain JavaScript with every character on the
line and column it started on. There are no source maps to build and no npm to install, and it
runs as a single native binary.

[![CI](https://github.com/extrode/pare/actions/workflows/ci.yml/badge.svg?branch=dev)](https://github.com/extrode/pare/actions/workflows/ci.yml)
[![License: ISL-P](https://img.shields.io/badge/license-ISL--P%201.2-blue)](LICENSE.md)
[![NativeAOT](https://img.shields.io/badge/NativeAOT-yes-brightgreen)](#the-pare-command)

You write this:

```ts
function add(a: number, b: number): number {
  return a + b;
}
```

and get this:

```js
function add(a        , b        )         {
  return a + b;
}
```

When the browser reports an error at `app.js:2:10`, that is `app.ts:2:10`.

## Contents

- [Why Pare](#why-pare)
- [Try it](#try-it)
- [In an ASP.NET Core app](#in-an-aspnet-core-app)
- [The pare command](#the-pare-command)
- [From other languages](#from-other-languages)
- [What Pare does not do](#what-pare-does-not-do)
- [How fast is it](#how-fast-is-it)
- [Documentation](#documentation)
- [Building from source](#building-from-source)
- [License](#license)

## Why Pare

If you build a .NET app and want TypeScript in the browser, the usual answer is "install Node and
a bundler". That puts a second toolchain on every dev laptop, CI runner and build server. Pare
removes it. `dotnet build` strips your `.ts` files, `dotnet watch` reloads the page when you save
one, and `dotnet publish` ships the result, compressed. Nothing else needs to be installed.

Because it only erases, it is also quick: about 330 MB/s, and zero allocations while it parses.

## Try it

Download `pare-<version>-<rid>.tar.gz` (`.zip` on Windows) from the
[releases page](https://github.com/extrode/pare/releases), unpack it and put the folder on your
`PATH`. It is a native binary and needs nothing else, not even .NET.

```sh
echo 'const n: number = 1;' | pare --stdin
```

```
const n         = 1;
```

The `Extrode.Pare.Cli` .NET tool package is not on nuget.org yet.

## In an ASP.NET Core app

Add two packages:

```sh
dotnet add package Extrode.Pare.Build
dotnet add package Extrode.Pare.AspNetCore
```

Mark the first one `PrivateAssets="all"` in your project file, and add a `using` and one line to
`Program.cs`:

```csharp
using Extrode.Pare.AspNetCore;

builder.AddPareDevelopment();   // re-strips on save; does nothing outside Development
```

Now every `wwwroot/**/*.ts` is turned into JavaScript by `dotnet build`:

| You do | What happens |
|---|---|
| `dotnet build` | `wwwroot/js/app.ts` becomes `obj/.../pare/js/app.js`, served as `/js/app.js` |
| save a `.ts` under `dotnet watch` | it is stripped again and the page reloads |
| `dotnet publish` | the site ships `app.js` with `.br` and `.gz` copies, and no `.ts` |
| write an `enum` | the build fails: `app.ts(3,1): error PARE100: ...`, like a C# error |

The output lives under `obj/`, never in your source tree, so there is nothing to gitignore.

Step by step, with a working project at the end:
[Your first web app](docs/01-getting-started/first-web-app.md).

Scripts outside `wwwroot`, Razor class libraries, Blazor components, embedding the output in a
console app: [guides](docs/03-guides/README.md).

## The pare command

```sh
pare src                        # src/**/*.ts become .js next to each source
pare src --outdir dist          # mirror into dist/
pare "src/**/*.ts" --check-only # report errors, write nothing (good for CI)
pare --stdin < in.ts > out.js   # one file, stdin to stdout
```

`.ts`, `.mts` and `.cts` become `.js`, `.mjs` and `.cjs`, and relative imports like `./util.ts` are
rewritten to `./util.js`. Exit code 0 means every file stripped, 1 means a file used syntax that
cannot be erased, 2 means an I/O or usage error.

A walk-through with a static site: [Your first static site](docs/01-getting-started/first-static-site.md).
All options: [The pare command](docs/03-guides/cli.md).

## From other languages

`libpare` is the same stripper as a C shared library, so Rust, Go, Python, Ruby, PHP, Node and
Deno can call it in-process, with no .NET installed.

```c
pare_result r;
if (pare_strip(ts, strlen(ts), 0, &r) == PARE_OK)
    fputs(r.output, stdout);
pare_free(&r);
```

[Using libpare](docs/03-guides/c-library.md) has the full program and link lines.
From C# there is also a library, `Extrode.Pare`: [Using the library](docs/03-guides/library.md).

## What Pare does not do

- **It does not check types.** Run `tsc --noEmit` (TypeScript 7 ships a native one). The build can
  run it for you with `PareTypeCheck=true`.
- **It does not generate code.** `enum`, namespaces with values, `constructor(private x)`,
  `import x = require()`, `export =`, `<T>x` and parameter decorators are refused, and the error
  says what to write instead. These are the cases `erasableSyntaxOnly` in `tsconfig.json`
  reports, so your editor can warn you first.
- **It does not bundle, minify or handle JSX.**

More in [What Pare does not do](docs/00-overview/what-pare-does-not-do.md).

## How fast is it

The same 2,392 real-world files (19.5 MB) through each tool, on an Apple M1 Max:

| Tool | Throughput | Pare is |
|---|---:|---:|
| Pare | 279 MB/s | - |
| oxc-transform | 87 MB/s | 3.2x faster |
| Node `stripTypeScriptTypes` | 40 MB/s | 7.0x faster |
| ts-blank-space | 28 MB/s | 10.0x faster |
| esbuild | 12 MB/s | 23.4x faster |

Pare's output matches TypeScript 7 `tsc` for 99.96% of the TypeScript test corpus files it strips.
Deno's standard library tests give the same results on Pare's output for 38 of 42 packages. Methods, caveats and how to
reproduce: [Benchmarks](docs/06-reference/benchmarks.md).

## Documentation

| Section | |
|---|---|
| [Overview](docs/00-overview/README.md) | what Pare is, how it works, how it compares |
| [Getting started](docs/01-getting-started/README.md) | install and first project |
| [Guides](docs/03-guides/README.md) | task by task |
| [Reference](docs/06-reference/README.md) | error codes, MSBuild properties, syntax, C API |

The docs are built into a static site with `scripts/build-docs.sh`.

## Building from source

```sh
dotnet build Pare.slnx
dotnet test --solution Pare.slnx
dotnet publish src/Extrode.Pare.Cli -c Release -r osx-arm64 -o tmp/aot   # NativeAOT CLI
scripts/pack-local.sh                                                    # packages into tmp/feed
dotnet run --project samples/WebApp                                      # the sample site
```

The slower integration tests drive Chromium, Firefox and WebKit through Playwright and need
network. Release packaging is described in [AGENTS.md](AGENTS.md#build-and-test). Project history
and decisions are in [work/progress.md](work/progress.md) and [docs/decisions](docs/decisions).

## License

Pare is under the Islamic Software License - Permissive (ISL-P) v1.2 with the Output Exception
(ISL-OE) v1.2. It is source-available with ethical use restrictions, and it is **not an OSI-approved
open-source license**. The Output Exception lets you ship the JavaScript Pare generates as part of
your own work. See [LICENSE.md](LICENSE.md) (Sections 4 and 5 hold the restrictions) and
[NOTICE.md](NOTICE.md).
