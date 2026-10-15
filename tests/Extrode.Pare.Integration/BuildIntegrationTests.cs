using System.Diagnostics;

namespace Extrode.Pare.Integration;

/// <summary>Runs `dotnet build` on a throwaway project that imports the package's props and targets.</summary>
public sealed class BuildIntegrationTests : IDisposable
{
    private static readonly string RepoRoot = FindRepoRoot();
    private static readonly string BuildDir = Path.Combine(RepoRoot, "src", "Extrode.Pare.Build", "build");
    // By path: loading the task type would need the MSBuild assemblies, which only the host ships.
    private static readonly string TaskAssembly = Path.Combine(AppContext.BaseDirectory, "Extrode.Pare.Build.dll");

    // The sample references `<version>-*`, so test packages carry the repo's version.
    private static readonly string TestVersion = typeof(BuildIntegrationTests).Assembly.GetName().Version!.ToString(3) + "-test.1";

    private readonly DirectoryInfo _dir = Directory.CreateTempSubdirectory("pare-build-");
    private string? _pathPrefix;
    private string? _nugetPackages;

    // Windows keeps a killed app's .exe locked for a moment after the kill returns.
    public void Dispose()
    {
        var until = DateTime.UtcNow.AddSeconds(10);
        while (true)
        {
            try
            {
                _dir.Delete(recursive: true);
                return;
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException && DateTime.UtcNow < until)
            {
                Thread.Sleep(200);
            }
        }
    }

    private string P(string relative) => Path.Combine(_dir.FullName, relative);

    // Where a web project's build writes the output of wwwroot/<path>.ts.
    private const string ObjOut = "obj/Debug/net10.0/pare/";

    // The end of a full path MSBuild prints: it resolves links above the temp directory (/var on macOS).
    private string Tail(string relative) => Path.Combine(_dir.Name, relative).Replace('/', Path.DirectorySeparatorChar);

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
            <PareTaskAssembly>{TaskAssembly}</PareTaskAssembly>
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
        if (_pathPrefix is not null)
            start.Environment["PATH"] = _pathPrefix + Path.PathSeparator + start.Environment["PATH"];
        if (_nugetPackages is not null)
            start.Environment["NUGET_PACKAGES"] = _nugetPackages;
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
    public void Web_projects_strip_into_obj_and_leave_wwwroot_alone()
    {
        Project("<OutputType>Library</OutputType>", sdk: "Microsoft.NET.Sdk.Web");
        Put("wwwroot/js/app.ts", "import { f } from './lib.ts';\nlet n: number = f();\n");
        Put("wwwroot/js/lib.ts", "export function f(): number { return 1; }\n");
        Put("wwwroot/m.mts", "export let m: T;\n");

        var (exit, output) = Build();
        Assert.True(exit == 0, output);
        Assert.Equal("import { f } from './lib.js';\nlet n         = f();\n", File.ReadAllText(P(ObjOut + "js/app.js")));
        Assert.Equal("export let m   ;\n", File.ReadAllText(P(ObjOut + "m.mjs")));
        Assert.Equal(["app.ts", "lib.ts"], Directory.GetFiles(P("wwwroot/js")).Select(Path.GetFileName).Order());

        var stamp = File.GetLastWriteTimeUtc(P(ObjOut + "js/app.js"));
        Put("wwwroot/js/lib.ts", "export function f(): string { return ''; }\n");
        Assert.Equal(0, Build().Exit);
        Assert.Equal("export function f()         { return ''; }\n", File.ReadAllText(P(ObjOut + "js/lib.js")));
        Assert.Equal(stamp, File.GetLastWriteTimeUtc(P(ObjOut + "js/app.js")));

        // AddPareDevelopment reads where the outputs go from the build output.
        using var map = System.Text.Json.JsonDocument.Parse(File.ReadAllText(P("bin/Debug/net10.0/Extrode.Pare.development.json")));
        Assert.EndsWith(Tail("wwwroot/"), map.RootElement.GetProperty("sourceRoot").GetString());
        Assert.EndsWith(Tail(ObjOut), map.RootElement.GetProperty("outputRoot").GetString());
    }

