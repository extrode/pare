# Handoff: what Rein's web UI needs from Pare

Date: 2026-10-17. From: Rein (`~/Developer/extrode.com/rein`), spec 020, plan
`docs/specs/020-rein-daily-driver/020-plan-web-ui.md` on branch `feat/020-web-ui`.
This file is here, not in the pare repo, because pare's tree had untracked files
(`samples/WebApp/wwwroot/js/*.js`) when it was written. Move it to `pare/docs/handoffs/` when
convenient.

## Context

Rein's web UI is moving from one C# string constant to TypeScript modules
(`erasableSyntaxOnly`), stripped by Pare during `dotnet build` and compiled into the NativeAOT
`rein` binary as embedded resources. Rein serves them itself from an `HttpListener`. It is
**not** an ASP.NET Core / Web SDK project: no static web assets, no `wwwroot`, no
`MapStaticAssets`. JS unit tests run inside xUnit on Jint (pure .NET, using Acornima, which Pare's
tests already use), with no Node, Bun or Deno.

## Needs

### N1. A public hook for stripped outputs in a non-Web project (required)

**Today:**
- `_PareOutput` is private.
- In a plain `Microsoft.NET.Sdk` project, outputs go next to the source, or under `PareOutDir`,
  and join `Content`.

**Rein needs** to embed them, in `obj/`, with a logical name that follows the source path.

**Acceptance:**
- After `PareStripTypeScript` runs, a public item `PareOutput` exists. Each item has metadata
  `PareSource` (full path) and `PareRelativePath` (path relative to the source root, `.js`
  extension).
- With `PareOutDir=$(IntermediateOutputPath)pare/` the outputs land there and never in the
  source tree.
- A consumer can write:

  ```xml
  <Target Name="EmbedPare" AfterTargets="PareStripTypeScript" BeforeTargets="PrepareForBuild;AssignTargetPaths">
    <ItemGroup>
      <EmbeddedResource Include="@(PareOutput)" LogicalName="web/%(PareOutput.PareRelativePath)" />
    </ItemGroup>
  </Target>
  ```

  and an incremental build (no `.ts` change) still embeds the same files.
- An alternative is a built-in switch: `PareEmbed=true` plus `PareEmbedPrefix=web/`.
  Either way, the behaviour should be documented in the README under "Non-web projects".
- Incremental and clean builds both work. A deleted `.ts` removes its embedded resource on the
  next build. Outputs are left out of `Content` when they are embedded.

### N2. A package Rein can restore on every machine (required)

**Today:** `Extrode.Pare.Build` exists only in `pare/tmp/feed` (gitignored, local to one
machine). Rein builds on mb1 (macOS), wlt1 (Windows) and Linux CI, and in Stryker sandboxes.

**Acceptance:** one of these, documented in the README:
- a published version on nuget.org, or a private feed with a URL;
- a pinned, versioned `.nupkg` that Rein can vendor under `rein/packages/` (a local feed in its
  `nuget.config`), restoring offline on macOS, Windows and Linux;
- `0.3.0` (non-dev) marked as the version to pin.

### N3. A stable in-memory strip API for tests (check; probably already met)

Rein's xUnit tests strip `.ts` at test time (so tests need no build-output path) and load the
result into Jint as ES modules.

**Acceptance:**
- `TypeStripper.Strip(ReadOnlySpan<char>, IBufferWriter<char>, IDiagnosticSink)` is public, is
  documented as stable for 0.x, and has a convenience overload `string? Strip(string source,
  out IReadOnlyList<Diagnostic> diagnostics)`, or a documented 5-line pattern.
- `Extrode.Pare` (the library alone, no MSBuild) is consumable as a package (same feed as N2).

### N4. Optional: module loading for a .NET JS engine

This is not required; Rein can write a 30-line Jint module loader. If Pare wants it, a tiny
`Extrode.Pare.Testing` package (or a sample) helps: resolve `./x.js` imports to `x.ts`, strip it
and return the source. **Acceptance:** a sample test that imports a two-module `.ts` graph into
Jint and calls an exported function.

### N5. Optional: type checking in CI without npm

`scripts/get-tsc.cs` exists in pare.

**Acceptance:** a README section showing a consumer repo using `PareTypeCheck=true` with
`PareTscPath` pointing at a `tsc` fetched by a copy of `get-tsc.cs`. Also state whether the
fetched binary is per-OS (Rein needs macOS arm64, Windows x64 and Linux x64).

## Minimal API sketch

```xml
<!-- Rein.csproj -->
<PropertyGroup>
  <PareOutDir>$(IntermediateOutputPath)pare/</PareOutDir>
  <EnablePareDefaultItems>false</EnablePareDefaultItems>
</PropertyGroup>
<ItemGroup>
  <PackageReference Include="Extrode.Pare.Build" Version="0.3.0" PrivateAssets="all" />
  <TypeScriptSource Include="web/**/*.ts" />
</ItemGroup>
<Target Name="EmbedPare" AfterTargets="PareStripTypeScript">
  <ItemGroup>
    <EmbeddedResource Include="@(PareOutput)" LogicalName="web/%(PareOutput.PareRelativePath)" />
  </ItemGroup>
</Target>
```

```csharp
// Rein.Tests
var js = PareStrip.ToString(File.ReadAllText("web/home.ts"), out var diags); // N3 convenience
Assert.Empty(diags);
```

## Not needed

JSX, bundling, minification, ASP.NET Core dev-loop features, source maps.
