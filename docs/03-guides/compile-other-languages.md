# Compiling other languages

For authors of build packages. Pare's plumbing, the part that puts output under `obj/`, serves it
as a static web asset, publishes it compressed, and deletes it with its source, works for any
compile-to-JavaScript step. [Husk](https://github.com/extrode/husk), which compiles Svelte
components, uses it. If you only write TypeScript you can skip this page.

## Declare the sources and your target

Usually in your package's `.props` and `.targets`:

```xml
<ItemGroup>
  <PareCompileSource Include="Components/**/*.svelte" PareCompiler="husk"
                     PareOutputExtension=".svelte.js" PareServeAt="/" />
</ItemGroup>
<PropertyGroup>
  <PareCompileDependsOn>$(PareCompileDependsOn);HuskCompile</PareCompileDependsOn>
</PropertyGroup>
<Target Name="HuskCompile" Inputs="@(PareCompileSource)" Outputs="@(PareCompileSource->'%(PareOutputPath)')">
  <!-- write each source's output to %(PareOutputPath) -->
</Target>
```

| Metadata | |
|---|---|
| `PareCompiler` | required: the compiler's name, the same as its `IPareCompiler.Name` |
| `PareOutputExtension` | required: the output's name is the source's name without its extension, plus this (`App.svelte` + `.svelte.js` = `App.svelte.js`) |
| `PareServeAt` | as for `TypeScriptSource` |

- Targets in `PareCompileDependsOn` run inside `PareStripTypeScript`, after the strip and before
  the outputs are registered. Each `@(PareCompileSource)` then has `PareOutputPath` (where to
  write; relative paths are below the project) and `PareRelativePath` (its URL or resource name).
  Skipping up-to-date sources is the target's job (`Inputs`/`Outputs`).
- Two outputs from one source (a component's `.js` and `.css`) are two items with the same
  `Include` and different extensions. Every file the target writes under `obj/` must be one of
  these outputs: the build deletes any other file there as a deleted source's output (a `.js.map`
  it was not told about, say).
- The sources leave `Content`, so a `.svelte` under `wwwroot` is not published. They are `Watch`
  items. `@(PareOutput)` lists their outputs, with `%(PareCompiler)`.
- `error PARE204` for a missing `PareCompiler` (letters, digits, `.`, `_`, `-`) or
  `PareOutputExtension`. PARE201, PARE202 and PARE203 apply as for TypeScript, across both kinds of
  source.

## Hot reload

On save in Development, `AddPareDevelopment()` hands each such source to the `IPareCompiler` with
its name, registered in DI:

```csharp
builder.Services.AddSingleton<IPareCompiler, SvelteCompiler>();
builder.AddPareDevelopment();   // a second call, from another package's setup, adds no watcher; its options apply too
```

`CompileAsync(sourcePath, source, outputPaths, logger, cancellationToken)` returns the text of each
output keyed by path, or null after logging its errors (the old outputs stay). Pare calls each
compiler one source at a time, writes what changed and deletes the outputs of a deleted source.
Sources out of date at start are compiled before the app serves. A source whose compiler is not
registered gets a warning at start and is not compiled.

The decision behind this is
decision 005 (`docs/decisions/` in the [repository](https://github.com/extrode/pare/tree/dev/docs/decisions)).
