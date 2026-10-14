using System.Diagnostics;
using Extrode.Pare.Build;

namespace Extrode.Pare.Tests.Build;

/// <summary>Runs `dotnet build` on a throwaway project that imports the package's props and targets.</summary>
public sealed class BuildIntegrationTests : IDisposable
{
    private static readonly string RepoRoot = FindRepoRoot();
    private static readonly string BuildDir = Path.Combine(RepoRoot, "src", "Extrode.Pare.Build", "build");
    private readonly DirectoryInfo _dir = Directory.CreateTempSubdirectory("pare-build-");

    public void Dispose() => _dir.Delete(recursive: true);

    private string P(string relative) => Path.Combine(_dir.FullName, relative);

    private void Put(string relative, string text)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(P(relative))!);
        File.WriteAllText(P(relative), text);
    }

    private void Project(string properties = "", string items = "", string sdk = "Microsoft.NET.Sdk", string targets = "") => Put("app.csproj", $"""
        <Project Sdk="{sdk}">
          <Import Project="{Path.Combine(BuildDir, "Extrode.Pare.Build.props")}" />
          <PropertyGroup>
            <TargetFramework>net10.0</TargetFramework>
            <PareTaskAssembly>{typeof(StripTypeScript).Assembly.Location}</PareTaskAssembly>
            {properties}
          </PropertyGroup>
          <ItemGroup>{items}</ItemGroup>
          <Import Project="{Path.Combine(BuildDir, "Extrode.Pare.Build.targets")}" />
          {targets}
        </Project>
        """);

    private (int Exit, string Output) Build(params string[] extra) =>
        Run(["build", "-nologo", "-nodeReuse:false", "--disable-build-servers", "-clp:NoSummary", .. extra]);

    private (int Exit, string Output) Run(params string[] args)
    {
        var start = new ProcessStartInfo("dotnet")
        {
            WorkingDirectory = _dir.FullName,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (var arg in args)
            start.ArgumentList.Add(arg);
        // A test host started by `dotnet test` carries MSBuild variables meant for itself.
        foreach (var key in start.Environment.Keys.Where(k => k.StartsWith("MSBuild", StringComparison.OrdinalIgnoreCase)).ToList())
            start.Environment.Remove(key);
        using var process = Process.Start(start)!;
        var stderr = process.StandardError.ReadToEndAsync(TestContext.Current.CancellationToken);
        string stdout = process.StandardOutput.ReadToEnd();
        process.WaitForExit();
        return (process.ExitCode, stdout + stderr.Result);
    }

    [Fact]
    public void Strips_wwwroot_sources_and_skips_them_when_up_to_date()
    {
        Project();
        Put("wwwroot/js/app.ts", "import { f } from './lib.ts';\nlet n: number = f();\n");
        Put("wwwroot/js/lib.ts", "export function f(): number { return 1; }\n");
        Put("wwwroot/js/types.d.ts", "declare let t: T;\n");
        Put("wwwroot/js/m.mts", "export let m: T;\n");

        var (exit, output) = Build();
        Assert.True(exit == 0, output);
        Assert.Equal("import { f } from './lib.js';\nlet n         = f();\n", File.ReadAllText(P("wwwroot/js/app.js")));
        Assert.Equal("export function f()         { return 1; }\n", File.ReadAllText(P("wwwroot/js/lib.js")));
        Assert.Equal("export let m   ;\n", File.ReadAllText(P("wwwroot/js/m.mjs")));
        Assert.False(File.Exists(P("wwwroot/js/types.d.js")));

        var stamp = File.GetLastWriteTimeUtc(P("wwwroot/js/app.js"));
        Assert.Equal(0, Build().Exit);
        Assert.Equal(stamp, File.GetLastWriteTimeUtc(P("wwwroot/js/app.js")));

        Put("wwwroot/js/lib.ts", "export function f(): string { return ''; }\n");
        Assert.Equal(0, Build().Exit);
        Assert.Equal("export function f()         { return ''; }\n", File.ReadAllText(P("wwwroot/js/lib.js")));
        Assert.Equal(stamp, File.GetLastWriteTimeUtc(P("wwwroot/js/app.js")));
    }

    [Fact]
    public void Outputs_become_static_web_assets_once_on_the_first_and_later_builds()
    {
        // The Web SDK globs wwwroot into Content at evaluation: absent on the first build, present
        // on the second, where a duplicate would be a second asset.
        Project("<OutputType>Library</OutputType>", sdk: "Microsoft.NET.Sdk.Web", targets: """
            <Target Name="Probe" AfterTargets="Build">
              <Message Importance="high" Text="asset: %(StaticWebAsset.RelativePath)" Condition="'@(StaticWebAsset)' != ''" />
            </Target>
            """);
        Put("wwwroot/app.ts", "let a: T;");

        for (int build = 0; build < 2; build++)
        {
            var (exit, output) = Build();
            Assert.True(exit == 0, output);
            Assert.Single(output.Split('\n'), line => line.Contains("asset: app#[.{fingerprint}]?.js", StringComparison.Ordinal) &&
                                                      !line.Contains(".js.gz", StringComparison.Ordinal));
        }
    }

    [Theory]
    [InlineData("", false)]
    [InlineData("<PareKeepSources>true</PareKeepSources>", true)]
    public void Sources_are_not_static_web_assets_unless_kept(string properties, bool kept)
    {
        Project("<OutputType>Library</OutputType>" + properties, sdk: "Microsoft.NET.Sdk.Web", targets: """
            <Target Name="Probe" AfterTargets="Build">
              <Message Importance="high" Text="asset: %(StaticWebAsset.RelativePath)" Condition="'@(StaticWebAsset)' != ''" />
            </Target>
            """);
        Put("wwwroot/app.ts", "let a: T;");
        Put("wwwroot/types.d.ts", "declare let t: T;");

        for (int build = 0; build < 2; build++)
        {
            var (exit, output) = Build();
            Assert.True(exit == 0, output);
            var assets = output.Split('\n').Where(line => line.Contains("asset: ", StringComparison.Ordinal)).ToList();
            Assert.Contains(assets, line => line.Contains("asset: app#[.{fingerprint}]?.js", StringComparison.Ordinal));
            Assert.Equal(kept, assets.Any(line => line.Contains("app#[.{fingerprint}]?.ts", StringComparison.Ordinal)));
            Assert.Equal(kept, assets.Any(line => line.Contains("types.d#[.{fingerprint}]?.ts", StringComparison.Ordinal)));
        }
    }

    [Fact]
    public void Rejected_syntax_fails_the_build_with_a_located_error()
    {
        Project();
        Put("wwwroot/app.ts", "let a = 1;\nenum E { A }\n");

        var (exit, output) = Build();

        Assert.NotEqual(0, exit);
        Assert.Contains(Path.Combine("wwwroot", "app.ts") + "(2,1): error PARE100: 'enum' generates runtime code", output);
        Assert.False(File.Exists(P("wwwroot/app.js")));
    }

    [Fact]
    public void Out_dir_keeps_the_relative_layout()
    {
        Project("<PareOutDir>gen</PareOutDir>");
        Put("wwwroot/js/app.mts", "let a: T;");

        var (exit, output) = Build();

        Assert.True(exit == 0, output);
        Assert.Equal("let a   ;", File.ReadAllText(P("gen/wwwroot/js/app.mjs")));
        Assert.False(File.Exists(P("wwwroot/js/app.mjs")));
    }

    [Fact]
    public void Type_check_runs_tsgo_before_stripping_and_fails_the_build_when_it_fails()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "the fake tsgo is a shell script");
        Put("tsgo", "#!/bin/sh\necho \"$@\" > args.txt\nexit $(cat exit-code.txt)\n");
        if (!OperatingSystem.IsWindows())
            File.SetUnixFileMode(P("tsgo"), UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        Project($"<PareTypeCheck>true</PareTypeCheck><PareTsgoPath>{P("tsgo")}</PareTsgoPath>");
        Put("wwwroot/app.ts", "let a: T;");

        Put("exit-code.txt", "0");
        var (exit, output) = Build();
        Assert.True(exit == 0, output);
        string args = File.ReadAllText(P("args.txt"));
        Assert.StartsWith("--noEmit -p /", args);
        Assert.EndsWith(Path.Combine(_dir.Name, "tsconfig.json") + "\n", args);

        File.Delete(P("wwwroot/app.js"));
        Put("exit-code.txt", "1");
        Assert.NotEqual(0, Build().Exit);
        Assert.False(File.Exists(P("wwwroot/app.js")));
    }

    [Fact]
    public void Custom_items_skip_declaration_files()
    {
        Project("<EnablePareDefaultItems>false</EnablePareDefaultItems>", """<TypeScriptSource Include="src/**/*.ts" />""");
        Put("src/a.ts", "let a: T;");
        Put("src/b.d.ts", "declare let b: T;");

        var (exit, output) = Build();

        Assert.True(exit == 0, output);
        Assert.Equal("let a   ;", File.ReadAllText(P("src/a.js")));
        Assert.False(File.Exists(P("src/b.d.js")));
    }

    [Fact]
    public void Default_items_can_be_turned_off()
    {
        Project("<EnablePareDefaultItems>false</EnablePareDefaultItems>");
        Put("wwwroot/app.ts", "let a: T;");

        var (exit, output) = Build();

        Assert.True(exit == 0, output);
        Assert.False(File.Exists(P("wwwroot/app.js")));
    }

    [Fact]
    public void Sample_app_builds_from_the_packed_package()
    {
        string sample = Path.Combine(RepoRoot, "samples", "WebApp");
        foreach (var file in Directory.EnumerateFiles(sample, "*", SearchOption.AllDirectories))
        {
            string relative = Path.GetRelativePath(sample, file);
            if (relative.Split(Path.DirectorySeparatorChar)[0] is "bin" or "obj" || relative == "nuget.config" ||
                Path.GetExtension(file) is ".js" or ".mjs")
                continue;
            Put(Path.Combine("app", relative), File.ReadAllText(file));
        }
        Put("app/nuget.config", $"""
            <configuration>
              <packageSources>
                <clear />
                <add key="feed" value="{P("feed")}" />
              </packageSources>
            </configuration>
            """);
#if DEBUG
        const string configuration = "Debug";
#else
        const string configuration = "Release";
#endif
        var (exit, output) = Run("pack", Path.Combine(RepoRoot, "src", "Extrode.Pare.Build"), "-nologo", "-nodeReuse:false", "--no-build", "-c", configuration,
            "-o", P("feed"), "-p:Version=0.1.0-test.1");
        Assert.True(exit == 0, output);

        // PublishAot would restore the ILCompiler package, which the private package folder lacks.
        (exit, output) = Run("build", P("app"), "-nologo", "-nodeReuse:false", "--disable-build-servers", "-p:PublishAot=false", $"-p:RestorePackagesPath={P("packages")}");

        Assert.True(exit == 0, output);
        Assert.Equal("import { plural,             } from \"./format.js\";", File.ReadLines(P("app/wwwroot/js/counter.js")).First());
        Assert.Contains("import { counter } from \"./counter.js\";", File.ReadAllText(P("app/wwwroot/js/app.js")));
        Assert.StartsWith("export function plural(n        , forms        )         {", File.ReadAllLines(P("app/wwwroot/js/format.js"))[2]);
        Assert.False(File.Exists(P("app/wwwroot/js/alpine.d.js")));
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Pare.slnx")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("Pare.slnx not found above the test output");
    }
}
