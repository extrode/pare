# MSBuild properties

Set these in the project file. The package is `Extrode.Pare.Build`. The task runs in-process under
`dotnet build`, and in MSBuild's .NET task host (a separate process) under Visual Studio 2026.

## Properties

| Property | Default | |
|---|---|---|
| `EnablePareDefaultItems` | `true` | `false` drops the default glob; add `<TypeScriptSource Include="..." />` yourself |
| `PareOutDir` | empty | write under this directory, keeping relative paths, instead of `obj/` or next to each source; not served, `.razor.js` and `.cshtml.js` included |
| `PareTypeCheck` | `false` | `true` runs `tsc --noEmit -p $(PareTsconfig)` first (TypeScript 7's native `tsc`) |
| `PareTscPath` | `tsc` | for example `$(MSBuildThisFileDirectory).tools/typescript/tsc` |
| `PareTsconfig` | `tsconfig.json` in the project | never copied to the build or publish output |
| `PareKeepSources` | `false` | `true` serves and publishes the `.ts` sources next to the output |
| `PareEmbed` | `false` | `true` embeds every output in the assembly instead; see [Non-web projects](../03-guides/non-web-projects.md) |
| `PareEmbedPrefix` | empty | prepended to each embedded resource's name, for example `web/` |

## Item metadata

On `TypeScriptSource` and `PareCompileSource` items:

| Metadata | |
|---|---|
| `PareServeAt` | the URL folder to serve this source under; see [TypeScript outside wwwroot](../03-guides/typescript-outside-wwwroot.md) |
| `PareCompiler` | `PareCompileSource` only: the compiler's name |
| `PareOutputExtension` | `PareCompileSource` only: the output's extension, such as `.svelte.js` |

## Items you can read

After the `PareStripTypeScript` target, `@(PareOutput)` lists every output. Each has
`%(PareSource)` (the source's full path) and `%(PareRelativePath)`, written with `/`:

- the URL path for a static web asset (`js/app.js`),
- the resource name without the prefix under `PareEmbed`,
- otherwise the output's path below the project (or below `PareOutDir`).

A target with `AfterTargets="PareStripTypeScript"` sees it on every build, including builds that
skip the strip as up to date.

## What gets stripped

Before `Build`, every `wwwroot/**/*.ts` and `*.mts` (not `.d.ts`, not `node_modules`) is stripped.
`.cts` is not in the default glob, since CommonJS does not run in a browser. An explicit
`<TypeScriptSource Include="x.cts" />` produces `x.cjs`.

A file listed twice as `TypeScriptSource` (the default glob and your own item) is one source with the
last declaration's metadata, so `<TypeScriptSource Include="wwwroot/js/a.ts" PareServeAt="lib" />`
in the project file wins over the default. On Windows `wwwroot/js/a.ts` and `wwwroot\js\a.ts` name
the same file; on Linux, `a.ts` and `A.ts` are two files.

## Through another package

From 0.7.2, the props and targets are also under `buildTransitive/`, so a
package that depends on `Extrode.Pare.Build` can hand it on to the projects that use it. That
package references Pare without `PrivateAssets="all"`:

```xml
<PackageReference Include="Extrode.Pare.Build" PrivateAssets="contentfiles;analyzers;build" />
```

Pare is a development dependency, so a plain `PackageReference` stops at the first project and
nothing flows on. Referencing it both directly and through such a package imports it once.

## Where the output goes

| Situation | Output |
|---|---|
| web project, source in `wwwroot` | `obj/<configuration>/<tfm>/pare/js/app.js`, registered as the static web asset `js/app.js` |
| web project, source elsewhere, with `PareServeAt` | the same place under `obj/`, served at the URL you chose |
| source elsewhere, no `PareServeAt` | the `.js` next to its source (`warning PARE202`) |
| `PareOutDir` set | below `PareOutDir`, not served |
| `PareEmbed` true | below `obj/`, embedded in the assembly |

Other details:

- `.ts` and `.d.ts` files are removed from `Content`, so they are not static web assets and are not
  published. `PareKeepSources=true` keeps them.
- In a web project without `PareOutDir`, the build deletes the output of a deleted source. Output
  written next to the source (a non-web project) or under `PareOutDir` stays; delete it yourself.
- A `foo.js` next to `foo.ts` in `wwwroot` (left by Pare 0.2, or hand-written) is not served;
  `warning PARE200` names it. Delete it.
- Outside `obj/`, an existing `foo.js` next to `foo.ts` is replaced on the next build that strips
  `foo.ts`.
- Rejections fail the build as `file(line,col): error PARE100: ...`.
