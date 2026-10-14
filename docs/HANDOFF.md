# HANDOFF — TsBlank: TypeScript type stripper in C# / .NET 10 NativeAOT

> Working name: **TsBlank** (rename freely). This document is the full context for Claude Code. Read it end to end before writing code.

## 1. Why this exists

- The web stack is **HTMX + Alpine.js + ASP.NET Core NativeAOT** ("Lean Native Web"), migrating away from a Svelte + Deno BFF.
- **Deno is ending:** the Deno team joined Cloudflare (announced 9 Oct 2026). The runtime gets about one more year of bug and security fixes, then development stops. Deno Deploy shuts down after six months.
- **Hard constraint: no Node, no Bun, no Deno** in the toolchain.
- TypeScript stays, but **only for browser code**: small typed modules alongside HTMX and Alpine.
- Goal: a **Node-free TS → JS pipeline that runs inside `dotnet build`**.

## 2. The plan in one paragraph

Write a **Tier 1 type stripper** in C#: parse TypeScript, then replace all type-only syntax with whitespace, so the output is valid JS with **identical line and column positions** (no source maps needed). Enforce TypeScript's `erasableSyntaxOnly` subset, which means no enums, namespaces, or parameter properties, so no code generation is ever needed. **Type checking is not our job**: use `tsgo` (TypeScript's native Go compiler) with `--noEmit` in the editor and CI. Same approach as Bloomberg's `ts-blank-space`, but zero-allocation and NativeAOT.

## 3. Scope

### In scope
- `.ts` and `.mts` files → `.js`.
- Erasing all type-only syntax (see §5).
- **Errors** (not silent passes) on non-erasable syntax (see §6).
- Library, CLI, and MSBuild integration (see §8).
- Correctness verified against the TypeScript conformance corpus (see §10).

### Out of scope (do NOT build)
- Type checking (that's `tsgo`'s job).
- Enums, namespaces with values, parameter properties, `import x = require()`, `export =`. These are **rejected**, not transformed.
- JSX / `.tsx` (maybe a later phase; design the lexer so it can be added).
- Downleveling (target is modern evergreen browsers; output is ES2022+ as written).
- Bundling, minification, tree shaking.
- Source maps (positions are preserved, so the output *is* its own map).
- A JS runtime.

## 4. Hard constraints

- **.NET 10**, C# latest, `<IsAotCompatible>true</IsAotCompatible>`, `<PublishAot>true</PublishAot>` for the CLI.
- **Performance-first and zero-allocation** in the hot path:
  - Input as `ReadOnlySpan<char>` (or UTF-8 `ReadOnlySpan<byte>`; decide in M0 and document why).
  - **No AST objects.** The parser is a *recognizer*: it walks the grammar and records **erase ranges** `(start, length)` into a pooled buffer (`ArrayPool<T>` or a reusable `ValueListBuilder`-style struct).
  - Lexer state is a plain struct (position, flags), so speculative parsing is a cheap save and rewind.
  - No LINQ, no regex, no reflection, no `dynamic`, no boxing in the hot path.
  - Output through `IBufferWriter<char>` / `IBufferWriter<byte>`.
- No third-party runtime dependencies in the core library. Test-only dependencies are fine (see §10).
- No Tailwind and no React anywhere (irrelevant here, but a standing rule).

## 5. Syntax to erase

Erasing means replacing every character with a space, **except line terminators** (`\n`, `\r`, `\u2028`, `\u2029`), which are kept.

