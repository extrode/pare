# T4: Pare extension point for compile-to-JavaScript steps

## Context

Husk (Svelte for .NET) needs Pare's build plumbing for `.svelte` output: placement under `obj/`,
static web assets, embedded output, publish, stale-output deletion, and re-compile on save in
Development. Pare decision 004 says Pare gains **one public, documented MSBuild extension point**
for this, and no engine or Svelte code of its own. Husk's tasklist T4 is done when the hook is
released in a Pare version and documented.

The user chose (this session):
- **Sources in, Pare computes paths.** The producer declares sources; Pare applies its own URL and
  path rules and hands back `PareOutputPath`/`PareRelativePath`. The producer writes the file there.
- **Pare's watcher, the producer plugs in.** `Extrode.Pare.AspNetCore` gains a public compiler
  interface; the development map tags each source with its compiler.

Work happens in `../pare` on `feat/compile-extension-point` off `dev`. Pare's rules apply:
zero-package core, C# stays ASCII, tests first, no new package.

## Public surface (what gets documented)

MSBuild, in a producer's `.props`/`.targets` (Husk's), or a project file:

```xml
<ItemGroup>
  <PareCompileSource Include="Components/**/*.svelte" PareCompiler="husk"
                     PareOutputExtension=".svelte.js" PareServeAt="/" />
</ItemGroup>
<PropertyGroup>
  <PareCompileDependsOn>$(PareCompileDependsOn);HuskCompile</PareCompileDependsOn>
</PropertyGroup>
<Target Name="HuskCompile" Inputs="@(PareCompileSource)" Outputs="%(PareCompileSource.PareOutputPath)"> ... </Target>
```

- `PareCompileSource` item. Metadata in: `PareCompiler` (required, a name), `PareOutputExtension`
  (required, starts with `.`, no `/`; output name = `%(Filename)` + it, so `App.svelte` ->
  `App.svelte.js`), optional `PareServeAt`. A second output for one source (Svelte's CSS) is a
  second item with the same `Include` and another extension. (Singular instead of the
  `PareOutputExtensions` list in the earlier preview: MSBuild cannot split a metadata list into
  items cleanly, and two items keep every output a plain item.)
- Placement uses the same rules as `TypeScriptSource`: below `wwwroot` keeps its path; `PareServeAt`
  folder; `PareEmbed` names; `PareOutDir`; next to the source outside web projects. Same errors:
  PARE201 (two outputs, either kind, at one URL), PARE202 (unserved), PARE203 (bad folder).
  New **PARE204**: a `PareCompileSource` without `PareCompiler` or a valid `PareOutputExtension`.
- `PareCompileDependsOn` property: targets run inside `PareStripTypeScript`, after paths are
  computed and the strip ran, before registration. There, `@(PareCompileSource)` carries
  `PareOutputPath` and `PareRelativePath`. Incrementality is the producer's (its own
  `Inputs`/`Outputs`).
- After the build, `@(PareOutput)` lists compiled outputs too, with `PareSource`,
  `PareRelativePath` and `PareCompiler`.

Runtime, `Extrode.Pare.AspNetCore`:

```csharp
public interface IPareCompiler
{
    string Name { get; }   // matches PareCompiler
    // Every output path the build gave this source; returns text per output, or null after logging.
    IReadOnlyDictionary<string, string>? Compile(string sourcePath, string source,
        IReadOnlyList<string> outputPaths, ILogger logger);
}
```

Registered through DI (`services.AddSingleton<IPareCompiler, HuskCompiler>()`), so Husk's own
`AddHusk...()` can register and call `AddPareDevelopment()`. `AddPareDevelopment` becomes
idempotent (today a second call starts a second set of watchers).

## Changes

### `src/Extrode.Pare.Build/build/Extrode.Pare.Build.targets`
1. `_PareComputeOutputs`: `_PareSource` also takes `@(PareCompileSource)`; for those, `_PareJs` =
   `%(Filename)%(PareOutputExtension)` instead of the `.ts` -> `.js` rule. Every later path rule,
   PARE201/202/203, is shared unchanged. Add `_PareBadCompile` + PARE204. Then write the computed
   items back: `PareCompileSource Remove` + `Include="@(_PareSource)"` where `PareCompiler` is set.