    [Fact]
    public void Outputs_become_static_web_assets_once_on_the_first_and_later_builds()
    {
        // The Web SDK globs wwwroot into Content at evaluation: absent on the first build, present
        // on the second, where a duplicate would be a second asset.
        Project("<OutputType>Library</OutputType>", sdk: "Microsoft.NET.Sdk.Web", targets: """
            <Target Name="Probe" AfterTargets="Build">
              <Message Importance="high" Text="asset: %(StaticWebAsset.RelativePath) %(StaticWebAsset.Identity)" Condition="'@(StaticWebAsset)' != ''" />
            </Target>
            """);
        Put("wwwroot/app.ts", "let a: T;");

        for (int build = 0; build < 2; build++)
        {
            var (exit, output) = Build();
            Assert.True(exit == 0, output);
            string asset = Assert.Single(output.Split('\n'), line => line.Contains("asset: app#[.{fingerprint}]?.js ", StringComparison.Ordinal));
            Assert.EndsWith(Tail(ObjOut + "app.js"), asset.TrimEnd());
        }
    }

    private const string AssetProbe = """
        <Target Name="Probe" AfterTargets="Build">
          <Message Importance="high" Text="asset: %(StaticWebAsset.RelativePath) %(StaticWebAsset.Identity)" Condition="'@(StaticWebAsset)' != ''" />
        </Target>
        """;

    private static List<string> Assets(string output) =>
        [.. output.Split('\n').Where(line => line.Contains("asset: ", StringComparison.Ordinal) && !line.Contains(".gz", StringComparison.Ordinal))
            .Select(line => line[(line.IndexOf("asset: ", StringComparison.Ordinal) + 7)..].TrimEnd())];

    [Fact]
    public void A_js_left_next_to_its_source_is_reported_and_not_served()
    {
        Project("<OutputType>Library</OutputType>", sdk: "Microsoft.NET.Sdk.Web", targets: AssetProbe);
        Put("wwwroot/js/app.ts", "let a: T;");
        Put("wwwroot/js/app.js", "old;");
        Put("wwwroot/js/plain.js", "plain;");

        for (int build = 0; build < 2; build++)
        {
            var (exit, output) = Build();
            Assert.True(exit == 0, output);
            Assert.Contains(Path.Combine("wwwroot", "js", "app.js") + " : warning PARE200: ", output);
            var assets = Assets(output);
            string app = Assert.Single(assets, a => a.StartsWith("js/app", StringComparison.Ordinal));
            Assert.StartsWith("js/app#[.{fingerprint}]?.js ", app);
            Assert.EndsWith(Tail(ObjOut + "js/app.js"), app);
            Assert.Contains(assets, a => a.StartsWith("js/plain#[.{fingerprint}]?.js ", StringComparison.Ordinal));
        }
        Assert.Equal("old;", File.ReadAllText(P("wwwroot/js/app.js")));
    }

