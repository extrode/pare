# TypeScript outside wwwroot

By default only `wwwroot/**/*.ts` is served. If your scripts live in `Scripts/` or next to your
Razor components, tell Pare which URL folder to serve them under with `PareServeAt`.

## The one-line version

```xml
<TypeScriptSource Include="Scripts/**/*.ts;Scripts/**/*.mts" PareServeAt="js" />
```

The file name and extension come from the source. The folders matched by `**` are kept:

| Source | Served at |
|---|---|
| `Scripts/app.ts` | `/js/app.js` |
| `Scripts/lib/fmt.ts` | `/js/lib/fmt.js` |
| `Scripts/worker.mts` | `/js/worker.mjs` |

The output goes under `obj/` like a `wwwroot` source's: served from the first build, compressed
and published.

## Blazor components

A script beside its component, the way the SDK serves a hand-written `Counter.razor.js`:

```xml
<TypeScriptSource Include="**/*.razor.ts" Exclude="bin/**;obj/**;node_modules/**" PareServeAt="/" />
```

`Components/Pages/Counter.razor.ts` is served at `/Components/Pages/Counter.razor.js`.

## How the URL is built

- `/` is the root. Leading and trailing slashes are ignored, and `\` counts as `/`.
- The URL is the folder, then the part of the path matched by `**` in the `Include` that created
  the item, then the file name. Without `**` (`Scripts/*.ts`) there is no subfolder.
- In a Razor class library the folder is below `/_content/<Library>/`, like `wwwroot`.
- A `wwwroot` source with `PareServeAt` is served there instead of at its `wwwroot` path.

## When something is wrong

| You see | Meaning | Fix |
|---|---|---|
| `error PARE203` | `PareServeAt` is not a folder (`..`, `:`, `//`, or a file name like `js/app.js`) | give a folder such as `js`, or `/` |
| `error PARE201` | two sources map to one URL | change one `PareServeAt`, or remove one source |
| `warning PARE202` | a source outside `wwwroot` has no `PareServeAt`, so its `.js` is written beside it and not served | add `PareServeAt`, or add `PARE202` to `NoWarn` if that is what you want |

PARE202 is not reported for `.razor.ts` and `.cshtml.ts`. See [Error codes](../06-reference/error-codes.md).

## During development

`AddPareDevelopment()` re-strips these on save. A **new** file outside `wwwroot` is stripped and
served after the next build, because static web assets are fixed at build time. Press Ctrl+R in
`dotnet watch` to rebuild.
