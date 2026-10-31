# Offline and vendored packages

If a machine cannot reach nuget.org, or you want the exact `.nupkg` files in your repo, vendor
them. Each [GitHub release](https://github.com/extrode/pare/releases) carries the packages.

Put them in a folder next to the solution:

```
packages/Extrode.Pare.0.7.2.nupkg
packages/Extrode.Pare.Build.0.7.2.nupkg
```

and add a `nuget.config` beside the solution (the path is relative to that file):

```xml
<configuration>
  <packageSources>
    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" />
    <add key="vendored" value="packages" />
  </packageSources>
  <packageSourceMapping>
    <packageSource key="nuget.org"><package pattern="*" /></packageSource>
    <packageSource key="vendored"><package pattern="Extrode.Pare*" /></packageSource>
  </packageSourceMapping>
</configuration>
```

Pin an exact release (`Version="0.7.2"`), not a `-dev` or `-*` version.

The CLI tool package works the same way: put the `Extrode.Pare.Cli.*.nupkg` files in one folder and
run `dotnet tool install -g Extrode.Pare.Cli --add-source ./packages`.

If your project uses a `nuget.config` that you do not want published, mark it so:
`<Content Update="nuget.config" CopyToPublishDirectory="Never" />`.
