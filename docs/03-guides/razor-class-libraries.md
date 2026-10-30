# Razor class libraries

Reference `Extrode.Pare.Build` from the library. Apps that use the library need nothing.

```xml
<PackageReference Include="Extrode.Pare.Build" Version="0.6.2" PrivateAssets="all" />
```

The library's `wwwroot/js/widget.ts` is then:

- served to apps as `/_content/<Library>/js/widget.js`;
- published with compressed copies;
- packed by `dotnet pack` as the stripped `.js` only, with no dependency on Pare. People who use
  your library never need to know Pare exists.

## Hot reload

When an app references the library as a project, `AddPareDevelopment()` in the app re-strips the
library's sources on save too. Each library's build output carries an
`Extrode.Pare.development.<Library>.json` that reaches the app's.
