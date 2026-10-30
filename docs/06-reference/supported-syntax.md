# Supported syntax

What Pare erases, and what it refuses. Erasing means replacing every character with a space,
except line terminators (`\n`, `\r`, ` `, ` `), which are kept.

## Erased

| Construct | Example | Erased part |
|---|---|---|
| Variable, parameter, return annotations | `let x: number`, `f(a: T): U {}` | `: number`, `: T`, `: U` |
| Optional markers | `f(a?: T)`, `{ x?: T }` in classes | `?` and the annotation |
| Definite assignment | `let x!: T`, `x!: T;` in a class | `!: T` |
| Non-null assertion | `obj!.x`, `fn()!` | `!` |
| `as` and `satisfies` | `x as T`, `x satisfies T` | ` as T`, ` satisfies T` |
| Generic parameters | `function f<T>()`, `class C<T>`, `<T>(x) => x` | `<T>` |
| Generic arguments | `f<T>(x)`, `new Map<K, V>()`, `` tag<T>`...` `` | `<T>`, `<K, V>` |
| `interface` declarations | `interface A { }` | the whole declaration |
| `type` aliases | `type A = B;` | the whole declaration |
| `declare` anything | `declare const x: T;`, `declare global {}`, `declare module "x" {}` | the whole declaration |
| Type-only namespaces | `namespace N { type X = 1 }` | the whole declaration |
| `import type`, `export type` | `import type { A } from "x"` | the whole statement |
| Inline type specifiers | `import { type A, b } from "x"` | `type A,` |
| Class modifiers | `public`, `private`, `protected`, `readonly`, `override` | the keyword |
| `abstract` classes and members | `abstract class C`, `abstract m(): void;` | `abstract`, plus the whole abstract member |
| `implements` clause | `class C implements I, J` | ` implements I, J` |
| `this` parameter | `function f(this: Window, a)` | `this: Window,` |
| Overload signatures | `function f(a: string): void;` with no body | the whole signature |
| Class index signatures | `[key: string]: any;` | the whole member |
| Bodiless class members | `m(): void;` in a class | the whole member |
| `declare` class fields | `declare x: T;` | the whole member |
| `export` of erased declarations | `export interface A {}`, `export type A = B` | the whole statement, `export` included |
| `export { type A }` | | `type A` and the trailing comma |
| `export type * from "x"` | | the whole statement |
| `unique symbol`, `asserts x is T`, `infer`, mapped and conditional types | inside types | covered by "the type" |

## Kept as written

- Standard (TC39) decorators on classes and members. The runtime has to support them.
- `declare enum`, which emits nothing, is erased. `declare` namespaces and namespaces that hold
  only types are erased.

## Refused

| Code | Construct | Why |
|---|---|---|
| PARE100 | `enum`, `const enum` | generates runtime code |
| PARE101 | `namespace` or `module` containing values | generates runtime code |
| PARE102 | parameter properties: `constructor(private x)` | generates `this.x = x` |
| PARE103 | `import x = require("y")`, `import x = N.y` | generates code |
| PARE104 | `export = x` | CommonJS-only emit |
| PARE105 | angle-bracket assertion `<T>expr` | ambiguous, and not allowed in `.tsx`; use `as` |
| PARE106 | parameter decorators | need legacy `experimentalDecorators` |
| PARE107 | `import type A, { B } from "m"` | TypeScript rejects it too (TS1363) |

Each has an example and the plain JavaScript to write in [Error codes](error-codes.md).

## Not supported

- `.tsx` and JSX.
- Syntax that needs type information to decide. For example, importing or re-exporting a type
  without the `type` keyword (`import { Foo } from "m"` where `Foo` is only a type) leaves a
  runtime import of a name that does not exist. Pare cannot know. Turn on `verbatimModuleSyntax`
  in `tsconfig.json` and `tsc` will make you write `import type`.

## The reference

When there is a grammar question, TypeScript's `parser.ts` is the reference. On the TypeScript
test corpus, Pare's output matches TypeScript 7 `tsc` emit (compared as syntax trees) for 99.96% of
the files Pare strips. The rest are triaged differences on `tsc`'s side.
