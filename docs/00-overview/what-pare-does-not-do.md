# What Pare does not do

Knowing the limits up front saves time.

## No type checking

A type error goes straight through:

```ts
let n: number = "hello";   // Pare strips this without complaint
```

Use TypeScript's own checker for types. TypeScript 7 ships a native `tsc` that runs without
Node. With `PareTypeCheck=true` the build runs it for you; see
[Type checking in CI](../03-guides/type-checking.md).

## No generated code

Some TypeScript features need the compiler to write JavaScript that is not in your file. Pare only
erases, so it rejects these and tells you what to write instead:

| Feature | Rejected as | Write this instead |
|---|---|---|
| `enum`, `const enum` | PARE100 | a `const` object with `as const` |
| `namespace` with values | PARE101 | an ES module |
| `constructor(private x: number)` | PARE102 | declare the field and assign it |
| `import x = require("m")` | PARE103 | `import x from "m"` |
| `export = x` | PARE104 | `export default x` |
| `<T>value` | PARE105 | `value as T` |
| parameter decorators | PARE106 | decorate the method |

These are the same cases that `"erasableSyntaxOnly": true` in `tsconfig.json` reports. Turn it on
and your editor warns you as you type. Node's built-in type stripping accepts the same subset, so
what Pare accepts also runs under Node.

## No bundling, minifying, JSX or downleveling

The output is your code minus its types. `.tsx` is not supported. Modern syntax stays as it is, so
the browser or runtime has to understand it. If you want one minified file, run a bundler on
Pare's output or use esbuild instead.

## No source maps

Not needed: lines and columns do not move.

## Not in the browser

Pare runs on a machine with .NET or through the CLI and C library. There is no WebAssembly build.
