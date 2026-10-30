# The pare command

`pare` turns `.ts` files into `.js` files by erasing the types. It does nothing else: no
bundling, no minifying, no type checking, no Node packages.

## Commands

| Command | What it does |
|---|---|
| `pare src` | Writes each `src/**/*.ts` as `.js` next to its source. `.mts` becomes `.mjs`, `.cts` becomes `.cjs`. |
| `pare src --outdir js` | Writes under `js/` instead, keeping the folder structure below `src/`. |
| `pare src --check-only` | Reports errors and writes nothing. Use it in CI or a pre-commit hook. |
| `pare --stdin < in.ts > out.js` | Reads one file from stdin and writes JavaScript to stdout. For editors and pipelines. |
| `pare --version` | Prints the version. |

Inputs can be files, folders (searched recursively) or globs (`*`, `?`, `**`) for shells that do not
expand them. `.d.ts` files and `node_modules` are skipped.

> An existing `foo.js` next to `foo.ts` is replaced, like `tsc` and `esbuild` do. Keep hand-written
> JavaScript out of the source folders, or use `--outdir`.

```sh
$ cat in.ts
const n: number = 1 as const;
$ pare --stdin < in.ts
const n         = 1         ;
```

## Relative imports

Browsers and Node load `.js` files, so Pare rewrites relative specifiers that name a TypeScript
file: `./util.ts` becomes `./util.js`, `.mts` becomes `.mjs`, `.cts` becomes `.cjs`. This covers
`import`, `export ... from` and `import("...")`.

## Exit codes

| Code | Meaning |
|---|---|
| 0 | Every file was stripped (or checked). |
| 1 | A file uses syntax that cannot be erased. That file gets no output; the message names the line. |
| 2 | I/O or usage error, for example a missing input. |

```sh
$ pare --check-only bad.ts
bad.ts:1:1: error PARE100: 'enum' generates runtime code and cannot be erased (use a 'const' object with 'as const' instead of 'enum')
$ pare missing.ts
pare: missing.ts: no such file or directory
```

Every rejection in a run is reported, not only the first.

## Strip again on every save

`pare` has no watch mode. Let Node or Deno watch the folder and run it. The sample has a small
script, [`samples/CliSite/watch.mjs`](https://github.com/extrode/pare/blob/dev/samples/CliSite/watch.mjs),
that runs `pare` once and prints errors without stopping:

```sh
node --watch-path=samples/CliSite/src samples/CliSite/watch.mjs
deno run --watch=samples/CliSite/src --allow-run=pare --allow-env samples/CliSite/watch.mjs
```

Copy the script into your project and change the paths.

## In CI

```sh
pare src --check-only
```

fails the job with exit code 1 if any file uses syntax Pare cannot erase. To also check types, run
`tsc --noEmit` after it; see [Type checking in CI](type-checking.md).

## Install options

`dotnet tool install -g Extrode.Pare.Cli` installs one framework-dependent tool for every platform.
It needs the .NET 10 runtime and starts slower than the NativeAOT binary. The NativeAOT binaries for
osx-arm64, osx-x64, linux-arm64, linux-x64, linux-musl-arm64, linux-musl-x64, win-x64 and win-arm64
come as archives on the releases page and need no .NET.
