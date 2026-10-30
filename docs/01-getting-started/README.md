# Getting started

Pick the way you want to use Pare. Each one takes about five minutes.

| I have... | Use | Start here |
|---|---|---|
| an ASP.NET Core app | the MSBuild package | [Your first web app](first-web-app.md) |
| a static site, or any non-.NET project | the `pare` command | [Your first static site](first-static-site.md) |
| C# code that wants to strip types | the library | [Using the library](../03-guides/library.md) |
| Rust, Go, Python or similar | the C library | [Using libpare from C and other languages](../03-guides/c-library.md) |

## Requirements

| To use | You need |
|---|---|
| the `pare` command from a release archive | nothing |
| the `pare` command as a .NET tool | the .NET 10 SDK (on platforms without a prebuilt binary, the tool runs on the .NET 10 runtime) |
| `Extrode.Pare.Build` or `Extrode.Pare.AspNetCore` | the .NET 10 SDK, a project that targets `net10.0` |
| Visual Studio | Visual Studio 2026 (MSBuild 18). Visual Studio 2022 cannot build `net10.0` projects. |
| `libpare` | a C compiler or an FFI in your language |

Pare builds on Windows, macOS and Linux. `dotnet build` works from the command line, Rider and VS
Code. The prebuilt binaries cover osx-arm64, osx-x64, linux-x64, linux-arm64, linux-musl-x64,
linux-musl-arm64, win-x64 and win-arm64.

## Install

**NuGet packages** (for .NET projects):

```sh
dotnet add package Extrode.Pare.Build
dotnet add package Extrode.Pare.AspNetCore
```

Open the project file afterwards and add `PrivateAssets="all"` to the `Extrode.Pare.Build`
reference, so projects that reference yours do not inherit the build step.

**The command line tool:**

Download `pare-<version>-<rid>.tar.gz` (`.zip` on Windows) from the
[releases page](https://github.com/extrode/pare/releases), unpack it and put the folder on your
`PATH`. It needs no .NET. The `Extrode.Pare.Cli` .NET tool package is not on nuget.org yet.

## Check that it works

```sh
echo 'const n: number = 1;' | pare --stdin
```

You should see the line back with the type replaced by spaces:

```
const n         = 1;
```