| Construct | Example | Erased part |
|---|---|---|
| Variable, parameter, and return annotations | `let x: number`, `f(a: T): U {}` | `: number`, `: T`, `: U` |
| Optional markers | `f(a?: T)`, `{ x?: T }` in classes | `?` (and the annotation) |
| Definite assignment | `let x!: T`, `x!: T;` in class | `!: T` |
| Non-null assertion | `obj!.x`, `fn()!` | `!` |
| `as` / `satisfies` | `x as T`, `x satisfies T` | ` as T`, ` satisfies T` |
| Generic parameters | `function f<T>()`, `class C<T>`, `<T>(x) => x` | `<T>` |
| Generic arguments | `f<T>(x)`, `new Map<K, V>()`, `` tag<T>`...` `` | `<T>`, `<K, V>` |
| `interface` declarations | `interface A { }` | entire declaration |
| `type` aliases | `type A = B;` | entire declaration |
| `declare` anything | `declare const x: T;`, `declare global {}`, `declare module "x" {}` | entire declaration |
| Type-only namespaces | `namespace N { type X = 1 }` (no values) | entire declaration |
| `import type` / `export type` | `import type { A } from "x"` | entire statement |
| Inline type specifiers | `import { type A, b } from "x"` | `type A,` |
| Class modifiers | `public`, `private`, `protected`, `readonly`, `override`, `abstract` (on members) | the keyword |
| `abstract` classes / members | `abstract class C`, `abstract m(): void;` | `abstract`, plus the whole abstract member |
| `implements` clause | `class C implements I, J` | ` implements I, J` |
| `this` parameter | `function f(this: Window, a)` | `this: Window,` |
| Overload signatures | `function f(a: string): void;` (no body) | entire signature |
| Class index signatures | `[key: string]: any;` | entire member |
| Bodiless class members | `m(): void;` in class | entire member |
| `declare` class fields | `declare x: T;` | entire member |
| `export` of erased decls | `export interface A {}`, `export type A = B` | entire statement, including `export` |
| `export { type A }` | | `type A` (and the trailing comma) |
| `export type * from "x"` | | entire statement |
| `unique symbol`, `asserts x is T`, `infer`, mapped and conditional types | inside types | covered by "the type" |

## 6. Syntax to reject (emit a diagnostic, exit non-zero)

| Construct | Why |
|---|---|
| `enum`, `const enum` | Generates runtime code |
| `namespace` / `module` containing values | Generates runtime code |
| Parameter properties `constructor(private x)` | Generates `this.x = x` |
| `import x = require("y")`, `import x = N.y` | Generates code |
| `export = x` | CommonJS-only emit |
| Angle-bracket assertions `<T>expr` | Ambiguous, and illegal in `.tsx`. Ask for `as` instead. |
| Legacy `experimentalDecorators` semantics | We don't know the config. TC39 decorators are plain JS and **pass through untouched**. |

Diagnostics must carry `file:line:col` plus a short message and a fix hint (for example, "use a `const` object with `as const` instead of `enum`").

## 7. Known traps (read before writing the parser)