    [Fact]
    public void Deleting_a_source_removes_its_output()
    {
        Project("<OutputType>Library</OutputType>", sdk: "Microsoft.NET.Sdk.Web", targets: AssetProbe);
        Put("wwwroot/a.ts", "let a: T;");
        Put("wwwroot/js/b.ts", "let b: T;");
        Assert.Equal(0, Build().Exit);
        Assert.True(File.Exists(P(ObjOut + "js/b.js")));

        File.Delete(P("wwwroot/js/b.ts"));
        // Written by AddPareDevelopment for a source deleted before the next build, so not in the
        // build's file list, which the SDK's incremental clean works from.
        Put(ObjOut + "js/orphan.js", "orphan;");
        var (exit, output) = Build();
        Assert.True(exit == 0, output);
        Assert.False(File.Exists(P(ObjOut + "js/b.js")));
        Assert.False(File.Exists(P(ObjOut + "js/orphan.js")));
        Assert.True(File.Exists(P(ObjOut + "a.js")));
        Assert.DoesNotContain(Assets(output), a => a.StartsWith("js/b", StringComparison.Ordinal));

        File.Delete(P("wwwroot/a.ts"));
        (exit, output) = Build();
        Assert.True(exit == 0, output);
        Assert.False(File.Exists(P(ObjOut + "a.js")));
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

    [Theory]
    [InlineData("", "tsconfig.json")]
    [InlineData("<PareTsconfig>cfg/tsconfig.app.json</PareTsconfig>", "cfg/tsconfig.app.json")]
    public void Tsconfig_is_not_published(string properties, string tsconfig)
    {
        Project("<OutputType>Library</OutputType>" + properties, sdk: "Microsoft.NET.Sdk.Web");
        Put("wwwroot/app.ts", "let a: T;");
        Put(tsconfig, "{}");
        Put("other.json", "{}");

        var (exit, output) = Run("publish", "-nologo", "-nodeReuse:false", "--disable-build-servers", "-o", P("pub"));

        Assert.True(exit == 0, output);
        Assert.True(File.Exists(P("pub/wwwroot/app.js")), output);
        Assert.True(File.Exists(P("pub/other.json")), output);
        Assert.True(File.Exists(P("bin/Release/net10.0/other.json")), output);
        Assert.False(File.Exists(P("pub/" + tsconfig)));
        Assert.False(File.Exists(P("pub/Extrode.Pare.development.json")));
        Assert.False(File.Exists(P("bin/Release/net10.0/" + tsconfig)));
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

    [Theory]
    [InlineData("Microsoft.NET.Sdk")]
    [InlineData("Microsoft.NET.Sdk.Web")]
    public void Out_dir_keeps_the_relative_layout(string sdk)
    {
        Project("<OutputType>Library</OutputType><PareOutDir>gen</PareOutDir>", sdk: sdk, targets: AssetProbe);
        Put("wwwroot/js/app.mts", "let a: T;");

        var (exit, output) = Build();

        Assert.True(exit == 0, output);
        Assert.Equal("let a   ;", File.ReadAllText(P("gen/wwwroot/js/app.mjs")));
        Assert.False(File.Exists(P("wwwroot/js/app.mjs")));
        Assert.False(File.Exists(P(ObjOut + "js/app.mjs")));
        // Outside wwwroot, so the Web SDK does not serve it either: PareOutDir is for other tools.
        Assert.DoesNotContain(Assets(output), a => a.Contains("app", StringComparison.Ordinal));
    }

    private void FakeTsc(string relative)
    {
        Put(relative, "#!/bin/sh\necho \"$@\" > \"$(dirname \"$0\")/args.txt\"\nexit $(cat exit-code.txt)\n");
        if (!OperatingSystem.IsWindows())
            File.SetUnixFileMode(P(relative), UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
    }

    [Fact]
    public void Type_check_runs_tsc_before_stripping_and_fails_the_build_when_it_fails()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "the fake tsc is a shell script");
        FakeTsc("tools/tsc");
        Project($"<PareTypeCheck>true</PareTypeCheck><PareTscPath>{P("tools/tsc")}</PareTscPath>");
        Put("wwwroot/app.ts", "let a: T;");

        Put("exit-code.txt", "0");
        var (exit, output) = Build();
        Assert.True(exit == 0, output);
        string args = File.ReadAllText(P("tools/args.txt"));
        Assert.StartsWith("--noEmit -p /", args);
        Assert.EndsWith(Path.Combine(_dir.Name, "tsconfig.json") + "\n", args);

        File.Delete(P("wwwroot/app.js"));
        Put("exit-code.txt", "1");
        Assert.NotEqual(0, Build().Exit);
        Assert.False(File.Exists(P("wwwroot/app.js")));
    }

    [Fact]
    public void Type_check_defaults_to_tsc_on_PATH()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "the fake tsc is a shell script");
        FakeTsc("path/tsc");
        Project("<PareTypeCheck>true</PareTypeCheck>");
        Put("wwwroot/app.ts", "let a: T;");
        Put("exit-code.txt", "0");
        _pathPrefix = P("path");

        var (exit, output) = Build();

        Assert.True(exit == 0, output);
        Assert.StartsWith("--noEmit -p /", File.ReadAllText(P("path/args.txt")));
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
    public void Sources_are_watch_items()
    {
        Project(items: """<TypeScriptSource Include="src/a.ts" />""");
        Put("wwwroot/app.ts", "let a: T;");
        Put("src/a.ts", "let a: T;");

        var (exit, output) = Run("msbuild", "-nologo", "-getItem:Watch");

        Assert.True(exit == 0, output);
        // JSON output: a Windows path comes back with escaped backslashes.
        string items = output.Replace(@"\\", "/");
        Assert.Contains("wwwroot/app.ts", items);
        Assert.Contains("src/a.ts", items);
    }

