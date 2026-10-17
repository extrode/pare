# Error codes

Pare only erases types: it replaces type syntax with spaces and changes nothing else, so every
line and column of the output matches the source. TypeScript syntax that would need new
JavaScript written in its place cannot be erased, so Pare rejects it instead of guessing. This is
the rule behind TypeScript's `erasableSyntaxOnly` option and Node's built-in type stripping:
turning on `erasableSyntaxOnly` in `tsconfig.json` makes `tsc` and the editor report the
PARE100 to PARE105 cases as TS1294. Pare also rejects parameter decorators (PARE106), which that
option allows.

A rejected file gets no output. The CLI exits with 1, the MSBuild package fails the build, and
`AddPareDevelopment` logs the error and keeps the previous `.js`. Every rejection in a file is
reported in one run.

| Code | Kind | Message |
|---|---|---|
| [PARE001](#pare001) | error | syntax error |
| [PARE100](#pare100) | error | 'enum' generates runtime code and cannot be erased |
| [PARE101](#pare101) | error | namespace containing values generates runtime code |
| [PARE102](#pare102) | error | parameter property generates runtime code |
| [PARE103](#pare103) | error | 'import x = ...' generates runtime code |
| [PARE104](#pare104) | error | 'export =' is CommonJS-only emit |
| [PARE105](#pare105) | error | angle-bracket type assertion is not supported |
| [PARE106](#pare106) | error | parameter decorators need legacy experimentalDecorators |
| [PARE107](#pare107) | error | a type-only import cannot have both a default and named bindings |
| [PARE200](#pare200) | warning (MSBuild) | not served: the build now writes this file's TypeScript output under obj/ and serves that. Delete it. |
| [PARE201](#pare201) | error (MSBuild) | two sources map to one URL |
| [PARE202](#pare202) | warning (MSBuild) | not served: outside wwwroot with no PareServeAt |
| [PARE203](#pare203) | error (MSBuild) | PareServeAt is not a URL folder |
| [PARE204](#pare204) | error (MSBuild) | PareCompileSource needs PareCompiler and PareOutputExtension |

Errors print as `file:line:col: error PARE100: message (hint)` from the CLI and the hot-reload
watcher, and as `file(line,col): error PARE100: message (hint)` from MSBuild.

## PARE001

`syntax error (check the syntax at this position)`

The input is not valid TypeScript at this position. Pare stops at the first syntax error; the
position is the token where parsing failed. Nesting too deep for the parser's stack (thousands of
levels) is reported the same way rather than crashing. `.tsx` and JSX are not supported.

```ts
let total = ;
```

Fix the syntax:

```ts
let total = 0;
```

## PARE100

`'enum' generates runtime code and cannot be erased (use a 'const' object with 'as const' instead of 'enum')`

An `enum` compiles to an object built at run time, so it cannot be erased. `const enum` is
rejected too: `tsc` inlines its members at each use, which an eraser cannot do. `declare enum`
emits nothing and is erased.

```ts
enum Color { Red, Green }
```

Use an object with `as const`, and a type of the same name for its values:

```ts
const Color = { Red: 0, Green: 1 } as const;
type Color = (typeof Color)[keyof typeof Color];
let c: Color = Color.Red;
```

## PARE101

`namespace containing values generates runtime code (use an ES module instead of a namespace)`

A `namespace` or `module` block that declares values (variables, functions, classes, nested
namespaces with values) compiles to an object built at run time. A namespace that holds only types,
or one marked `declare`, is erased.

```ts
namespace Util {
  export const version = 1;
}
```

Use a module: export the values from the file, and keep namespaces for types only:

```ts
export const version = 1;
namespace Shapes {
  export type Kind = "circle" | "square";
}
let k: Shapes.Kind = "circle";
```

## PARE102

`parameter property generates runtime code (declare the field in the class body and assign it in the constructor)`

A constructor parameter with `public`, `private`, `protected`, `readonly` or `override` also
declares a field and assigns it, which needs code that is not in the source.

```ts
class Point {
  constructor(public x: number) {}
}
```

Declare the field and assign it yourself:

```ts
class Point {
  x: number;
  constructor(x: number) {
    this.x = x;
  }
}
```

## PARE103

`'import x = ...' generates runtime code (use an ES 'import' declaration)`

`import x = require("m")` is CommonJS emit, and `import x = N.y` creates a variable. `import type
x = require("m")` is erased.

```ts
import fs = require("node:fs");
```

Use an ES import, or `import type` when only types are used:

```ts
import fs from "node:fs";
import type Fs = require("node:fs");
```

## PARE104

`'export =' is CommonJS-only emit (use 'export default' instead)`

`export =` compiles to `module.exports = ...`, which has no ES module form.

```ts
function greet() {}
export = greet;
```

```ts
function greet() {}
export default greet;
```

## PARE105

`angle-bracket type assertion is not supported (use 'expr as T' instead of '<T>expr')`

`<T>expr` is the older assertion form. It reads like JSX and is not allowed in `.tsx` files, so
Pare accepts only the `as` form, which every TypeScript file can use.

```ts
declare const input: unknown;
let n = <number>input;
```

```ts
declare const input: unknown;
let n = input as number;
```

## PARE106

`parameter decorators need legacy experimentalDecorators (use TC39 decorators on the class or member instead)`

Decorators on parameters exist only in TypeScript's legacy `experimentalDecorators` mode, whose
emit rewrites the class. Standard (TC39) decorators on classes and members are kept as written; the
runtime has to support them.

```ts
declare function log(target: object, key: string, index: number): void;
class Api {
  get(@log id: string) {}
}
```

Decorate the method instead:

```ts
declare function log(value: Function, context: ClassMethodDecoratorContext): void;
class Api {
  @log
  get(id: string) {}
}
```

## PARE107

`a type-only import cannot have both a default and named bindings (split it into two 'import type' declarations)`

TypeScript rejects this form too (TS1363).

```ts
import type Config, { Options } from "./config.ts";
```

```ts
import type Config from "./config.ts";
import type { Options } from "./config.ts";
```

## PARE200

`warning PARE200: not served: the build now writes this file's TypeScript output under obj/ and serves that. Delete it.`

MSBuild package only, for web projects whose output goes under `obj/` (the default since 0.3). A
`foo.js` sits next to `foo.ts` in `wwwroot`: left there by Pare 0.2, which wrote output next to
the source, or written by hand. The build serves and publishes the stripped `foo.ts` from `obj/`
and leaves `foo.js` out, so the two cannot conflict. The build still succeeds.

Delete the `foo.js` files the warning names, and any `.gitignore` rules added for them. If a
hand-written `foo.js` is the one you want served, rename it or the `.ts`.

## PARE201

`error PARE201: two sources map to one URL, js/app.js: wwwroot/js/app.ts, Scripts/app.ts. Serve one under a different PareServeAt folder, or remove one from its item.`

MSBuild package only, for web projects whose output goes under `obj/`. Each source's output is
served at a URL: its path below `wwwroot` by default, or below the folder its `PareServeAt` names.
Two sources ended up with the same one, which would also make them write the same file. The
sources of another compiler (`PareCompileSource`) count too, so a `.svelte` and a `.ts` cannot
share a URL. Paths
that differ only in case count as the same, as they do on macOS and Windows file systems. The
build stops before stripping anything.

Give one of the named sources a different `PareServeAt`, or exclude one of them from
`TypeScriptSource` (`<TypeScriptSource Remove="Scripts/app.ts" />`) or `PareCompileSource`.

## PARE202

`warning PARE202: not served: outside wwwroot with no PareServeAt, so the .js goes next to the source and is not a static web asset. Add PareServeAt="js" to its item to serve it under js/, or add PARE202 to NoWarn to keep it as is.`

MSBuild package only, for web projects whose output goes under `obj/`. A `TypeScriptSource` (or
`PareCompileSource`) item outside `wwwroot`, such as `Scripts/app.ts`, has no `PareServeAt`. Its output is written next to
it, as `Scripts/app.js`, which the web app does not serve or publish. The build still succeeds.
Not reported for `PareOutDir`, outside web projects, or for a `.razor.ts` or `.cshtml.ts`, whose
output beside the component the SDK serves itself.

To serve it, name the URL folder:

```xml
<TypeScriptSource Include="Scripts/**/*.ts" PareServeAt="js" />
```

To keep the output next to the source on purpose, add `<NoWarn>$(NoWarn);PARE202</NoWarn>` to the
project.

## PARE203

`error PARE203: PareServeAt="js/app.js" is not a URL folder. Give a folder such as js, or / for the root, with no . or .. segment, colon, double slash or file name: the file name comes from the source.`

MSBuild package only. `PareServeAt` names a folder; each file's name and extension come from its
source (`app.ts` gives `app.js`, `app.mts` gives `app.mjs`). Values with a `.` or `..` segment, a
colon, `//` (or `\\`), or ending in a script extension such as `.js` or `.ts` are rejected. The
build stops before stripping anything.

Use a folder: `PareServeAt="js"`, or `PareServeAt="/"` to serve at the root of the project's
static web assets.

## PARE204

`error PARE204: PareCompileSource needs PareCompiler (the compiler's name) and PareOutputExtension (the output's extension, a dot and a name such as .svelte.js); got PareCompiler="husk" PareOutputExtension="svelte.js".`

MSBuild package only. A package that compiles another language to JavaScript (see "Other
compilers" in the README) declared a `PareCompileSource` item without `PareCompiler`, or without a
`PareOutputExtension` that starts with a dot and holds no folder. The build stops before compiling
anything. This is a fault in that package's `.props` or in the project file that added the item:

```xml
<PareCompileSource Include="Components/**/*.svelte" PareCompiler="husk" PareOutputExtension=".svelte.js" />
```
