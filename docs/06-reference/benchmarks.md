# Benchmarks

Measured on an Apple M1 Max (osx-arm64), .NET 10.0, Release build.

## Library speed

BenchmarkDotNet 0.15.8, one `TypeStripper.Strip` call per source, output to a reused buffer:

| Input | Mean | MB/s | Allocated |
|---|---:|---:|---:|
| TypeScript 5.9.3 `checker.ts` (3.1 MB) | 9.3 ms | 335 | 0 B |
| Real-world corpus, 2,392 files (19.5 MB) | 60.2 ms | 272 | 0 B |
| `(a = ` nested 1,000 deep | 32.3 ms | 0.2 | 0 B |

Deep nesting is quadratic, as in TypeScript's own parser. Real code never reaches it.

## Against other tools

The same 2,392 files (TypeScript's compiler, Vue, Hono, Deno std, Svelte, Zod at pinned commits)
through other tools. Each tool's output is compared with Pare's byte for byte, then as an Acornima
syntax tree without locations. Printers (esbuild, oxc, Deno) reprint literals, so the tree
comparison ignores literal spelling, `{ a: a }` versus `{ a }` and `void 0` versus `undefined`. For
printers, a looser comparison also ignores imports (they drop unused ones) and a numeric suffix on
names (they rename a shadowed `x` to `x1`). Deno is Deno's own transpiler (swc, as `deno run` uses
it), read from its cache after `deno cache --no-check`.

| Tool | Same bytes | Same tree | Same but imports dropped or names renamed | Differ | Throughput | Pare is |
|---|---:|---:|---:|---:|---:|---:|
| Pare 0.5.0 | - | - | - | - | 279 MB/s | - |
| Node 24.16 `stripTypeScriptTypes` | 1,898 | 490 | - | 0 | 40 MB/s | 7.0x faster |
| oxc-transform 0.154.0 | 13 | 2,375 | 0 | 0 | 87 MB/s | 3.2x faster |
| ts-blank-space 0.9.0 | 1,967 | 420 | - | 1 | 28 MB/s | 10.0x faster |
| esbuild 0.28.2 | 11 | 1,961 | 296 | 120 | 12 MB/s | 23.4x faster |
| Deno 2.9.7 (swc) | 11 | 2,326 | 51 | 0 | - | - |

- Same tree but not the same bytes: Node and ts-blank-space keep tabs and write UTF-8-width spaces
  for non-ASCII comment text, and Pare writes its semicolon fixes in a few places they leave blank.
- ts-blank-space's one difference is a bug on its side: `get!: T` followed by another field on the
  next line becomes a getter, and its output does not parse. Pare had the same bug until this
  comparison found it.
- Deno's 51: it drops imports whose names are unused, including `import {} from "m"`, which still
  loads `m` (Pare keeps it, as `verbatimModuleSyntax` requires), and renames shadowed bindings.
- esbuild's 120: it turns strings with line breaks into template literals and folds some
  expressions. None is a Pare error.
- Four files are left out: their output does not parse for any stripper, because they import or
  re-export a type without `type`, which needs type information to remove.
- Whole process over all files: the `pare` CLI (NativeAOT) 0.95 s, `deno cache --no-check` from a
  cold cache 1.1 s, one run each.

Throughput is the best of 5 passes in one run on a laptop and moves by 10 to 20 percent between
runs. The BenchmarkDotNet numbers above are the steady ones.

## Real-world check

`scripts/run-std-tests.cs` runs Deno's standard library test suite on Pare's output. It strips a
copy of [denoland/std](https://github.com/denoland/std) (pinned commit, 1,600+ files) with the
`pare` CLI in about 0.5 s, points the `@std/*` imports at the `.js` files, and runs `deno test` on
each package's original `*_test.ts` and stripped `*_test.js`:

- 38 of 42 packages give the same result (4,997 passing tests). 5,521 tests pass on Pare's output
  in all.
- csv: one test compares `Function.prototype.toString()` with Deno's reprinted `(n)=>n`. A
  blanking stripper keeps `(n        ) => n`.
- testing: snapshot tests look for a snapshot file named after the `.ts` test file.
- http: one test passes only on the `.js` copy, because the TypeScript copy sits under a dot
  directory (`.corpus`) that `serveDir()` refuses.
- expect: not run. Pare rejects three of its files (parameter properties and a `<T>x` assertion,
  which generate runtime code), so `expect.js` is missing.

Failures common to both runs (cli, encoding, fmt, fs and others) come from the environment.

## Reproduce

```sh
dotnet run scripts/get-bench-corpus.cs                       # ~25 MB into .corpus/realworld
npm --prefix tests/Extrode.Pare.Bench/compare ci --ignore-scripts
dotnet publish src/Extrode.Pare.Cli -c Release -r osx-arm64 -o tmp/aot
dotnet run -c Release --project tests/Extrode.Pare.Bench -- compare --passes 5
dotnet run -c Release --project tests/Extrode.Pare.Bench -- --filter '*'
dotnet run scripts/run-std-tests.cs            # or name packages: ... -- yaml semver
```

`compare` writes every tool's output under `tmp/compare/` and the differences to
`tmp/compare/differences.tsv`.