2. `_PareStrip`: `Sources` and `Inputs`/`Outputs` filtered to items with no `PareCompiler`.
3. `PareStripTypeScript` `DependsOnTargets`: `...;_PareStrip;$(PareCompileDependsOn);_PareRegisterOutputs`.
4. `_PareRegisterOutputs`: `PareOutput` gets `PareCompiler`; `Content Remove="@(PareCompileSource)"`
   unless `PareKeepSources` (else `.svelte` under `wwwroot` publishes). Static web assets, embed,
   FileWrites, PARE200, stale deletion work as is, since they read `_PareSource`.
5. Development map: each `sources` entry gains `"compiler"` when set. `Watch` includes
   `@(PareCompileSource)`.
6. Header comment documents the new item, property and metadata.

### `src/Extrode.Pare.AspNetCore/`
- New `IPareCompiler.cs`.
- `PareDevelopmentExtensions.cs`: `ReadMap` reads `compiler`; mapped sources become
  source -> (outputs[], compiler). Service resolves `IEnumerable<IPareCompiler>`; a mapped source
  whose compiler is not registered logs one warning at start and is skipped. Idempotent via a
  marker service.
- `TypeScriptWatcher.cs`: `_mapped` holds outputs list + compiler. `Strip` branches: no compiler ->
  today's path; compiler -> `Compile`, then the existing write-if-changed and log per output
  (`Compiled X to Y`). Delete of a source removes all its outputs. Settle/rename/RCL handling reused.

### Docs and release
- `README.md` MSBuild section: "Other compilers" (item, property, metadata, runtime interface, a
  minimal producer example).
- `docs/error-codes.md`: PARE204; PARE201/202 wording made source-neutral ("two sources", not
  "two TypeScript sources") if `ErrorCodesDocTests` allows; otherwise keep text.
- `docs/decisions/2026-10-17-005-compile-extension-point.md`: the two choices and the rejected options.
- `work/todo.md`: remove the "Public MSBuild extension point" item; `CHANGELOG.md` `[0.6.0]`;
  `Directory.Build.props` `Version` 0.5.0 -> 0.6.0. Packing/publishing to NuGet and pushing stay
  the user's (Confirm-first).

## Tests (tests first, each break-checked by reverting the code with `git checkout --`)

Unit, `tests/Extrode.Pare.Tests/DevelopmentWatcherTests.cs` (existing `MappedWatcher` helper):
- a compiled source is compiled on start when its outputs are missing/older, and on save;
- two outputs from one compile both written; unchanged text writes nothing;
- compiler returns null -> old outputs stay; source deleted -> all outputs removed;
- unregistered compiler -> one warning, no write; TS sources in the same map still stripped;
- `AddPareDevelopment` called twice -> one hosted service.

Integration, `tests/Extrode.Pare.Integration/BuildIntegrationTests.cs`, with a fake producer in
the test project file (a `.fake` source copied to `%(PareOutputPath)` with a marker line):
- web project: output at `obj/.../pare/x.fake.js`, endpoint in `staticwebassets.endpoints.json`,
  published with `.br`/`.gz`, no `.fake` published;
- `PareServeAt`, `PareEmbed` (resource name), `PareOutDir` placement;
- PARE201 for a `.ts` and a `.fake` at one URL; PARE202; PARE204;
- stale deletion: removing a `.fake` removes its output and keeps other compiled outputs;
- development map lists the source with `"compiler": "fake"`;
- browser: the existing Playwright sample loads a compiled module that imports a stripped `.ts`
  module (Chromium, Firefox, WebKit).

## Verification

```sh
dotnet build Pare.slnx
dotnet test --solution Pare.slnx          # unit + integration (~2 min) + Playwright
deno run -A ~/.claude/tools/laws-check.js --run       # if docs/laws/ exists in pare
deno run -A ~/.claude/tools/structure-audit.js        # pare, then husk
```

Then `/code-review` on the branch, fix, merge `--no-ff` to pare `dev`, unpushed.

## Husk side (after the Pare merge)

Branch `chore/t4-done` in husk: tick T4 in `work/tasklist.md` (done-when "released" is met once the
user publishes 0.6.0; until then note "merged, 0.6.0 unpublished"), roll status, merge to `dev`.
