# TypeScript sources outside wwwroot

Status: proposed

Todo item: "obj/ output for sources outside `wwwroot` (for example `Scripts/**/*.ts`, routed by a
`PareRelativePath`)". Measured 2026-10-15 on macOS arm64, SDK 10.0.401, Extrode.Pare.Build built
from dev 1245d2a, on throwaway Web SDK projects.

## Why this matters

Pare picks up `wwwroot/**/*.ts` by default. Some projects keep TypeScript elsewhere:

- a `Scripts/` folder next to `Controllers/`, the old ASP.NET habit;
- a Blazor component's script beside the component, `Components/Pages/Counter.razor.ts`. The SDK
  already serves a hand-written `Counter.razor.js` there (collocated JS); this would be the
  TypeScript version.

Such a project adds `<TypeScriptSource Include="Scripts/**/*.ts" />`. What happens today:

```
Scripts/app.ts          ->  Scripts/app.js written into the source tree
Scripts/lib/fmt.ts      ->  Scripts/lib/fmt.js
browser GET /app.js, /Scripts/app.js, /js/app.js   ->  404 (no route registered)
publish output          ->  no app.js anywhere
build output            ->  no warning
```

So the build succeeds, the files appear, and nothing serves them. The user finds out in the
browser. The question is how such a source should get a URL.

## What already works (undocumented)

The targets already route any source that carries `PareRelativePath` metadata through the obj/
pipeline that `wwwroot` sources use. Written in the project file:

```xml
<TypeScriptSource Include="Scripts/**/*.ts" PareRelativePath="js/%(RecursiveDir)%(Filename).js" />
```

Measured result:

```
obj/Debug/net10.0/pare/js/app.js, js/lib/fmt.js     (nothing written into Scripts/)
routes: js/app.js, js/lib/fmt.js, plus fingerprinted and .gz variants
publish: wwwroot/js/app.js(.br/.gz), wwwroot/js/lib/fmt.js(.br/.gz)
wwwroot/js/app.js content: import { fmt } from "./lib/fmt.js"; types blanked
```

The Blazor case, `PareRelativePath="%(RelativeDir)%(Filename).js"`, publishes
`wwwroot/Components/Pages/Counter.razor.js` and gets the fingerprinted route
`Components/Pages/Counter.b7kxzc1n4a.razor.js`. That is the same URL and fingerprint pattern the
SDK gives a hand-written `.razor.js`.

Gaps found while measuring:

| Gap | Measured or read | Effect |
|---|---|---|
| Two sources mapped to one URL (`wwwroot/js/app.ts` and `Scripts/app.ts` -> `js/app.js`) | measured | SDK crash: `MSB4018 DiscoverPrecompressedAssets ... An item with the same key has already been added`. Fixed: error PARE201 names both sources |
| `.mts` with a mapping that hard-codes `.js` | read (targets) | the user's mapping decides the extension, so `.mts` becomes `.js`, not `.mjs` |
| Edits under `dotnet watch` (hot reload mode) | read (`PareDevelopmentExtensions.cs`, development map) | `AddPareDevelopment` watches one `sourceRoot`, always `wwwroot`; a `Scripts/` edit is not re-stripped until the next build |
| `%(RecursiveDir)` uses `\` on Windows | measured on wp1 | the SDK serves `js/lib/fmt.js` regardless, but the duplicate check missed `js/lib\fmt.js`. Fixed: the targets normalize to `/` |
| No mapping at all | measured | silent: next-to-source output, never served |

## How the SDK does it for its own non-wwwroot files

Read from `Microsoft.NET.Sdk.StaticWebAssets` targets in SDK 10.0.401:

- Scoped CSS (`Foo.razor.css` anywhere): bundled into one obj/ file and served at a fixed name,
  `<PackageId>.styles.css`. Where the source sits does not decide the URL
  (`Microsoft.NET.Sdk.StaticWebAssets.ScopedCss.targets`).
- Collocated JS (`**/*.razor.js`, `**/*.cshtml.js`): `DefineStaticWebAssets` with
  `ContentRoot="$(MSBuildProjectDirectory)"`, so the URL is the path from the project folder:
  `Components/Pages/Counter.razor.js` (`Microsoft.NET.Sdk.StaticWebAssets.JSModules.targets`).

So the SDK has two answers: a fixed URL for bundles, and a project-relative path for per-file
scripts. No SDK feature serves an arbitrary folder at an arbitrary URL prefix; that is what the
`PareRelativePath` mapping adds.

## Options

| Option | What the user writes | Cost to build | Forecloses |
|---|---|---|---|
| A. Keep today, document "put browser TS in wwwroot" | nothing | docs only | the silent 404 stays; the Blazor collocated case stays unsupported |
| B. Make `PareRelativePath` public, fix its gaps | `PareRelativePath="js/%(RecursiveDir)%(Filename).js"` per include | targets: duplicate-URL error, `.mts` -> `.mjs` check, `/` normalization; `AddPareDevelopment`: watch every mapped source, not just wwwroot; tests for each; docs | the metadata name becomes public API, renaming it later is breaking |
| C. Default: an unmapped source outside wwwroot is served at its project-relative path (`Scripts/app.ts` -> `/Scripts/app.js`), like `.razor.js` | only the include | as B, plus a default mapping | folder names become URLs without the user asking; existing users who include outside sources see their next-to-source output move to obj/ and a new public URL appear (a behavior change, needs a CHANGELOG upgrade note) |
| D. C, plus include `**/*.razor.ts` by default | nothing | as C, plus default item, exclusions (`bin/`, `obj/`, `node_modules/`) | any `.razor.ts` in the project gets compiled and served whether wanted or not |

## Recommendation

**B, plus a warning for the unmapped case.** In a project with static web assets, a source outside
`wwwroot` with no `PareRelativePath` gets a new warning code (next free: PARE202) saying it will
not be served and showing the one-line mapping to add. Its output stays next to the source, so
nothing changes for anyone who relies on that today. Non-web projects are unaffected.

Why not C or D now: both pick URLs for the user, and C changes existing builds. B keeps every
URL explicit, costs no behavior change, and leaves C/D open as an opt-in property later (for
example `PareServeProjectRelative=true`), since C is B with a default mapping filled in.

Work for B, smallest first:

1. Docs: README MSBuild section and `docs/error-codes.md` (PARE202).
2. Targets: PARE202. Done: an error when two sources share a `PareRelativePath` (instead of the SDK
   crash); normalize `\` to `/`.
3. `AddPareDevelopment`: the development map lists each mapped source and its output, not one
   root, so `dotnet watch` re-strips them.
4. Tests: integration test for `Scripts/**` and `Components/**/*.razor.ts` through build,
   publish and a browser load; the duplicate-URL error; a wp1 run for the backslash case.

## Not covered

How `Microsoft.TypeScript.MSBuild` maps sources to URLs was not checked: it is not on this
machine, and its docs were not consulted.
