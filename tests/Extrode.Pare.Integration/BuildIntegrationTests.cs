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

    private (int Exit, string Output) Run(params string[] args) => Exec("dotnet", args);

    private (int Exit, string Output) Exec(string fileName, params string[] args)
    {
        var start = new ProcessStartInfo(fileName)
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

    [Fact]
    public void Razor_class_library_sources_publish_stripped_under_content()
    {
        Put("Lib/Lib.csproj", $"""
            <Project Sdk="Microsoft.NET.Sdk.Razor">
              <Import Project="{Path.Combine(BuildDir, "Extrode.Pare.Build.props")}" />
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
                <PareTaskAssembly>{TaskAssembly}</PareTaskAssembly>
              </PropertyGroup>
              <ItemGroup>
                <TypeScriptSource Include="Scripts/*.ts" PareServeAt="/" />
              </ItemGroup>
              <Import Project="{Path.Combine(BuildDir, "Extrode.Pare.Build.targets")}" />
            </Project>
            """);
        Put("Lib/wwwroot/js/widget.ts", "import { f } from './lib.ts';\nlet n: number = f();\n");
        Put("Lib/wwwroot/js/lib.ts", "export function f(): number { return 1; }\n");
        Put("Lib/Scripts/extra.ts", "let e: T;\n");
        Put("App/App.csproj", """
            <Project Sdk="Microsoft.NET.Sdk.Web">
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
                <OutputType>Library</OutputType>
              </PropertyGroup>
              <ItemGroup>
                <ProjectReference Include="../Lib/Lib.csproj" />
              </ItemGroup>
            </Project>
            """);

        var (exit, output) = Run("publish", "App/App.csproj", "-nologo", "-nodeReuse:false", "--disable-build-servers", "-clp:NoSummary", "-o", P("pub"));
        Assert.True(exit == 0, output);
        string content = P("pub/wwwroot/_content/Lib");
        var published = Directory.EnumerateFiles(content, "*", SearchOption.AllDirectories)
            .Select(f => Path.GetRelativePath(content, f).Replace('\\', '/')).Order().ToList();
        Assert.Equal(["extra.js", "extra.js.br", "extra.js.gz", "js/lib.js", "js/lib.js.br", "js/lib.js.gz", "js/widget.js", "js/widget.js.br", "js/widget.js.gz"], published);
        Assert.Equal("import { f } from './lib.js';\nlet n         = f();\n", File.ReadAllText(Path.Combine(content, "js/widget.js")));
        Assert.Equal("export function f()         { return 1; }\n", File.ReadAllText(Path.Combine(content, "js/lib.js")));
        foreach (string js in published.Where(f => f.EndsWith(".js", StringComparison.Ordinal)))
        {
            byte[] plain = File.ReadAllBytes(Path.Combine(content, js));
            using (var gz = new System.IO.Compression.GZipStream(File.OpenRead(Path.Combine(content, js + ".gz")), System.IO.Compression.CompressionMode.Decompress))
                Assert.Equal(plain, ReadAll(gz));
            using (var br = new System.IO.Compression.BrotliStream(File.OpenRead(Path.Combine(content, js + ".br")), System.IO.Compression.CompressionMode.Decompress))
                Assert.Equal(plain, ReadAll(br));
        }
        Assert.Empty(Directory.EnumerateFiles(P("pub"), "*.ts", SearchOption.AllDirectories));
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

    private const string ScriptsMapping = """<TypeScriptSource Include="Scripts/**/*.ts" PareServeAt="js" />""";

    // The second case puts %(RecursiveDir) in the URL, which is `lib\` on Windows.
    [Theory]
    [InlineData("wwwroot/js/app.ts", "Scripts/app.ts", "js/app.js")]
    [InlineData("wwwroot/js/lib/fmt.ts", "Scripts/lib/fmt.ts", "js/lib/fmt.js")]
    public void Two_sources_at_one_url_fail_the_build_naming_both(string first, string second, string url)
    {
        Project("<OutputType>Library</OutputType>", sdk: "Microsoft.NET.Sdk.Web", items: ScriptsMapping);
        Put(first, "let a: T;");
        Put(second, "let b: T;");
        Put("Scripts/other.ts", "let o: T;");

        var (exit, output) = Build();

        Assert.NotEqual(0, exit);
        Assert.DoesNotContain("MSB4018", output);
        string error = Assert.Single(output.Split('\n').Distinct(), line => line.Contains("error PARE201", StringComparison.Ordinal));
        Assert.Contains($"app.csproj : error PARE201: two TypeScript sources map to one URL, {url}: ", error);
        Assert.Contains("Serve one under a different PareServeAt folder", error);
        Assert.Contains(first.Replace('/', Path.DirectorySeparatorChar), error.Replace('/', Path.DirectorySeparatorChar));
        Assert.Contains(second.Replace('/', Path.DirectorySeparatorChar), error.Replace('/', Path.DirectorySeparatorChar));
        Assert.DoesNotContain("other", error);
        Assert.False(File.Exists(P(ObjOut + url)));
    }

    [Fact]
    public void A_mapped_source_outside_wwwroot_is_served_at_its_mapped_url()
    {
        Project("<OutputType>Library</OutputType>", sdk: "Microsoft.NET.Sdk.Web", items: ScriptsMapping, targets: AssetProbe);
        Put("Scripts/app.ts", "import { f } from './lib/fmt.ts';\nlet a: T = f();\n");
        Put("Scripts/lib/fmt.ts", "export let f: T;\n");

        for (int build = 0; build < 2; build++)
        {
            var (exit, output) = Build();
            Assert.True(exit == 0, output);
            var assets = Assets(output);
            string app = Assert.Single(assets, a => a.Contains("app", StringComparison.Ordinal));
            Assert.StartsWith("js/app#[.{fingerprint}]?.js ", app);
            string fmt = Assert.Single(assets, a => a.Contains("fmt", StringComparison.Ordinal));
            Assert.StartsWith("js/lib/fmt#[.{fingerprint}]?.js ", fmt);
            Assert.EndsWith(Tail(ObjOut + "js/lib/fmt.js"), fmt);
        }
        Assert.Equal("import { f } from './lib/fmt.js';\nlet a    = f();\n", File.ReadAllText(P(ObjOut + "js/app.js")));
        Assert.False(File.Exists(P("Scripts/app.js")));
        Assert.DoesNotContain("PARE202", Build().Output);
    }

    [Theory]
    [InlineData("node", new[] { "--input-type=module", "-e" })]
    [InlineData("deno", new[] { "eval" })]
    public void A_served_mts_source_publishes_as_mjs_and_its_importer_runs(string runtime, string[] evaluate)
    {
        string where = OnPath(runtime);
        Project("<OutputType>Library</OutputType>", sdk: "Microsoft.NET.Sdk.Web",
            items: """<TypeScriptSource Include="Scripts/**/*.mts" PareServeAt="js" />""");
        Put("Scripts/main.mts", "import { twice } from './lib/twice.mts';\nexport const four: number = twice(2);\n");
        Put("Scripts/lib/twice.mts", "export function twice(n: number): number { return n * 2; }\n");

        var (exit, output) = Run("publish", "-nologo", "-nodeReuse:false", "--disable-build-servers", "-clp:NoSummary", "-o", P("pub"));

        Assert.True(exit == 0, output);
        string js = P("pub/wwwroot/js");
        Assert.Equal(["lib/twice.mjs", "lib/twice.mjs.br", "lib/twice.mjs.gz", "main.mjs", "main.mjs.br", "main.mjs.gz"],
            Directory.EnumerateFiles(js, "*", SearchOption.AllDirectories).Select(f => Path.GetRelativePath(js, f).Replace('\\', '/')).Order());
        string main = new Uri(Path.Combine(js, "main.mjs")).AbsoluteUri;
        Assert.Equal("4", Evaluate(where, evaluate, $$"""const { four } = await import("{{main}}"); console.log(four);"""));
    }

    [Theory]
    [InlineData("/", "app#[.{fingerprint}]?.js ", "lib/fmt#[.{fingerprint}]?.js ")]
    [InlineData("js", "js/app#[.{fingerprint}]?.js ", "js/lib/fmt#[.{fingerprint}]?.js ")]
    [InlineData("/js/", "js/app#[.{fingerprint}]?.js ", "js/lib/fmt#[.{fingerprint}]?.js ")]
    [InlineData(@"\static\js\", "static/js/app#[.{fingerprint}]?.js ", "static/js/lib/fmt#[.{fingerprint}]?.js ")]
    public void Serve_at_is_a_folder_with_its_slashes_trimmed(string serveAt, string app, string fmt)
    {
        Project("<OutputType>Library</OutputType>", sdk: "Microsoft.NET.Sdk.Web", targets: AssetProbe,
            items: $"""<TypeScriptSource Include="Scripts/**/*.ts" PareServeAt="{serveAt}" />""");
        Put("Scripts/app.ts", "let a: T;");
        Put("Scripts/lib/fmt.ts", "let f: T;");

        var (exit, output) = Build();

        Assert.True(exit == 0, output);
        var assets = Assets(output);
        Assert.Single(assets, a => a.StartsWith(app, StringComparison.Ordinal));
        Assert.Single(assets, a => a.StartsWith(fmt, StringComparison.Ordinal));
    }

    [Fact]
    public void A_wwwroot_source_with_serve_at_is_served_there_instead()
    {
        Project("<OutputType>Library</OutputType>", sdk: "Microsoft.NET.Sdk.Web", targets: AssetProbe,
            items: """<TypeScriptSource Update="wwwroot/js/app.ts" PareServeAt="scripts" />""");
        Put("wwwroot/js/app.ts", "let a: T;");
        Put("wwwroot/js/other.ts", "let o: T;");

        var (exit, output) = Build();

        Assert.True(exit == 0, output);
        var assets = Assets(output);
        // %(RecursiveDir) comes from the default item's wwwroot/**.
        Assert.Single(assets, a => a.StartsWith("scripts/js/app#[.{fingerprint}]?.js ", StringComparison.Ordinal));
        Assert.DoesNotContain(assets, a => a.StartsWith("js/app", StringComparison.Ordinal));
        Assert.Single(assets, a => a.StartsWith("js/other#[.{fingerprint}]?.js ", StringComparison.Ordinal));
    }

    [Fact]
    public void Pare_relative_path_wins_over_serve_at()
    {
        Project("<OutputType>Library</OutputType>", sdk: "Microsoft.NET.Sdk.Web", targets: AssetProbe,
            items: """<TypeScriptSource Include="Scripts/*.ts" PareServeAt="js" PareRelativePath="custom/%(Filename).js" />""");
        Put("Scripts/app.ts", "let a: T;");

        var (exit, output) = Build();

        Assert.True(exit == 0, output);
        Assert.Single(Assets(output), a => a.StartsWith("custom/app#[.{fingerprint}]?.js ", StringComparison.Ordinal));
    }

    [Fact]
    public void A_razor_ts_served_at_the_root_replaces_a_stale_razor_js_beside_it()
    {
        Project("<OutputType>Library</OutputType>", sdk: "Microsoft.NET.Sdk.Web", targets: AssetProbe,
            items: """<TypeScriptSource Include="**/*.razor.ts" Exclude="bin/**;obj/**;node_modules/**" PareServeAt="/" />""");
        Put("Components/Pages/Counter.razor", "<p>count</p>\n");
        Put("Components/Pages/Counter.razor.ts", "export function onLoad(): void {}\n");
        Put("Components/Pages/Counter.razor.js", "old;");

        for (int build = 0; build < 2; build++)
        {
            var (exit, output) = Build();
            Assert.True(exit == 0, output);
            Assert.DoesNotContain("MSB4018", output);
            Assert.Contains(Path.Combine("Components", "Pages", "Counter.razor.js") + " : warning PARE200: ", output);
            string counter = Assert.Single(Assets(output), a => a.Contains("Counter", StringComparison.Ordinal));
            Assert.StartsWith("Components/Pages/Counter#[.{fingerprint}]?.razor.js ", counter);
            Assert.EndsWith(Tail(ObjOut + "Components/Pages/Counter.razor.js"), counter);
        }
        Assert.Equal("export function onLoad()       {}\n", File.ReadAllText(P(ObjOut + "Components/Pages/Counter.razor.js")));
    }

    [Theory]
    [InlineData("Microsoft.NET.Sdk.Web", "", "", true)]
    [InlineData("Microsoft.NET.Sdk.Web", "<NoWarn>$(NoWarn);PARE202</NoWarn>", "", false)]
    [InlineData("Microsoft.NET.Sdk.Web", "<PareOutDir>gen</PareOutDir>", "", false)]
    [InlineData("Microsoft.NET.Sdk.Web", "", """ PareServeAt="js" """, false)]
    [InlineData("Microsoft.NET.Sdk", "", "", false)]
    public void An_unserved_source_outside_wwwroot_is_reported(string sdk, string properties, string metadata, bool reported)
    {
        Project("<OutputType>Library</OutputType>" + properties, sdk: sdk,
            items: $"""<TypeScriptSource Include="Scripts/*.ts"{metadata}/>""");
        Put("Scripts/app.ts", "let a: T;");
        Put("wwwroot/js/served.ts", "let s: T;");

        var (exit, output) = Build();

        Assert.True(exit == 0, output);
        var warnings = output.Split('\n').Where(line => line.Contains("warning PARE202", StringComparison.Ordinal)).Distinct().ToList();
        if (!reported)
        {
            Assert.Empty(warnings);
            return;
        }
        string warning = Assert.Single(warnings);
        Assert.Contains(Path.Combine("Scripts", "app.ts") + " : warning PARE202: not served: ", warning);
        Assert.Contains("Add PareServeAt=\"js\"", warning);
        Assert.Equal("let a   ;", File.ReadAllText(P("Scripts/app.js")));
    }

    // Next to its component, the SDK serves it as collocated JS.
    [Fact]
    public void A_razor_ts_beside_its_component_is_not_reported()
    {
        Project("<OutputType>Library</OutputType>", sdk: "Microsoft.NET.Sdk.Web",
            items: """<TypeScriptSource Include="Components/*.razor.ts" />""");
        Put("Components/Counter.razor", "<p>count</p>\n");
        Put("Components/Counter.razor.ts", "let c: T;");

        var (exit, output) = Build();

        Assert.True(exit == 0, output);
        Assert.DoesNotContain("PARE202", output);
        Assert.Equal("let c   ;", File.ReadAllText(P("Components/Counter.razor.js")));
    }

    [Theory]
    [InlineData("../js")]
    [InlineData("js/./lib")]
    [InlineData(".")]
    [InlineData("c:/js")]
    [InlineData("js//lib")]
    [InlineData(@"js\\lib")]
    [InlineData("js/app.js")]
    [InlineData("app.MTS")]
    [InlineData("js/app.ts/")]
    public void A_serve_at_that_is_not_a_folder_fails_the_build(string serveAt)
    {
        Project("<OutputType>Library</OutputType>", sdk: "Microsoft.NET.Sdk.Web",
            items: $"""<TypeScriptSource Include="Scripts/*.ts" PareServeAt="{serveAt}" />""");
        Put("Scripts/app.ts", "let a: T;");

        var (exit, output) = Build();

        Assert.NotEqual(0, exit);
        string error = Assert.Single(output.Split('\n').Distinct(), line => line.Contains("error PARE203", StringComparison.Ordinal));
        Assert.Contains(Path.Combine("Scripts", "app.ts") + $" : error PARE203: PareServeAt=\"{serveAt}\" is not a URL folder.", error);
        Assert.False(Directory.Exists(P(ObjOut)) && Directory.EnumerateFiles(P(ObjOut), "*.js", SearchOption.AllDirectories).Any());
        Assert.False(File.Exists(P("Scripts/app.js")));
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

    // MSBuild.exe of the newest Visual Studio, if it is MSBuild 18 or later: the .NET 10 SDK needs 18.
    private static string? VisualStudioMSBuild()
    {
        string vswhere = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Microsoft Visual Studio", "Installer", "vswhere.exe");
        if (!OperatingSystem.IsWindows() || !File.Exists(vswhere))
            return null;
        var start = new ProcessStartInfo(vswhere, ["-latest", "-prerelease", "-products", "*", "-requires", "Microsoft.Component.MSBuild", "-find", @"MSBuild\**\Bin\MSBuild.exe"])
        {
            RedirectStandardOutput = true,
        };
        using var process = Process.Start(start)!;
        string? msbuild = process.StandardOutput.ReadToEnd().Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault();
        process.WaitForExit();
        return msbuild is not null && FileVersionInfo.GetVersionInfo(msbuild).FileMajorPart >= 18 ? msbuild : null;
    }

    [Fact]
    public void Visual_Studio_MSBuild_strips_with_the_dotnet_task_host()
    {
        string? msbuild = VisualStudioMSBuild();
        Assert.SkipWhen(msbuild is null, "no Visual Studio with MSBuild 18 or later");
        Project("<OutputType>Library</OutputType>", sdk: "Microsoft.NET.Sdk.Web", targets: AssetProbe);
        Put("wwwroot/js/a.ts", "let n: number = 1;\nexport { n };\n");
        string[] build = ["-restore", "-nologo", "-nodeReuse:false", "-clp:NoSummary", "app.csproj"];

        var (exit, output) = Exec(msbuild!, build);
        Assert.True(exit == 0, output);
        // MSBuild 18.10 falls back to a task host on its own, with this warning, when UsingTask has no Runtime.
        Assert.DoesNotContain("out-of-process", output, StringComparison.Ordinal);
        Assert.Equal("let n         = 1;\nexport { n };\n", File.ReadAllText(P(ObjOut + "js/a.js")));
        Assert.Contains(Assets(output), asset => asset.StartsWith("js/a#[.{fingerprint}]?.js ", StringComparison.Ordinal)
            && asset.EndsWith(Tail(ObjOut + "js/a.js"), StringComparison.Ordinal));

        Put("wwwroot/js/b.ts", "enum E { A }\n");
        (exit, output) = Exec(msbuild!, build);
        Assert.NotEqual(0, exit);
        Assert.Contains(Path.Combine("wwwroot", "js", "b.ts") + "(1,1): error PARE100: 'enum' generates runtime code", output);
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

    // The SDK fails a .razor.js with no component beside it (BLAZOR106), as Content or None.
    [Fact]
    public void Out_dir_takes_a_razor_ts_without_failing_the_build()
    {
        Project("<OutputType>Library</OutputType><PareOutDir>gen</PareOutDir>", sdk: "Microsoft.NET.Sdk.Web", targets: AssetProbe,
            items: """<TypeScriptSource Include="Components/*.razor.ts;Pages/*.cshtml.ts" />""");
        Put("Components/Counter.razor", "<p>count</p>\n");
        Put("Components/Counter.razor.ts", "let c: T;");
        Put("Pages/Index.cshtml", "<p>index</p>\n");
        Put("Pages/Index.cshtml.ts", "let i: T;");

        for (int build = 0; build < 2; build++)
        {
            var (exit, output) = Build();
            Assert.True(exit == 0, output);
            Assert.DoesNotContain("BLAZOR106", output);
            Assert.DoesNotContain(Assets(output), a => a.Contains("Counter", StringComparison.Ordinal) || a.Contains("Index", StringComparison.Ordinal));
        }
        Assert.Equal("let c   ;", File.ReadAllText(P("gen/Components/Counter.razor.js")));
        Assert.Equal("let i   ;", File.ReadAllText(P("gen/Pages/Index.cshtml.js")));
        Assert.False(File.Exists(P("Components/Counter.razor.js")));
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
        string where = OnPath(runtime);
        BuildSample();
        // A package.json further up can say "commonjs" (one in a Windows %TEMP% did).
        Put("app/" + ObjOut + "package.json", """{ "type": "module" }""");
        string counter = new Uri(P("app/" + ObjOut + "js/counter.js")).AbsoluteUri;
        Assert.Equal("0 clicks|1 click|2 clicks", Evaluate(where, evaluate, $$"""
            const { counter } = await import("{{counter}}");
            const c = counter();
            const seen = [c.label()];
            c.increment(); seen.push(c.label());
            c.increment(); seen.push(c.label());
            console.log(seen.join("|"));
            """));
    }

    /// <summary>The runtime's full path; skips the test when it is not installed.</summary>
    private static string OnPath(string runtime)
    {
        var where = Environment.GetEnvironmentVariable("PATH")!.Split(Path.PathSeparator)
            .Select(d => Path.Combine(d, OperatingSystem.IsWindows() ? runtime + ".exe" : runtime)).FirstOrDefault(File.Exists);
        Assert.SkipWhen(where is null, $"{runtime} is not on PATH");
        return where!;
    }

    /// <summary>Runs a module script in Node or Deno and returns its trimmed stdout.</summary>
    private static string Evaluate(string runtime, string[] evaluate, string script)
    {
        var start = new ProcessStartInfo(runtime) { RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (string arg in evaluate)
            start.ArgumentList.Add(arg);
        start.ArgumentList.Add(script);
        start.Environment["NO_COLOR"] = "1";
        using var process = Process.Start(start)!;
        var stderr = process.StandardError.ReadToEndAsync(TestContext.Current.CancellationToken);
        string stdout = process.StandardOutput.ReadToEnd();
        process.WaitForExit();
        Assert.True(process.ExitCode == 0, stderr.Result);
        return stdout.Trim();
    }

    // Built with this project (a ProjectReference without the assembly), run through `dotnet`.
    private static readonly string CliDll = Path.Combine(RepoRoot, "src", "Extrode.Pare.Cli", "bin", Configuration, "net10.0", "pare.dll");

    /// <summary>Copies samples/CliSite's page and sources to site/ and strips them with the pare CLI, as its README says.</summary>
    private void StripCliSample()
    {
        string sample = Path.Combine(RepoRoot, "samples", "CliSite");
        Put("site/index.html", File.ReadAllText(Path.Combine(sample, "index.html")));
        foreach (var file in Directory.EnumerateFiles(Path.Combine(sample, "src")))
            Put(Path.Combine("site", "src", Path.GetFileName(file)), File.ReadAllText(file));
        var (exit, output) = Run(CliDll, "site/src", "--outdir", "site/js");
        Assert.True(exit == 0, output);
        Assert.Equal(["main.js", "model.js", "render.js"], Directory.GetFiles(P("site/js")).Select(Path.GetFileName).Order());
        Assert.Equal("import { byPriority, summary,           } from \"./model.js\";", File.ReadLines(P("site/js/main.js")).First());
    }

    /// <summary>A static file server, as `python3 -m http.server` would be, for the life of a test.</summary>
    private sealed class StaticServer : IDisposable
    {
        private readonly System.Net.HttpListener _listener = new();
        private readonly string _root;

        public Uri Address { get; }

        public StaticServer(string root)
        {
            _root = root;
            using (var probe = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0))
            {
                probe.Start();
                Address = new Uri($"http://127.0.0.1:{((System.Net.IPEndPoint)probe.LocalEndpoint).Port}/");
            }
            _listener.Prefixes.Add(Address.ToString());
            _listener.Start();
            _ = Answer();
        }

        private async Task Answer()
        {
            while (true)
            {
                System.Net.HttpListenerContext context;
                try
                {
                    context = await _listener.GetContextAsync();
                }
                catch (Exception e) when (e is System.Net.HttpListenerException or ObjectDisposedException or InvalidOperationException)
                {
                    return;
                }
                using var response = context.Response;
                string path = Uri.UnescapeDataString(context.Request.Url!.AbsolutePath).TrimStart('/');
                string file = Path.Combine(_root, path.Length == 0 ? "index.html" : path);
                if (!File.Exists(file))
                {
                    response.StatusCode = 404;
                    continue;
                }
                // Browsers refuse a module script served without a JavaScript type.
                response.ContentType = Path.GetExtension(file) == ".js" ? "text/javascript" : "text/html; charset=utf-8";
                await response.OutputStream.WriteAsync(await File.ReadAllBytesAsync(file));
            }
        }

        public void Dispose() => _listener.Close();
    }

    [Theory]
    [MemberData(nameof(Browsers))]
    public async Task Cli_sample_runs_in_a_browser(string engine)
    {
        StripCliSample();
        using var server = new StaticServer(P("site"));
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
        await page.GotoAsync(server.Address.ToString());

        var status = page.Locator("#status");
        var items = page.Locator("#tasks li");
        await Microsoft.Playwright.Assertions.Expect(status).ToHaveTextAsync("2 of 3 open");
        await Microsoft.Playwright.Assertions.Expect(items).ToHaveTextAsync(
            ["Ship the release (high)", "Write the docs (medium)", "Tidy the samples (low)"]);
        await page.Locator("#add").ClickAsync();
        await Microsoft.Playwright.Assertions.Expect(status).ToHaveTextAsync("3 of 4 open");
        await Microsoft.Playwright.Assertions.Expect(items.Last).ToHaveTextAsync("New task 1 (low)");
        Assert.Empty(errors);
        Assert.Empty(failed);
    }

    [Theory]
    [InlineData("node", new[] { "--input-type=module", "-e" })]
    [InlineData("deno", new[] { "eval" })]
    public void Cli_sample_modules_run_outside_the_browser(string runtime, string[] evaluate)
    {
        string where = OnPath(runtime);
        StripCliSample();
        Put("site/js/package.json", """{ "type": "module" }""");
        string model = new Uri(P("site/js/model.js")).AbsoluteUri;
        Assert.Equal("b,a|1 of 2 open", Evaluate(where, evaluate, $$"""
            const { byPriority, summary } = await import("{{model}}");
            const tasks = [{ title: "a", priority: "low", done: true }, { title: "b", priority: "high", done: false }];
            console.log(byPriority(tasks).map(t => t.title).join(",") + "|" + summary(tasks));
            """));
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
