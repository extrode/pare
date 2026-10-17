# Plan 003: embedded output and a restorable package for Rein

Status: done, 2026-10-17, on `feat/embed-output`. Source:
[docs/handoffs/2026-10-17-rein-web-ui-needs.md](../handoffs/2026-10-17-rein-web-ui-needs.md).
Choices made by the user on 2026-10-17: both N1 shapes, 0.4.0 as vendored `.nupkg`, the string
overload with a 0.x stability note, N5 and N4.

## Steps

1. **`PareOutput` item (N1).** `PareStripTypeScript` becomes a target with no body that depends
   on the type check, the strip (`_PareStrip`, incremental) and `_PareRegisterOutputs`, so a
   consumer's `AfterTargets="PareStripTypeScript"` runs after the item exists, whatever the
   import order (a project body's targets are declared before a package's). Each `PareOutput`
   has `PareSource` (full path of the `.ts`) and `PareRelativePath`: the URL path for a static
   web asset, else the output's path relative to the project (or under `PareOutDir`), with `/`.
2. **`PareEmbed=true` (N1).** Every source goes to `$(IntermediateOutputPath)pare/` (or
   `PareOutDir`), at its path relative to the project (`PareServeAt`/`PareRelativePath` remap
   it), becomes `EmbeddedResource` with `LogicalName="$(PareEmbedPrefix)%(PareRelativePath)"`,
   never `Content` or a static web asset. The SDK's incremental clean deletes a deleted
   source's output (a Pare-side delete was redundant: break-check survived). A
   deleted source drops its resource: `_CoreCompileResourceInputs` is in the SDK's
   `CoreCompileInputs.cache`, so the set change recompiles (checked by test).
3. **String overload (N3).** `string? TypeStripper.Strip(string source, out IReadOnlyList<Diagnostic> diagnostics)`.
4. **`get-tsc.cs --dest` (N5)**, so a copy works in another repo; README section.
5. **Jint sample (N4).** Jint, pinned, in a test project: a two-module `.ts` graph imported
   through a module loader that strips on load; calls an exported function.
6. **0.4.0 (N2).** Version bump, CHANGELOG, pack `Extrode.Pare` and `Extrode.Pare.Build` into
   `tmp/dist-lib`; README "Non-web projects" and vendoring instructions. Tag and release stay
   the user's.
