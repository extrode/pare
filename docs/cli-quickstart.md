# CLI quickstart

`pare` turns `.ts` files into `.js` files by erasing the types. It does nothing else: no
bundling, no minifying, no type checking, no Node packages. Use it when you want to write
TypeScript for a plain static site, a Deno or Node script, or any project that is not built with
.NET. For ASP.NET Core projects, the MSBuild package does this on `dotnet build` instead (see the
[README](../README.md#msbuild)).

## Install

Pick one.

**Standalone binary, no .NET needed.** Each [release](https://github.com/extrode/pare/releases)
has `pare-<version>-<rid>.tar.gz` for osx-arm64, osx-x64, linux-x64, linux-arm64,
linux-musl-x64 and linux-musl-arm64, and `.zip` for win-x64 and win-arm64. Unpack it and put the
folder on `PATH`:

```sh
tar -xzf pare-0.4.0-osx-arm64.tar.gz
pare-0.4.0-osx-arm64/pare --version     # 0.4.0
```

**.NET tool.** Needs the .NET SDK. It installs the same native binary for the platforms above, and
a build that runs on the .NET 10 runtime anywhere else. Until the package is on nuget.org, download
the release's `Extrode.Pare.Cli.*.nupkg` files into one folder and point `--add-source` at it:

```sh
dotnet tool install -g Extrode.Pare.Cli --version 0.4.0 --add-source ./packages
pare --version                          # 0.4.0
```

## Try the sample

[`samples/CliSite`](../samples/CliSite) is a static page with three TypeScript modules in `src/`.
`main.ts` imports `./model.ts` and `./render.ts`, and uses interfaces, generics, `satisfies`, `as`,
`!` and a type-only import. From the repository root:

```sh
pare samples/CliSite/src --outdir samples/CliSite/js
python3 -m http.server 8000 -d samples/CliSite
```

Open http://localhost:8000. You should see "2 of 3 open" above three tasks sorted by priority,
and "Add task" adds one. Any static server works, for example Deno's:

```sh
deno run --allow-net --allow-read --allow-sys jsr:@std/http/file-server --port 8000 samples/CliSite
```

Look at `samples/CliSite/js/main.js`. The types are replaced by spaces, so every line and column
matches `src/main.ts`, and browser stack traces point at the right place in the TypeScript
without a source map. Relative imports are rewritten from `./model.ts` to `./model.js`:

```js
import { byPriority, summary,           } from "./model.js";
...
const list = document.querySelector("#tasks")                    ;
const status = document.querySelector                      ("#status") ;
```

## Options

| Command | What it does |
|---|---|
| `pare src` | Writes each `src/**/*.ts` as `.js` next to its source (`.mts` to `.mjs`, `.cts` to `.cjs`). |
| `pare src --outdir js` | Writes under `js/` instead, keeping the folder structure below `src/`. |
| `pare src --check-only` | Reports errors and writes nothing. Use it in CI or a pre-commit hook. |
| `pare --stdin < in.ts > out.js` | Reads one file from stdin, writes JavaScript to stdout. For editors and pipelines. |

Inputs can be files, folders (searched recursively) or globs. `.d.ts` files and `node_modules`
are skipped. An existing `.js` next to its `.ts` is replaced, so keep hand-written JavaScript out
of the source folders or use `--outdir`.

```sh
$ cat in.ts
const n: number = 1 as const;
$ pare --stdin < in.ts
const n         = 1         ;
```

Exit codes:

| Code | Meaning |
|---|---|
| 0 | Every file was stripped (or checked). |
| 1 | A file uses syntax that cannot be erased. That file gets no output; the error names the line. |
| 2 | I/O or usage error, for example a missing input. |

```sh
$ pare --check-only bad.ts
bad.ts:1:1: error PARE100: 'enum' generates runtime code and cannot be erased (use a 'const' object with 'as const' instead of 'enum')
$ pare missing.ts
pare: missing.ts: no such file or directory
```

## Strip again on every save

`pare` has no watch mode; let Node or Deno watch the folder and re-run it.
[`samples/CliSite/watch.mjs`](../samples/CliSite/watch.mjs) runs `pare` once and prints errors
without stopping, and either runtime re-runs it when a file in `src/` changes:

```sh
node --watch-path=samples/CliSite/src samples/CliSite/watch.mjs
deno run --watch=samples/CliSite/src --allow-run=pare --allow-env samples/CliSite/watch.mjs
```

Copy the script into your project and change the paths in it.

## What pare does not do

- **No type checking.** A type error goes through unchanged. Run `tsc --noEmit` (with
  `"erasableSyntaxOnly": true` in `tsconfig.json`) or your editor for that.
- **No code generation.** `enum`, namespaces with values, parameter properties
  (`constructor(private x: number)`), `import x = require(...)`, `export =`, `<T>x` assertions and
  parameter decorators are rejected, not rewritten. [Error codes](error-codes.md) lists each one
  with the plain JavaScript to write instead.
- **No bundling, minifying, JSX or downleveling.** The output is your code minus its types.
