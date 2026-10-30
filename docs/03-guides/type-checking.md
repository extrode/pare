# Type checking in CI

Pare does not check types. A line like `let n: number = "hi"` strips without complaint. Type
checking is TypeScript's job, and TypeScript 7 ships a native `tsc` that needs no Node.

## Let the build run it

```xml
<PareTypeCheck>true</PareTypeCheck>
```

Before the strip, the build runs `tsc --noEmit -p tsconfig.json`. A type error fails the build.

## Get `tsc` without npm

`scripts/get-tsc.cs` in the Pare repository fetches the native `tsc` from the npm registry and
checks its sha512, without npm or Node. Copy it into your repo, **outside any project directory**
(a project compiles every `.cs` below it), and run it on each machine and in CI before the build:

```sh
dotnet run scripts/get-tsc.cs --dest .tools/typescript   # add .tools/ to .gitignore
```

Tell the build where it is:

```xml
<PropertyGroup>
  <PareTypeCheck>true</PareTypeCheck>
  <PareTscPath>$(MSBuildThisFileDirectory).tools/typescript/tsc</PareTscPath>
  <PareTscPath Condition="$([MSBuild]::IsOSPlatform('Windows'))">$(PareTscPath).exe</PareTscPath>
</PropertyGroup>
```

- The binary is per OS and CPU (`@typescript/typescript-<os>-<cpu>`: `darwin-arm64`, `win32-x64`,
  `linux-x64` and the other arm64/x64 pairs). Fetch it on each machine instead of committing it.
- Running it again with the same version does nothing. `--version <x.y.z>` picks another.
- The script refuses a `--dest` that is not empty and that it did not fill itself.
- `$(PareTsconfig)` defaults to the project's `tsconfig.json`. It is never copied to the build or
  publish output.

## A tsconfig that matches Pare

```json
{
  "compilerOptions": {
    "target": "esnext",
    "module": "esnext",
    "moduleResolution": "bundler",
    "lib": ["esnext", "dom", "dom.iterable"],
    "strict": true,
    "noEmit": true,
    "isolatedModules": true,
    "verbatimModuleSyntax": true,
    "erasableSyntaxOnly": true,
    "allowImportingTsExtensions": true
  },
  "include": ["wwwroot/js/**/*.ts"]
}
```

`erasableSyntaxOnly` makes the editor and `tsc` flag the same syntax Pare rejects.
`verbatimModuleSyntax` makes you write `import type` for types, which is what a stripper needs:
without type information Pare cannot tell that an imported name is only a type.

## Without the build

```sh
pare src --check-only    # syntax Pare cannot erase
tsc --noEmit             # types
```