    [Fact]
    public async Task Dotnet_watch_without_hot_reload_restrips_an_edited_source()
    {
        Project(sdk: "Microsoft.NET.Sdk.Web");
        Put("Program.cs", "System.Console.WriteLine(\"ready\");\nawait System.Threading.Tasks.Task.Delay(-1);\n");
        Put("wwwroot/app.ts", "let a: number = 1;");
        var start = new ProcessStartInfo("dotnet", ["watch", "--no-hot-reload", "--non-interactive"])
        {
            WorkingDirectory = _dir.FullName,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (var key in start.Environment.Keys.Where(k => k.StartsWith("MSBuild", StringComparison.OrdinalIgnoreCase)).ToList())
            start.Environment.Remove(key);
        var output = new System.Text.StringBuilder();
        int starts = 0;
        using var watch = new Process { StartInfo = start };
        watch.OutputDataReceived += (_, e) =>
        {
            lock (output)
                output.AppendLine(e.Data);
            if (e.Data == "ready")
                Interlocked.Increment(ref starts);
        };
        watch.ErrorDataReceived += (_, e) => { lock (output) output.AppendLine(e.Data); };
        watch.Start();
        watch.BeginOutputReadLine();
        watch.BeginErrorReadLine();
        try
        {
            async Task Until(Func<bool> done)
            {
                var deadline = DateTime.UtcNow.AddSeconds(120);
                while (!done())
                {
                    lock (output)
                        Assert.True(DateTime.UtcNow < deadline && !watch.HasExited, output.ToString());
                    await Task.Delay(200, TestContext.Current.CancellationToken);
                }
            }

            await Until(() => Volatile.Read(ref starts) == 1);
            Assert.Equal("let a         = 1;", File.ReadAllText(P(ObjOut + "app.js")));

            File.WriteAllText(P("wwwroot/app.ts"), "let b: string = \"edited\";");
            await Until(() => Volatile.Read(ref starts) == 2);
            Assert.Equal("let b         = \"edited\";", File.ReadAllText(P(ObjOut + "app.js")));
        }
        finally
        {
            watch.Kill(entireProcessTree: true);
            watch.WaitForExit();
        }
    }

    [Fact]
    public void Package_carries_the_license_notice_and_readme()
    {
#if DEBUG
        const string configuration = "Debug";
#else
        const string configuration = "Release";
#endif
        var (exit, output) = Run("pack", Path.Combine(RepoRoot, "src", "Extrode.Pare.Build"), "-nologo", "-nodeReuse:false", "--no-build",
            "-c", configuration, "-o", P("feed"), $"-p:Version={TestVersion}");
        Assert.True(exit == 0, output);

        using var package = System.IO.Compression.ZipFile.OpenRead(P($"feed/Extrode.Pare.Build.{TestVersion}.nupkg"));
        var entries = package.Entries.Select(e => e.FullName).ToList();
        Assert.Contains("LICENSE.md", entries);
        Assert.Contains("NOTICE.md", entries);
        Assert.Contains("README.md", entries);
        using var nuspec = new StreamReader(package.GetEntry("Extrode.Pare.Build.nuspec")!.Open());
        string text = nuspec.ReadToEnd();
        Assert.Contains("<license type=\"file\">LICENSE.md</license>", text);
        Assert.Contains("<readme>README.md</readme>", text);
        Assert.Contains("<authors>Extrode</authors>", text);
        using var license = new StreamReader(package.GetEntry("LICENSE.md")!.Open());
        Assert.Contains("means **`Extrode (extrode.com)`**", license.ReadToEnd());
    }

#if DEBUG
    private const string Configuration = "Debug";
#else
    private const string Configuration = "Release";
#endif

    /// <summary>Copies samples/WebApp to app/ and packs the package into a private feed it restores from.</summary>
    private void PrepareSample()
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
        foreach (string project in new[] { "Extrode.Pare", "Extrode.Pare.Build", "Extrode.Pare.AspNetCore" })
        {
            var (exit, output) = Run("pack", Path.Combine(RepoRoot, "src", project), "-nologo", "-nodeReuse:false", "--no-build", "-c", Configuration,
                "-o", P("feed"), $"-p:Version={TestVersion}");
            Assert.True(exit == 0, output);
        }
    }

    // PublishAot would restore the ILCompiler package, which the private package folder lacks.
    private (int Exit, string Output) SampleCommand(string command, params string[] extra) =>
        Run([command, P("app"), "-nologo", "-nodeReuse:false", "--disable-build-servers", "-p:PublishAot=false", $"-p:RestorePackagesPath={P("packages")}", .. extra]);

    private void BuildSample()
    {
        PrepareSample();
        var (exit, output) = SampleCommand("build");
        Assert.True(exit == 0, output);
        Assert.Equal("import { plural,             } from \"./format.js\";", File.ReadLines(P("app/" + ObjOut + "js/counter.js")).First());
        Assert.Contains("import { counter } from \"./counter.js\";", File.ReadAllText(P("app/" + ObjOut + "js/app.js")));
        Assert.StartsWith("export function plural(n        , forms        )         {", File.ReadAllLines(P("app/" + ObjOut + "js/format.js"))[2]);
        Assert.False(File.Exists(P("app/" + ObjOut + "js/alpine.d.js")));
        Assert.Empty(Directory.GetFiles(P("app/wwwroot"), "*.js", SearchOption.AllDirectories));
    }

