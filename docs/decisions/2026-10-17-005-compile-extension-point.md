# 005 Extension point for other compile-to-JavaScript steps

Date: 2026-10-17. Status: accepted (the user). Plan:
[2026-10-17-005-compile-extension-point.md](../plans/2026-10-17-005-compile-extension-point.md).

Decision 004 promised one public MSBuild extension point through which another package (Husk,
for `.svelte`) runs its own compile step on Pare's plumbing. Its shape:

- **Sources in, Pare computes the paths.** The other package declares `PareCompileSource` items
  (`PareCompiler`, `PareOutputExtension`, optional `PareServeAt`) and adds its target to
  `PareCompileDependsOn`. Pare gives each item `PareOutputPath` and `PareRelativePath` by the same
  rules as a `.ts` (`wwwroot` path, `PareServeAt`, `PareEmbed`, `PareOutDir`), then registers,
  embeds, publishes and deletes the outputs. PARE201-203 cover both kinds of source; PARE204 is a
  malformed item. One output per item: a second output (a component's CSS) is a second item.
- **Pare's watcher, the other package plugs in.** `Extrode.Pare.AspNetCore` gains
  `IPareCompiler`, resolved from DI by name. The development map lists each compiled source with
  its compiler, one entry per output, under its own `compiled` key so that Pare 0.5's reader does
  not strip it. `CompileAsync` takes a `CancellationToken` from the start, since changing a public
  interface later breaks every implementer; Pare calls each compiler one source at a time.
  `AddPareDevelopment` is idempotent, so the other package's setup can call it; each call's
  options apply.

Rejected:

- Outputs in, the other package computes paths: a smaller change to Pare, but every user of the
  hook re-implements `PareServeAt` and the `wwwroot` rules and their errors, and drifts from them.
- A map-only runtime hook where the other package ships its own watcher: no new runtime API, but
  about 300 lines of watcher logic (settle, rename, Razor class library maps) duplicated.
- A metadata list of extensions (`PareOutputExtensions=".js;.css"`) on one item: MSBuild cannot
  split metadata into items cleanly; two items keep every output a plain item.
