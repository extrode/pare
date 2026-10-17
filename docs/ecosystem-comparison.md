# Pare and its counterparts

What Pare does, in plain terms, next to the tools that do the same job in Node, Deno, Bun and
the rest of the JavaScript world. Written 2026-10-17.

## 1. TypeScript to JavaScript

Browsers run JavaScript, so the types have to go. Pare overwrites them with spaces and touches
nothing else:

```ts
// app.ts
function add(a: number, b: number): number { return a + b; }
let names: string[] = [];
```
```js
// app.js: same length, same lines
function add(a        , b        )         { return a + b; }
let names           = [];
```

Every character keeps its line and column, so a browser error at `app.js:2:5` is `app.ts:2:5`.
No source maps.

| Ecosystem | Counterpart | Difference |
|---|---|---|
| Node 22.18+ / 23.6+ | runs `.ts` directly ("type stripping", Amaro, built on swc) | same blanking idea, inside Node |
| Bloomberg ts-blank-space | same technique, npm library | needs Node |
| Deno, Bun | run `.ts` directly | rewrite the code (so `enum` works) and use source maps |
| esbuild, swc, oxc | fast compilers | rewrite, bundle, minify; need source maps; npm or native binaries |
| `tsc` | the official compiler | also type checks; slowest |

Pare, like Node's type stripping, refuses TypeScript that would need generated code, and says
what to write instead ([error codes](error-codes.md)):

```ts
enum Color { Red }              // PARE100: use a const object + 'as const'
constructor(private x: number)  // PARE102: declare the field, assign it yourself
let n = <number>value;          // PARE105: use 'value as number'
```

TypeScript 5.8's `--erasableSyntaxOnly` allows the same subset: code `tsc` accepts with that
flag, Pare and Node accept too.

Pare does not type check: `let x: number = "hi"` strips fine, as it does in Node, esbuild and
`deno run`. Use `tsc --noEmit` (Deno's `deno check`), or `PareTypeCheck=true` to have the build
run it.

## 2. The `pare` CLI

```sh
pare src                          # src/**/*.ts -> .js next to each
pare src --outdir dist            # mirror into dist/
pare "src/**/*.ts" --check-only   # report only, for CI
```

One native binary, about 1.4 MB, no runtime. Counterparts: `tsc --outDir dist`,
`esbuild src/*.ts --outdir=dist`, `swc src -d dist`, all from npm or a downloaded binary.

## 3. Inside the .NET build (`Extrode.Pare.Build`)

```xml
<PackageReference Include="Extrode.Pare.Build" Version="0.5.0" PrivateAssets="all" />
```

`dotnet build` then strips `wwwroot/js/app.ts` to `obj/`, serves it as `/js/app.js` (compressed,
published) and fails on `enum` like a C# error: `app.ts(3,1): error PARE100: ...`.

| Ecosystem | Counterpart |
|---|---|
| JavaScript | Vite, webpack + ts-loader, `"build": "tsc"` in `package.json` |
| older .NET | `Microsoft.TypeScript.MSBuild`, which needs Node at build time |
| Deno | none; `deno run` handles TypeScript itself |

## 4. Edit, save, refresh (`AddPareDevelopment`)

```csharp
builder.AddPareDevelopment();   // Development only
```

`dotnet watch` does not rebuild on a `.ts` save; the running app re-strips each save and the
browser reloads. Counterparts: the Vite dev server, `tsc --watch`, `node --watch`,
`deno run --watch`.

## 5. UI inside the binary (`PareEmbed`)

```xml
<PareEmbed>true</PareEmbed>
<PareEmbedPrefix>ui/</PareEmbedPrefix>
```
```csharp
var js = typeof(Program).Assembly.GetManifestResourceStream("ui/web/app.js");
```

Counterparts: Go's `//go:embed`, `bun build --compile`, `deno compile`, Rust's `include_str!`.

## 6. The library

```csharp
string? js = TypeStripper.Strip(tsSource, out var errors);
```

No package dependencies, no allocation while parsing, 270-330 MB/s: the speed class of swc
and oxc. Output matches TypeScript 7 `tsc` on 99.96% of the corpus files it strips.
Counterparts: `ts.transpileModule()`, `stripTypeScriptTypes()` from `node:module`,
ts-blank-space, `@swc/core`.

## Which to pick

- Node, Deno or Bun already in the stack: let them run TypeScript.
- Bundling and minification wanted, npm in the build acceptable: esbuild or Vite.
- .NET, TypeScript in the browser, no Node anywhere (dev machines, CI, build servers): Pare.

## Outside .NET

The CLI archives need no .NET, so any stack can call `pare` from a Makefile, CI step or
Dockerfile. Gaps for general use:

| Gap | What it takes |
|---|---|
| a library for other languages | done 2026-10-17: `libpare`, a C shared library from NativeAOT that Rust, Go (cgo), Python (ctypes), Ruby, PHP, and Node or Deno FFI can call (README "C library") |
| browser or edge | WebAssembly; .NET's WASM target ships a multi-MB runtime, NativeAOT-LLVM is experimental |
| `cargo add`, `pip install`, npm | thin wrappers over the shared library, maintained per ecosystem |
| `.tsx` / JSX | lexer and recognizer work; the lexer was designed to allow it |
| built-in `--watch` | today: documented recipes with external watchers |

Outside .NET, esbuild (one ~10 MB binary that also bundles, minifies and handles TSX and
`enum`), Node's built-in stripping, and oxc or swc already cover the ground. Pare's edge there:
no source maps, a smaller binary, and the strict erasable subset, so what Pare accepts runs
under Node's stripping unchanged.

## Svelte and SvelteKit the way Deno runs them

Deno runs SvelteKit because it is a JavaScript runtime: an engine, npm resolution, Node API
compatibility, a server. Pare is one step of a pipeline, and Svelte 5's compiler strips
erasable TypeScript in `<script lang="ts">` itself.

| SvelteKit needs | Deno gives | Closest .NET-only option |
|---|---|---|
| the Svelte compiler (JavaScript) for `.svelte` -> `.js` | V8 | an embedded engine: ClearScript (V8, native binary per platform, fast) or Jint (pure C#, slow) |
| `import 'svelte/internal/client'` and npm packages | npm compatibility | vendor the `svelte` package's ESM files, serve them with an import map |
| bundling (Vite, Rollup) | `deno run npm:vite` | none: browsers load ES modules, HTTP/2 makes many files cheap |
| dev server, hot module reload | Vite under Deno | `dotnet watch` + `AddPareDevelopment`, recompiling `.svelte` on save (full reload) |
| server rendering, `+page.server.ts`, form actions | a JavaScript runtime in production | none short of shipping an engine and rebuilding SvelteKit's server: building Deno |

Three tiers:

1. Svelte components as a client-side app, no SvelteKit: the Svelte compiler in an embedded
   engine at build time, output under `obj/`, the runtime vendored behind an import map, ASP.NET
   Core for routing and the API. About 2-4 weeks after a spike. Pinned Svelte version.
2. SvelteKit-style routing and layouts rebuilt in C#, rendered on the client: large, and most of
   SvelteKit is gone.
3. Full SvelteKit with server rendering, no Node or Deno: a JavaScript runtime to maintain.
   Not realistic.

[handoff.md](handoff.md) records the move from Svelte + Deno to HTMX + Alpine.js + ASP.NET Core
NativeAOT. Tier 1 keeps the no-Node rule but brings a component framework back.
Tracked in [work/todo.md](../work/todo.md).