    /// <summary>Starts the app on a free port and waits until it answers.</summary>
    private static async Task<(Process Server, HttpClient Http)> Serve(string app, string environment)
    {
        int port;
        using (var probe = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0))
        {
            probe.Start();
            port = ((System.Net.IPEndPoint)probe.LocalEndpoint).Port;
        }
        var start = new ProcessStartInfo(app.EndsWith(".dll", StringComparison.Ordinal) ? "dotnet" : app, app.EndsWith(".dll", StringComparison.Ordinal) ? [app] : [])
        {
            WorkingDirectory = Path.GetDirectoryName(app)!,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        start.Environment["ASPNETCORE_URLS"] = $"http://127.0.0.1:{port}";
        start.Environment["ASPNETCORE_ENVIRONMENT"] = environment;
        var server = Process.Start(start)!;
        server.OutputDataReceived += (_, _) => { };
        server.ErrorDataReceived += (_, _) => { };
        server.BeginOutputReadLine();
        server.BeginErrorReadLine();
        // Browsers send Accept-Encoding; MapStaticAssets answers with the build's compressed copies.
        var http = new HttpClient(new HttpClientHandler { AutomaticDecompression = System.Net.DecompressionMethods.All })
        {
            BaseAddress = new Uri($"http://127.0.0.1:{port}"),
        };
        var ct = TestContext.Current.CancellationToken;
        for (int i = 0; ; i++)
        {
            try
            {
                using var _ = await http.GetAsync("/fragments/time", ct);
                break;
            }
            catch (HttpRequestException) when (i < 100)
            {
                await Task.Delay(100, ct);
            }
        }
        return (server, http);
    }

    private static void Stop(Process server)
    {
        server.Kill(entireProcessTree: true);
        server.WaitForExit();
        server.Dispose();
    }

    public static TheoryData<string> Browsers => ["chromium", "firefox", "webkit"];

    /// <summary>Clicks through the page in one browser engine; fails on any console or page error.</summary>
    private static async Task ClickThrough(string engine, Uri site)
    {
        // Downloads the browser on first run; Alpine and htmx come from jsDelivr.
        Assert.Equal(0, Microsoft.Playwright.Program.Main(engine == "chromium" ? ["install", "chromium", "--only-shell"] : ["install", engine]));
        using var playwright = await Microsoft.Playwright.Playwright.CreateAsync();
        await using var browser = await playwright[engine].LaunchAsync();
        var page = await browser.NewPageAsync();
        var errors = new List<string>();
        page.Console += (_, m) => { if (m.Type == "error") errors.Add(m.Text); };
        page.PageError += (_, e) => errors.Add(e);
        var failed = new List<string>();
        page.RequestFailed += (_, r) => failed.Add(r.Url);
        page.Response += (_, r) => { if (r.Status >= 400) failed.Add($"{r.Status} {r.Url}"); };
        await page.GotoAsync(site.ToString());

        var counter = page.Locator("[x-data] span");
        var button = page.Locator("[x-data] button");
        await Microsoft.Playwright.Assertions.Expect(counter).ToHaveTextAsync("0 clicks");
        await button.ClickAsync();
        await Microsoft.Playwright.Assertions.Expect(counter).ToHaveTextAsync("1 click");
        await button.ClickAsync();
        await Microsoft.Playwright.Assertions.Expect(counter).ToHaveTextAsync("2 clicks");
        await page.GetByText("Server time").ClickAsync();
        await Microsoft.Playwright.Assertions.Expect(page.Locator("#time")).ToHaveTextAsync(
            new System.Text.RegularExpressions.Regex(@"^\d\d:\d\d:\d\d UTC$"));

        // The modules the browser loaded are Pare's output, at the TypeScript positions.
        string format = await page.EvaluateAsync<string>("() => import('/js/format.js').then(m => m.plural.toString())");
        Assert.StartsWith("function plural(n        , forms        )         {", format);
        Assert.Equal("3 clicks", await page.EvaluateAsync<string>(
            "() => import('/js/counter.js').then(m => { const c = m.counter(2); c.increment(); return c.label(); })"));
        Assert.Empty(errors);
        Assert.Empty(failed);
    }

