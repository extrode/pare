# Pare and the alternatives

Browsers run JavaScript, so something has to remove the types. Here is where Pare sits next to
the other options.

## At a glance

| Tool | Type checks | Rewrites code | Needs | Output matches source positions |
|---|---|---|---|---|
| **Pare** | no | no, erases only | nothing (one binary) or .NET | yes, always |
| Node 22.18+ type stripping | no | no, erases only | Node | yes |
| ts-blank-space | no | no, erases only | Node | yes |
| Deno, Bun | no | yes, so `enum` works | their runtime | with source maps |
| esbuild, swc, oxc | no | yes, can bundle and minify | npm or a binary | with source maps |
| `tsc` | yes | yes | Node or the native `tsc` | with source maps |

Pare and Node's type stripping accept the same subset of TypeScript, the one that
`erasableSyntaxOnly` describes. Code that passes one passes the other.

## Pick by situation

- **Node, Deno or Bun already runs your code.** Let them run the TypeScript. You do not need Pare.
- **You want bundling and minification and npm in your build is fine.** Use esbuild or Vite.
- **You build with .NET and want TypeScript in the browser with no Node anywhere** (dev laptops, CI,
  build servers): Pare.
- **You want a standalone converter in a Makefile, Dockerfile or CI step:** the `pare` CLI.
- **You want to strip types from Rust, Go, Python or another language:** `libpare`.

## Speed

The same 2,392 real-world files (19.5 MB: TypeScript's compiler, Vue, Hono, Deno std, Svelte, Zod)
through each tool, on an Apple M1 Max:

| Tool | Throughput | Pare is |
|---|---:|---:|
| Pare 0.5.0 | 279 MB/s | - |
| oxc-transform 0.154.0 | 87 MB/s | 3.2x faster |
| Node 24.16 `stripTypeScriptTypes` | 40 MB/s | 7.0x faster |
| ts-blank-space 0.9.0 | 28 MB/s | 10.0x faster |
| esbuild 0.28.2 | 12 MB/s | 23.4x faster |

Throughput moves 10 to 20 percent between runs on a laptop. The full table, including how closely
each tool's output matches Pare's, is in [Benchmarks](../06-reference/benchmarks.md).

## What Pare adds in .NET

| In a JavaScript project you would use | With Pare |
|---|---|
| Vite or webpack with ts-loader | `Extrode.Pare.Build`, a package reference |
| the Vite dev server, `tsc --watch` | `builder.AddPareDevelopment()` and `dotnet watch` |
| `//go:embed` or `bun build --compile` for a UI inside the binary | `<PareEmbed>true</PareEmbed>` |
| `ts.transpileModule()` | `TypeStripper.Strip(...)` |

The older .NET option, `Microsoft.TypeScript.MSBuild`, needs Node at build time.
