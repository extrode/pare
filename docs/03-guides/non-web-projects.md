# Non-web projects

A console app, or a NativeAOT binary that serves its own UI, has no static web assets. Embed the
output in the assembly instead.

```xml
<PropertyGroup>
  <PareEmbed>true</PareEmbed>
  <PareEmbedPrefix>ui/</PareEmbedPrefix>
  <EnablePareDefaultItems>false</EnablePareDefaultItems>
</PropertyGroup>
<ItemGroup>
  <PackageReference Include="Extrode.Pare.Build" Version="0.6.3" PrivateAssets="all" />
  <TypeScriptSource Include="web/**/*.ts" />
</ItemGroup>
```

`web/lib/util.ts` is written to `obj/<configuration>/<tfm>/pare/web/lib/util.js`, never into the
source tree, and embedded as `ui/web/lib/util.js`. Read it back:

```csharp
using var stream = typeof(Program).Assembly.GetManifestResourceStream("ui/web/lib/util.js");
```

- The resource name is the prefix plus the path below the project. `PareServeAt` changes the path:
  `<TypeScriptSource Include="web/**/*.ts" PareServeAt="/" />` gives `ui/lib/util.js`.
- The outputs are not `Content`, not static web assets, and not copied to the build or publish
  output.
- A name such as `strings.de.ts` stays in the main assembly (no satellite assembly for `de`).
- The step is incremental: a `.ts` edit rewrites its `.js` and recompiles; a deleted `.ts` drops
  its resource and its `.js` on the next build.

## Do it your own way

To embed or copy the files differently, leave `PareEmbed` off and use the `@(PareOutput)` item:

```xml
<PropertyGroup>
  <PareOutDir>$(BaseIntermediateOutputPath)pare/</PareOutDir>
</PropertyGroup>
<Target Name="EmbedPare" AfterTargets="PareStripTypeScript">
  <ItemGroup>
    <EmbeddedResource Include="@(PareOutput)" LogicalName="ui/%(PareOutput.PareRelativePath)" />
  </ItemGroup>
</Target>
```

`$(IntermediateOutputPath)` is still empty where a project file sets its properties, so
`PareOutDir` cannot use it. `$(BaseIntermediateOutputPath)` (`obj/`) is set by then.

After `PareStripTypeScript`, `@(PareOutput)` lists every output. `%(PareSource)` is the source's
full path. `%(PareRelativePath)` uses `/`: the URL path for a static web asset (`js/app.js`), the
resource name without the prefix under `PareEmbed`, and otherwise the output's path below the
project (or below `PareOutDir`). A target with `AfterTargets="PareStripTypeScript"` sees it on
every build, including builds that skip the strip as up to date.