1. **`<` ambiguity.** `f<T>(x)` is a generic call; `a < b > (c)` is two comparisons. Use TypeScript's own rule: speculatively parse type arguments, and accept only if parsing succeeds **and** the next token is one of `(`, a template literal, or a token that can't start an expression (TS's `canFollowTypeArgumentsInExpression`). Otherwise, rewind.
2. **Arrow functions vs parenthesized expressions.** `(a: T) => x` vs `(a)`. Speculative parse with rewind, and handle `async (x): T => ...`.
3. **Regex vs division.** Decide based on the previous significant token.
4. **Template literals.** Nested `${ }` needs a brace-depth stack in the lexer.
5. **ASI hazards created by erasure.**
   - **Return type across lines:** in `(a)\n: T => x`, erasing `: T` leaves a newline between `)` and `=>`, which is illegal JS. Fix: move the `)` to the end of the erased region (ts-blank-space does this).
   - **Erased statement followed by a line starting with `(`, `[`, or a template:** if a removed declaration sits between two statements, the previous line can merge with the next. Fix: if the char before the erased statement isn't `;` or `}`, write `;` as the first char of the erased range.
   - **`return`/`throw`/`yield` followed by an erased type on the next line:** check that this can't change meaning. Write tests.
6. **Contextual keywords.** `type`, `as`, `satisfies`, `declare`, `abstract`, `readonly`, `namespace`, `module`, `keyof`, `infer`, `is`, `asserts`, `override`, `accessor`, and `out`/`in` on type parameters can all be identifiers. Example: `type = 1;` is valid JS.
7. **`accessor` is JS** (TC39 decorators). Don't erase it.
8. **`import type X, { Y }`** is a TS error; just reject it.
9. **Non-null `!` vs `!=`.** Postfix `!` only directly after an expression with no line break before it.
10. **Optional chaining with generics:** `a?.b<T>()`.
11. **Class member ambiguities:** `get`, `set`, `static`, and `async` as member *names*.

## 8. Deliverables / repo layout

```
TsBlank/
├─ src/
│  ├─ TsBlank.Core/          # library: lexer, recognizer, eraser. Zero deps.
│  ├─ TsBlank.Cli/           # `tsblank` CLI, NativeAOT
│  └─ TsBlank.Build/         # MSBuild task + .targets (NuGet-packable)
├─ tests/
│  ├─ TsBlank.Tests/         # unit + snapshot tests
│  ├─ TsBlank.Conformance/   # runs the TypeScript conformance corpus
│  └─ TsBlank.Bench/         # BenchmarkDotNet
├─ scripts/
│  ├─ get-tsgo.cs            # `dotnet run scripts/get-tsgo.cs`: download tsgo, no npm
│  └─ get-ts-corpus.cs       # sparse-clone TypeScript repo test cases
├─ samples/WebApp/           # minimal ASP.NET Core NativeAOT app using TsBlank.Build
├─ tsconfig.json             # for tsgo checking (see §9)
├─ HANDOFF.md
└─ PROGRESS.md               # Claude Code keeps this updated per milestone
```

### Core API (proposed; refine in M0)

```csharp
public static class TsBlank
{
    // Returns false if non-erasable syntax was found; diagnostics are written to the sink.
    public static bool Strip(
        ReadOnlySpan<char> source,
        IBufferWriter<char> output,
        IDiagnosticSink diagnostics);
}

public interface IDiagnosticSink
{
    void Report(in Diagnostic d); // Diagnostic is a readonly struct: code, start, length, message id
}
```

### CLI

```
tsblank <files|globs...> [--outdir dir] [--check-only] [--stdin]
```
- Mirrors the directory structure into `--outdir`, rewriting `.ts` → `.js` and `.mts` → `.mjs`.
- **Rewrites relative import specifiers** `./x.ts` → `./x.js`. This is the only non-whitespace change besides the ASI fixes, so it must be listed in the README.
- Exit codes: 0 = ok, 1 = rejected syntax, 2 = I/O or usage error.

### MSBuild integration (`TsBlank.Build`)

- Item group: `<TypeScriptSource Include="wwwroot/**/*.ts" />` (default glob, overridable).
- Target runs **before `Build`**, is **incremental** (Inputs/Outputs), and writes `.js` next to the source or into `$(TsBlankOutDir)`.
- Runs **in-process** (an MSBuild task referencing Core), with no process spawn.
- Optional `<TsBlankTypeCheck>true</TsBlankTypeCheck>` runs `tsgo --noEmit -p tsconfig.json` as an `Exec`. Off by default, on in CI.

### Optional (last milestone): dev middleware

ASP.NET Core middleware that serves `/foo.js` by stripping `/foo.ts` on the fly, caching by file timestamp. Development only.

## 9. Type checking with tsgo (no npm)

`tsconfig.json`:
```json
{
  "compilerOptions": {
    "target": "esnext",
    "module": "esnext",
    "moduleResolution": "bundler",
    "lib": ["esnext", "dom", "dom.iterable"],
    "strict": true,
    "noEmit": true,
    "isolatedModules": true,
    "verbatimModuleSyntax": true,
    "erasableSyntaxOnly": true,
    "allowImportingTsExtensions": true
  },
  "include": ["**/*.ts"]
}
```

`scripts/get-tsgo.cs`: fetch the platform package tarball for `@typescript/native-preview` straight from the npm registry over HTTPS, extract the `tsgo` binary, and put it in `./.tools/`. **Verify the current package name, platform sub-package naming, and tarball layout first**: these were preview-era names and may have changed. If TypeScript 7 has shipped stable, use that instead.

Alpine typings: vendor a `.d.ts` file if needed. No `node_modules`.

## 10. Testing strategy

1. **Unit and snapshot tests:** one test per row of §5, §6, and §7. Input → expected output with exact whitespace.
2. **Position invariant:** for every test, every non-erased, non-whitespace character must sit at the same offset in input and output (except documented ASI and `)` moves).
3. **Validity:** every output must parse as a JS module. Use **Acornima** (pure C# JS parser) in tests only.
4. **Differential test against tsgo:** for each corpus file that passes `erasableSyntaxOnly`, compare our output with `tsgo` emit (`--target esnext --module esnext`) by **parsing both with Acornima and comparing ASTs**, ignoring locations. Any mismatch is a bug in one of the two; investigate.
5. **Conformance corpus:** `tests/cases/conformance` and `tests/cases/compiler` from `microsoft/TypeScript`, fetched by `scripts/get-ts-corpus.cs` (sparse checkout, pinned commit). Track the pass rate in `PROGRESS.md`. Files that use rejected syntax should produce a diagnostic, not a crash.
6. **Fuzzing (stretch):** never crash, never loop forever, on any input.
7. **Benchmarks:** throughput in MB/s and allocations per file on (a) a large real-world TS file, (b) the whole corpus. Target: **0 B allocated per `Strip` call** beyond the output buffer, with pools warmed.

## 11. Milestones

| # | Milestone | Done when | Est. |
|---|---|---|---|
| M0 | Repo, solution, CI, `PROGRESS.md`, API sketch, UTF-16 vs UTF-8 decision | Builds, CI green, AOT publish of an empty CLI works | 2–3 days |
| M1 | Lexer: all JS and TS tokens, templates, regex detection, contextual keywords | Lexer round-trips the whole corpus without errors | 1–1.5 weeks |
| M2 | Recognizer for JS statements and expressions (no TS yet) | Plain JS passes through byte-identical | 1–1.5 weeks |
| M3 | TS types grammar + all §5 erasures | All §5 tests pass | 1.5–2 weeks |
| M4 | §6 rejections + §7 traps + ASI fixes | All §6 and §7 tests pass | 1 week |
| M5 | Conformance + differential harness | ≥ 99% pass rate on eligible corpus files; every failure triaged | 1 week |
| M6 | CLI + MSBuild task + sample app | `dotnet build` of `samples/WebApp` produces working JS; CLI publishes NativeAOT | 3–5 days |
| M7 | Performance pass + benchmarks | Zero allocations confirmed; numbers in README | 3–5 days |
| M8 | (Optional) dev middleware, tsgo script polish | | 2–3 days |

**Total: about 6–8 weeks** of focused work.

## 12. Working agreements for Claude Code

- Work milestone by milestone. **Update `PROGRESS.md`** at the end of each session: what's done, what's next, open questions, and the current corpus pass rate.
- Tests first for each §5, §6, and §7 row.
- When grammar behavior is unclear, **TypeScript's `parser.ts` is the reference**. Match its decisions and cite the function name in a comment.
- Don't add dependencies to `TsBlank.Core`. Ask before adding any to other projects.
- Keep explanations in commits and PRs short and to the point.
- If something in this handoff turns out to be wrong (package names, a grammar assumption), fix it in this file and note it in `PROGRESS.md`.

## 13. Cloud environment setup

Claude Code on the web runs in a cloud container. If .NET 10 isn't available, add this setup step:

```bash
curl -sSL https://dot.net/v1/dotnet-install.sh -o dotnet-install.sh
bash dotnet-install.sh --channel 10.0 --install-dir "$HOME/.dotnet"
export PATH="$HOME/.dotnet:$PATH"
dotnet --info
```

NativeAOT publish on Linux needs `clang` and `zlib1g-dev`. Install them if `dotnet publish -p:PublishAot=true` fails.

## 14. Alternative considered (not chosen, kept for reference)

Build esbuild as a Go shared library (`-buildmode=c-shared`) and call it via P/Invoke. That's 1–3 weeks and supports all TS syntax plus bundling, but it adds a native Go binary per platform and isn't C#. Fall back to this only if TsBlank stalls past M5.