    [Fact]
    public async Task Dotnet_watch_hot_reload_refreshes_the_browser_with_the_restripped_source()
    {
        PrepareSample();
        // dotnet watch takes no -p. The sample keeps PublishAot, whose ILCompiler package comes from the user's cache.
        string cache = Environment.GetEnvironmentVariable("NUGET_PACKAGES") ??
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".nuget", "packages");
        Put("app/Directory.Build.props", $"""
            <Project>
              <PropertyGroup>
                <RestorePackagesPath>{P("packages")}</RestorePackagesPath>
                <RestoreAdditionalProjectFallbackFolders>{cache}</RestoreAdditionalProjectFallbackFolders>
              </PropertyGroup>
            </Project>
            """);
        int port;
        using (var probe = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0))
        {
            probe.Start();
            port = ((System.Net.IPEndPoint)probe.LocalEndpoint).Port;
        }
        // The sample's launch profile uses port 5180.
        var start = new ProcessStartInfo("dotnet", ["watch", "--non-interactive", "--no-launch-profile"])
        {
            WorkingDirectory = P("app"),
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (var key in start.Environment.Keys.Where(k => k.StartsWith("MSBuild", StringComparison.OrdinalIgnoreCase)).ToList())
            start.Environment.Remove(key);
        start.Environment["ASPNETCORE_URLS"] = $"http://127.0.0.1:{port}";
        start.Environment["ASPNETCORE_ENVIRONMENT"] = "Development";
        var output = new System.Text.StringBuilder();
        using var watch = new Process { StartInfo = start };
        watch.OutputDataReceived += (_, e) => { lock (output) output.AppendLine(e.Data); };
        watch.ErrorDataReceived += (_, e) => { lock (output) output.AppendLine(e.Data); };
        watch.Start();
        watch.BeginOutputReadLine();
        watch.BeginErrorReadLine();
        string Output() { lock (output) return output.ToString(); }
        try
        {
            var ct = TestContext.Current.CancellationToken;
            using var http = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{port}") };
            var deadline = DateTime.UtcNow.AddSeconds(180);
            while (true)
            {
                Assert.True(DateTime.UtcNow < deadline && !watch.HasExited, Output());
                try
                {
                    using var _ = await http.GetAsync("/fragments/time", ct);
                    break;
                }
                catch (HttpRequestException)
                {
                    await Task.Delay(200, ct);
                }
            }

            Assert.Equal(0, Microsoft.Playwright.Program.Main(["install", "chromium", "--only-shell"]));
            using var playwright = await Microsoft.Playwright.Playwright.CreateAsync();
            await using var browser = await playwright.Chromium.LaunchAsync();
            var page = await browser.NewPageAsync();
            // An edit before the refresh socket opens never reaches the page, and the refresh script
            // connects 500 ms after load without saying so.
            await page.AddInitScriptAsync("""
                const Native = WebSocket;
                window.WebSocket = class extends Native {
                  constructor(...args) { super(...args); this.addEventListener('open', () => window.pareSocketOpen = true); }
                };
                """);
            await page.GotoAsync(http.BaseAddress.ToString());
            await page.WaitForFunctionAsync("() => window.pareSocketOpen === true");
            var counter = page.Locator("[x-data] span");
            await Microsoft.Playwright.Assertions.Expect(counter).ToHaveTextAsync("0 clicks");
            string built = Output();

            string source = P("app/wwwroot/js/counter.ts");
            File.WriteAllText(source, File.ReadAllText(source).Replace("other: \"clicks\"", "other: \"taps\""));
            // No reload from the test: dotnet watch refreshes the page itself.
            await Microsoft.Playwright.Assertions.Expect(counter).ToHaveTextAsync("0 taps", new() { Timeout = 30_000 });
            Assert.Contains("const clicks = { one: \"click\", other: \"taps\" }                 ;", File.ReadAllText(P("app/" + ObjOut + "js/counter.js")));
            Assert.Empty(Directory.GetFiles(P("app/wwwroot"), "*.js", SearchOption.AllDirectories));
            string after = Output()[built.Length..];
            Assert.Contains(("Stripped wwwroot/js/counter.ts to " + ObjOut + "js/counter.js").Replace('/', Path.DirectorySeparatorChar), after);
            Assert.Contains("Static asset changes applied", after);
            Assert.DoesNotContain("Restarting", after);
        }
        finally
        {
            watch.Kill(entireProcessTree: true);
            watch.WaitForExit();
        }
    }

    [Theory]
    [MemberData(nameof(Browsers))]
    public async Task Sample_app_builds_serves_and_runs_in_a_browser(string engine)
    {
        BuildSample();
        var (server, http) = await Serve(P("app/bin/Debug/net10.0/WebApp.dll"), "Development");
        try
        {
            var ct = TestContext.Current.CancellationToken;
            Assert.Contains("x-data=\"counter(0)\"", await http.GetStringAsync("/", ct));
            Assert.Contains("import { counter } from \"./counter.js\";", await http.GetStringAsync("/js/app.js", ct));
            await ClickThrough(engine, http.BaseAddress!);
        }
        finally
        {
            Stop(server);
            http.Dispose();
        }
    }

    [Fact]
    public async Task Published_sample_serves_only_javascript_compressed_and_runs_in_every_browser()
    {
        PrepareSample();
        var (exit, output) = SampleCommand("publish", "-c", "Release", "-o", P("pub"));
        Assert.True(exit == 0, output);
        var published = Directory.EnumerateFiles(P("pub/wwwroot"), "*", SearchOption.AllDirectories)
            .Select(f => Path.GetRelativePath(P("pub/wwwroot"), f).Replace('\\', '/')).Order().ToList();
        Assert.Contains("js/app.js", published);
        Assert.Contains("js/counter.js.br", published);
        Assert.Contains("js/format.js.gz", published);
        Assert.DoesNotContain(published, f => f.EndsWith(".ts", StringComparison.Ordinal) || f.Contains(".ts.", StringComparison.Ordinal));
        Assert.False(File.Exists(P("pub/tsconfig.json")));
        // The compressed copies are of Pare's output, not of the source.
        foreach (string js in published.Where(f => f.EndsWith(".js", StringComparison.Ordinal)))
        {
            byte[] plain = File.ReadAllBytes(P("pub/wwwroot/" + js));
            using (var gz = new System.IO.Compression.GZipStream(File.OpenRead(P("pub/wwwroot/" + js + ".gz")), System.IO.Compression.CompressionMode.Decompress))
                Assert.Equal(plain, ReadAll(gz));
            using (var br = new System.IO.Compression.BrotliStream(File.OpenRead(P("pub/wwwroot/" + js + ".br")), System.IO.Compression.CompressionMode.Decompress))
                Assert.Equal(plain, ReadAll(br));
        }

        var (server, http) = await Serve(P("pub/WebApp.dll"), "Production");
        try
        {
            var ct = TestContext.Current.CancellationToken;
            foreach (string source in (string[])["/js/app.ts", "/js/counter.ts", "/js/alpine.d.ts", "/tsconfig.json"])
            {
                using var missing = await http.GetAsync(source, ct);
                Assert.Equal(System.Net.HttpStatusCode.NotFound, missing.StatusCode);
            }
            using (var raw = new HttpClient { BaseAddress = http.BaseAddress })
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, "/js/counter.js");
                request.Headers.AcceptEncoding.ParseAdd("br");
                using var response = await raw.SendAsync(request, ct);
                Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
                Assert.Equal("br", Assert.Single(response.Content.Headers.ContentEncoding));
                Assert.Equal("text/javascript", response.Content.Headers.ContentType?.MediaType);
            }
            foreach (string engine in Browsers.Select(b => (string)b.Data))
                await ClickThrough(engine, http.BaseAddress!);
        }
        finally
        {
            Stop(server);
            http.Dispose();
        }
    }

    private static byte[] ReadAll(Stream stream)
    {
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        return buffer.ToArray();
    }

    [Fact]
    public async Task NativeAot_published_sample_runs_in_every_browser()
    {
        PrepareSample();
        // ILCompiler comes from the user's NuGet cache (read-only fallback) or nuget.org; only the
        // package under test goes to the private folder.
        Put("app/nuget.config", $"""
            <configuration>
              <packageSources>
                <clear />
                <add key="feed" value="{P("feed")}" />
                <add key="nuget.org" value="https://api.nuget.org/v3/index.json" />
              </packageSources>
              <packageSourceMapping>
                <packageSource key="feed"><package pattern="Extrode.*" /></packageSource>
                <packageSource key="nuget.org"><package pattern="*" /></packageSource>
              </packageSourceMapping>
            </configuration>
            """);
        string cache = Environment.GetEnvironmentVariable("NUGET_PACKAGES") ??
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".nuget", "packages");
        string rid = System.Runtime.InteropServices.RuntimeInformation.RuntimeIdentifier;
        var (exit, output) = Run("publish", P("app"), "-nologo", "-nodeReuse:false", "--disable-build-servers", "-c", "Release", "-r", rid,
            "-o", P("aot"), $"-p:RestorePackagesPath={P("packages")}", $"-p:RestoreAdditionalProjectFallbackFolders={cache}");
        Assert.True(exit == 0, output);
        Assert.DoesNotContain("warning IL", output);
        string binary = P(OperatingSystem.IsWindows() ? "aot/WebApp.exe" : "aot/WebApp");
        Assert.True(File.Exists(binary), output);
        Assert.False(File.Exists(P("aot/WebApp.dll")));

        var (server, http) = await Serve(binary, "Production");
        try
        {
            using var missing = await http.GetAsync("/js/counter.ts", TestContext.Current.CancellationToken);
            Assert.Equal(System.Net.HttpStatusCode.NotFound, missing.StatusCode);
            foreach (string engine in Browsers.Select(b => (string)b.Data))
                await ClickThrough(engine, http.BaseAddress!);
        }
        finally
        {
            Stop(server);
            http.Dispose();
        }
    }

    [Theory]
    [InlineData("node", new[] { "--input-type=module", "-e" })]
    [InlineData("deno", new[] { "eval" })]
    public void Sample_modules_run_outside_the_browser(string runtime, string[] evaluate)
    {
        var where = Environment.GetEnvironmentVariable("PATH")!.Split(Path.PathSeparator)
            .Select(d => Path.Combine(d, OperatingSystem.IsWindows() ? runtime + ".exe" : runtime)).FirstOrDefault(File.Exists);
        Assert.SkipWhen(where is null, $"{runtime} is not on PATH");
        BuildSample();
        // A package.json further up can say "commonjs" (one in a Windows %TEMP% did).
        Put("app/" + ObjOut + "package.json", """{ "type": "module" }""");
        string counter = new Uri(P("app/" + ObjOut + "js/counter.js")).AbsoluteUri;
        var start = new ProcessStartInfo(where!) { RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (string arg in evaluate)
            start.ArgumentList.Add(arg);
        start.ArgumentList.Add($$"""
            const { counter } = await import("{{counter}}");
            const c = counter();
            const seen = [c.label()];
            c.increment(); seen.push(c.label());
            c.increment(); seen.push(c.label());
            console.log(seen.join("|"));
            """);
        start.Environment["NO_COLOR"] = "1";
        using var process = Process.Start(start)!;
        string stderr = process.StandardError.ReadToEnd();
        string stdout = process.StandardOutput.ReadToEnd();
        process.WaitForExit();
        Assert.True(process.ExitCode == 0, stderr);
        Assert.Equal("0 clicks|1 click|2 clicks", stdout.Trim());
    }

    [Fact]
    public void Cli_tool_installs_the_native_binary_for_this_platform()
    {
        string rid = System.Runtime.InteropServices.RuntimeInformation.RuntimeIdentifier;
        string cli = Path.Combine(RepoRoot, "src", "Extrode.Pare.Cli");
        foreach (string[] target in new[] { Array.Empty<string>(), ["-r", rid], ["-r", "any"] })
        {
            var (packed, packOutput) = Run(["pack", cli, "-nologo", "-nodeReuse:false", "-c", Configuration, "-o", P("feed"), $"-p:Version={TestVersion}", .. target]);
            Assert.True(packed == 0, packOutput);
        }

        // A private package folder, so the fixed test version never lands in the user's cache.
        _nugetPackages = P("packages");
        var (exit, output) = Run("tool", "install", "Extrode.Pare.Cli", "--version", TestVersion, "--tool-path", P("tool"), "--add-source", P("feed"));
        Assert.True(exit == 0, output);
        string store = P($"tool/.store/extrode.pare.cli/{TestVersion}");
        Assert.Equal(["extrode.pare.cli", "extrode.pare.cli." + rid],
            Directory.GetDirectories(store).Select(Path.GetFileName).Order());
        Assert.Empty(Directory.GetFiles(store, "pare.dll", SearchOption.AllDirectories));

        // On Windows the SDK puts a pare.cmd shim on the tool path for a native tool.
        var start = new ProcessStartInfo(P(OperatingSystem.IsWindows() ? "tool/pare.cmd" : "tool/pare"), "--stdin")
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
        };
        using var pare = Process.Start(start)!;
        pare.StandardInput.Write("type T = 1;\nlet x: number = 1;\n");
        pare.StandardInput.Close();
        string stripped = pare.StandardOutput.ReadToEnd();
        pare.WaitForExit();
        Assert.Equal(0, pare.ExitCode);
        Assert.Equal("           \nlet x         = 1;\n", stripped);
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Pare.slnx")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("Pare.slnx not found above the test output");
    }
}
