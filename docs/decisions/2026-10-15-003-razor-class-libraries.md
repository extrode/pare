# 003: TypeScript in Razor class libraries

Date: 2026-10-15. Status: accepted; A and B done.

B as built: each web project's build writes its development map to `obj/` and copies it to its
own output, and to the output of every project referencing it, as
`Extrode.Pare.development.<project>.json` (`None`, `CopyToPublishDirectory="Never"`).
`AddPareDevelopment` reads `Extrode.Pare.development.json` and every
`Extrode.Pare.development.*.json` beside it, one watcher per output root. Checked under
`dotnet watch` (hot reload): a library save logs `Stripped ../Lib/Scripts/fmt.ts`, then
"Static asset changes applied", no restart. A library from NuGet has no sources and no map.

**Short answer: it already works in 0.3.0.** A Razor class library (RCL) that references
`Extrode.Pare.Build` gets its `.ts` stripped, served at `/_content/<lib>/...` and published, with
no change to Pare. What's left is a test and a README section. The only real choice is whether
re-strip on save (`AddPareDevelopment`) should cover library sources, and that can wait.

## What an RCL is, and what the question was

A Razor class library is a .NET project that ships UI pieces (components, CSS, JS) for several
web apps to share. Its `wwwroot/` files aren't served at the app's root. The app serves them
under `/_content/<LibraryName>/`.

Example: a library `MyLib` with `wwwroot/js/widget.ts`:

```ts
interface Options { label: string }
export function widget(o: Options): string { return o.label as string; }
```

An app references it and loads the module:

```html
<script type="module">
  import { widget } from "/_content/MyLib/js/widget.js";
</script>
```

The open question (work/todo.md) was whether Pare's obj/ output, which registers each `.js` at
the source's path below `wwwroot`, also picks up the library's `_content/MyLib/` prefix. It
does: Pare registers its outputs with `BasePath="$(StaticWebAssetBasePath)"`, which the SDK sets to
`_content/MyLib` in a library and `/` in an app.

## Measured with the released 0.3.0 packages

Setup: `dotnet new razorclasslib` (MyLib) referencing `Extrode.Pare.Build` 0.3.0 with
`PrivateAssets="all"`, and `dotnet new web` (App) with a `ProjectReference` to it and
`app.MapStaticAssets()`. SDK 10.0.401, macOS arm64.

| Scenario | Result |
|---|---|
| Build | `MyLib/obj/Debug/net10.0/pare/js/widget.js` written, types replaced by spaces |
| App, Development, `GET /_content/MyLib/js/widget.js` | 200 `text/javascript`, stripped |
| App's endpoint manifest | `_content/MyLib/js/widget.js`, `.gz`, and a fingerprinted copy; no `.ts` |
| Publish (`-c Release`) | `wwwroot/_content/MyLib/js/widget.js` + `.br` + `.gz`, all stripped; no `.ts` |
| `dotnet pack` of MyLib | `staticwebassets/js/widget.js` stripped, no `.ts`, no dependency on any Extrode package |
| App without Pare, library with Pare | Works. Only the library needs the reference |
| App with Pare, library without | Library's `.ts` isn't stripped: Pare looks only in the app's own `wwwroot` (from the props, not run) |

So a library author can ship TypeScript in an RCL, and the NuGet package they publish contains
plain JavaScript. Apps that use the library need nothing installed.

## What isn't covered

1. **No test.** All of the above was a one-off check. A regression in the static web assets
   wiring (`SourceId`, `BasePath`) would go unnoticed.
2. **Not in the README.** Users would have to guess that the library, not the app, needs the
   reference, and that `PrivateAssets="all"` keeps the package out of the library's dependencies.
3. **Re-strip on save doesn't reach library sources** (fixed since, see top). `AddPareDevelopment` reads one
   `Extrode.Pare.development.json` from the app's build output, which names only the app's
   `wwwroot`. Editing `MyLib/wwwroot/js/widget.ts` while the app runs serves the old `.js` until
   the next build. From the targets and code, not run under `dotnet watch`.

Seen along the way, not RCL-specific: in Development, `/js/own.ts` and
`/_content/MyLib/js/widget.ts` both return 200. The SDK's development manifest serves every file
under each `wwwroot` through a `**` pattern, so sources are downloadable while debugging. Publish
output has no `.ts`. Listed in work/todo.md.

## Options

| Option | What users get | Cost | Forecloses |
|---|---|---|---|
| A. Test + README only | A guarantee it keeps working, and docs saying which project gets the reference | Small: one integration test (RCL + app, build and publish, check paths), one README section | Nothing |
| B. A + re-strip on save for libraries | Edits to a library's `.ts` show on browser refresh without a build | Medium: the map lists every referenced project's source and output roots; the watcher watches several roots and writes into other projects' `obj/` | Little. The map format changes, but it's internal |
| C. App strips libraries that lack Pare | Libraries need no reference | High: the app would write into the library's `obj/`, and a library from NuGet has no `.ts` to strip anyway | Library authors' control over their own output |

On by default: under A and B nothing new is switched on. A library that references Pare already
gets this, and one that doesn't is unaffected. Only C would need a default, and it's the option
to reject.

## Recommendation

**A now, B when someone asks for it.** A is cheap and turns "it worked once" into "it's tested".
B is worth doing when a real user edits library TypeScript under `dotnet watch`. Until then, a
build picks up the change.

### Work for A

- Integration test in `tests/Extrode.Pare.Integration/BuildIntegrationTests.cs`: RCL with
  `wwwroot/js/widget.ts` + web app with a `ProjectReference`; build, then check
  `obj/.../pare/js/widget.js` and the app's `staticwebassets.endpoints.json` for
  `_content/MyLib/js/widget.js`; publish, then check `wwwroot/_content/MyLib/js/widget.js` is
  stripped and no `.ts` was published. Break-check by setting `BasePath` to `/` in
  `_PareDefineStaticWebAssets`.
- README, MSBuild section: "Razor class libraries", with the reference (`PrivateAssets="all"`),
  the `/_content/<lib>/` URL, and the fact that the packed library contains only `.js`.
