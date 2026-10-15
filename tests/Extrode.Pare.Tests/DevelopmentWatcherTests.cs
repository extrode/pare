using Extrode.Pare.AspNetCore;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Logging;

namespace Extrode.Pare.Tests;

public sealed class DevelopmentWatcherTests : IDisposable
{
    private readonly DirectoryInfo _dir = Directory.CreateTempSubdirectory("pare-dev-");
    private readonly ListLogger _log = new();

    public void Dispose() => _dir.Delete(recursive: true);

    private string P(string relative) => Path.Combine(_dir.FullName, relative);

    private void Put(string relative, string text)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(P(relative))!);
        File.WriteAllText(P(relative), text);
    }

    private TypeScriptWatcher Watcher(string? outDir = null) => new(_dir.FullName, P("wwwroot"), outDir, _log);

    private static void WaitFor(Func<bool> condition)
    {
        var until = DateTime.UtcNow.AddSeconds(10);
        while (!condition())
        {
            Assert.True(DateTime.UtcNow < until, "timed out");
            Thread.Sleep(20);
        }
    }

    private static string? ReadOrNull(string path)
    {
        try { return File.ReadAllText(path); }
        catch (IOException) { return null; }
    }

    [Fact]
    public void Start_strips_what_the_build_would()
    {
        Put("wwwroot/js/app.ts", "import { f } from './lib.ts';\nlet n: number = f();\n");
        Put("wwwroot/js/m.mts", "export let m: T;\n");
        Put("wwwroot/js/types.d.ts", "declare let t: T;\n");
        Put("wwwroot/node_modules/x/i.ts", "let i: T;\n");
        using var watcher = Watcher();
        watcher.Start();

        Assert.Equal("import { f } from './lib.js';\nlet n         = f();\n", File.ReadAllText(P("wwwroot/js/app.js")));
        Assert.Equal("export let m   ;\n", File.ReadAllText(P("wwwroot/js/m.mjs")));
        Assert.False(File.Exists(P("wwwroot/js/types.d.js")));
        Assert.False(File.Exists(P("wwwroot/node_modules/x/i.js")));
    }

    [Fact]
    public void Start_leaves_up_to_date_output_alone()
    {
        Put("wwwroot/a.ts", "let a: T;\n");
        Put("wwwroot/a.js", "built\n");
        File.SetLastWriteTimeUtc(P("wwwroot/a.ts"), DateTime.UtcNow.AddMinutes(-1));
        using var watcher = Watcher();
        watcher.Start();
        Assert.Equal("built\n", File.ReadAllText(P("wwwroot/a.js")));
    }

    [Fact]
    public void Saving_a_source_strips_it_again()
    {
        Put("wwwroot/js/a.ts", "let a: number = 1;\n");
        using var watcher = Watcher();
        watcher.Start();

        Put("wwwroot/js/a.ts", "let a: string = 'two';\n");
        WaitFor(() => ReadOrNull(P("wwwroot/js/a.js")) == "let a         = 'two';\n");
        Put("wwwroot/js/new.ts", "let b: T;\n");
        WaitFor(() => ReadOrNull(P("wwwroot/js/new.js")) == "let b   ;\n");
        Assert.Contains(_log.Lines, l => l.Contains("Stripped wwwroot/js/new.ts to wwwroot/js/new.js".Replace('/', Path.DirectorySeparatorChar)));
    }

    // Linux watches each directory on its own and adds a new one only after it is reported, so a
    // file written into a new directory before then raises no event.
    [Fact]
    public void A_source_in_a_new_directory_is_stripped()
    {
        Put("wwwroot/a.ts", "let a: T;\n");
        using var watcher = Watcher();
        watcher.Start();

        Put("wwwroot/lib/deep/b.ts", "let b: T;\n");
        WaitFor(() => ReadOrNull(P("wwwroot/lib/deep/b.js")) == "let b   ;\n");
    }

    [Fact]
    public void A_rejected_source_is_logged_and_its_output_kept()
    {
        Put("wwwroot/a.ts", "let a: number = 1;\n");
        using var watcher = Watcher();
        watcher.Start();

        Put("wwwroot/a.ts", "enum E { A }\n");
        WaitFor(() => _log.Lines.Any(l => l.Contains("error PARE")));
        Assert.Contains(_log.Lines, l => l.StartsWith(Path.Combine("wwwroot", "a.ts") + ":1:1: error PARE1", StringComparison.Ordinal));
        Assert.Equal("let a         = 1;\n", File.ReadAllText(P("wwwroot/a.js")));
    }

    [Fact]
    public void OutDir_keeps_the_path_from_the_content_root()
    {
        Put("wwwroot/js/a.ts", "let a: T;\n");
        using var watcher = Watcher("gen");
        watcher.Start();
        Assert.Equal("let a   ;\n", File.ReadAllText(P("gen/wwwroot/js/a.js")));
        Assert.False(File.Exists(P("wwwroot/js/a.js")));
    }

    private TypeScriptWatcher BuildOutputWatcher() => TypeScriptWatcher.ForBuildOutput(_dir.FullName, P("wwwroot"), P("obj/pare"), _log);

    [Fact]
    public void Build_output_keeps_the_path_below_the_web_root()
    {
        Put("wwwroot/js/a.ts", "let a: T;\n");
        Put("wwwroot/m.mts", "let m: T;\n");
        using var watcher = BuildOutputWatcher();
        watcher.Start();
        Assert.Equal("let a   ;\n", File.ReadAllText(P("obj/pare/js/a.js")));
        Assert.Equal("let m   ;\n", File.ReadAllText(P("obj/pare/m.mjs")));
        Assert.False(File.Exists(P("wwwroot/js/a.js")));
    }

    [Fact]
    public void Deleting_a_source_deletes_its_build_output()
    {
        Put("wwwroot/js/a.ts", "let a: T;\n");
        Put("wwwroot/js/b.ts", "let b: T;\n");
        using var watcher = BuildOutputWatcher();
        watcher.Start();

        File.Delete(P("wwwroot/js/a.ts"));
        WaitFor(() => !File.Exists(P("obj/pare/js/a.js")));
        Assert.True(File.Exists(P("obj/pare/js/b.js")));
        Assert.Contains(_log.Lines, l => l.Contains("Removed " + Path.Combine("obj", "pare", "js", "a.js")));
    }

    [Fact]
    public void Deleting_a_source_keeps_a_js_next_to_it()
    {
        Put("wwwroot/a.ts", "let a: T;\n");
        using var watcher = Watcher();
        watcher.Start();

        File.Delete(P("wwwroot/a.ts"));
        Put("wwwroot/b.ts", "let b: T;\n");
        WaitFor(() => File.Exists(P("wwwroot/b.js")));
        Assert.True(File.Exists(P("wwwroot/a.js")));
    }

    [Fact]
    public void A_new_source_is_served_after_the_next_build()
    {
        Put("wwwroot/a.ts", "let a: T;\n");
        using var watcher = BuildOutputWatcher();
        watcher.Start();
        Assert.DoesNotContain(_log.Lines, l => l.Contains("next build"));

        Put("wwwroot/a.ts", "let a: U;\n");
        Put("wwwroot/js/new.ts", "let n: T;\n");
        WaitFor(() => File.Exists(P("obj/pare/js/new.js")) && ReadOrNull(P("obj/pare/a.js")) == "let a   ;\n" && _log.Lines.Length >= 2);
        string newLine = Assert.Single(_log.Lines, l => l.Contains("next build"));
        Assert.Contains(Path.Combine("wwwroot", "js", "new.ts"), newLine);
    }

    private TypeScriptWatcher MappedWatcher(params (string Source, string Output)[] sources) =>
        TypeScriptWatcher.ForBuildOutput(_dir.FullName, P("wwwroot"), P("obj/pare"), _log,
            sources.ToDictionary(s => P(s.Source), s => P(s.Output)));

    private string[] Outputs() =>
        [.. Directory.EnumerateFiles(P("obj/pare"), "*", SearchOption.AllDirectories).Select(f => Path.GetRelativePath(P("obj/pare"), f).Replace('\\', '/')).Order()];

    [Fact]
    public void Mapped_sources_outside_the_web_root_strip_to_their_output_and_again_on_save()
    {
        Put("wwwroot/a.ts", "let a: T;\n");
        Put("Scripts/lib/fmt.ts", "let f: number = 1;\n");
        Put("Scripts/m.mts", "let m: T;\n");
        Put("Scripts/other.ts", "let o: T;\n");
        using var watcher = MappedWatcher(("Scripts/lib/fmt.ts", "obj/pare/js/lib/fmt.js"), ("Scripts/m.mts", "obj/pare/js/m.mjs"));
        watcher.Start();
        Assert.Equal("let a   ;\n", File.ReadAllText(P("obj/pare/a.js")));
        Assert.Equal("let f         = 1;\n", File.ReadAllText(P("obj/pare/js/lib/fmt.js")));
        Assert.Equal("let m   ;\n", File.ReadAllText(P("obj/pare/js/m.mjs")));

        Put("Scripts/other.ts", "let o: U;\n");
        Put("Scripts/new.ts", "let n: T;\n");
        Put("Scripts/lib/fmt.ts", "let f: string = 'two';\n");
        WaitFor(() => ReadOrNull(P("obj/pare/js/lib/fmt.js")) == "let f         = 'two';\n");
        Thread.Sleep(TypeScriptWatcher.Settle * 4);
        Assert.Equal(["a.js", "js/lib/fmt.js", "js/m.mjs"], Outputs());
        Assert.Equal(["fmt.ts"], Directory.GetFiles(P("Scripts/lib")).Select(Path.GetFileName));
        Assert.Equal(["m.mts", "new.ts", "other.ts"], Directory.GetFiles(P("Scripts")).Select(Path.GetFileName).Order());
    }

    [Fact]
    public void Saving_a_mapped_source_by_rename_strips_it_again()
    {
        Put("Scripts/fmt.ts", "let f: T;\n");
        using var watcher = MappedWatcher(("Scripts/fmt.ts", "obj/pare/js/fmt.js"));
        watcher.Start();

        Put("Scripts/fmt.ts.tmp", "let g: T;\n");
        File.Move(P("Scripts/fmt.ts.tmp"), P("Scripts/fmt.ts"), overwrite: true);
        WaitFor(() => ReadOrNull(P("obj/pare/js/fmt.js")) == "let g   ;\n");
    }

    [Fact]
    public void Deleting_a_mapped_source_deletes_its_output()
    {
        Put("Scripts/a.ts", "let a: T;\n");
        Put("Scripts/b.ts", "let b: T;\n");
        using var watcher = MappedWatcher(("Scripts/a.ts", "obj/pare/js/a.js"), ("Scripts/b.ts", "obj/pare/js/b.js"));
        watcher.Start();

        File.Delete(P("Scripts/a.ts"));
        WaitFor(() => !File.Exists(P("obj/pare/js/a.js")));
        Assert.True(File.Exists(P("obj/pare/js/b.js")));
    }

    [Fact]
    public void A_mapped_web_root_source_strips_to_its_mapped_output_only()
    {
        Put("wwwroot/x.ts", "let x: T;\n");
        using var watcher = MappedWatcher(("wwwroot/x.ts", "obj/pare/served/x.js"));
        watcher.Start();
        Assert.Equal("let x   ;\n", File.ReadAllText(P("obj/pare/served/x.js")));

        Put("wwwroot/x.ts", "let y: T;\n");
        WaitFor(() => ReadOrNull(P("obj/pare/served/x.js")) == "let y   ;\n");
        Assert.Equal(["served/x.js"], Outputs());
    }

    [Fact]
    public void Mapped_sources_need_no_web_root()
    {
        Put("Scripts/fmt.ts", "let f: T;\n");
        using var watcher = MappedWatcher(("Scripts/fmt.ts", "obj/pare/js/fmt.js"));
        watcher.Start();
        Assert.Equal("let f   ;\n", File.ReadAllText(P("obj/pare/js/fmt.js")));

        Put("Scripts/fmt.ts", "let g: T;\n");
        WaitFor(() => ReadOrNull(P("obj/pare/js/fmt.js")) == "let g   ;\n");
    }

    [Fact]
    public async Task AddPareDevelopment_writes_where_the_build_map_says()
    {
        Put("wwwroot/a.ts", "let a: T;\n");
        Put("map.json", System.Text.Json.JsonSerializer.Serialize(new Dictionary<string, string>
        {
            ["sourceRoot"] = P("wwwroot") + Path.DirectorySeparatorChar,
            ["outputRoot"] = P("obj/pare") + Path.DirectorySeparatorChar,
        }));
        var builder = WebApplication.CreateSlimBuilder(new WebApplicationOptions
        {
            ContentRootPath = _dir.FullName,
            WebRootPath = "wwwroot",
            EnvironmentName = "Development",
        });
        builder.Logging.ClearProviders();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.AddPareDevelopment(o => o.DevelopmentMapPath = P("map.json"));
        await using var app = builder.Build();
        await app.StartAsync(TestContext.Current.CancellationToken);
        Assert.True(File.Exists(P("obj/pare/a.js")));
        Assert.False(File.Exists(P("wwwroot/a.js")));
        await app.StopAsync(TestContext.Current.CancellationToken);
    }

    private void PutMap(string relative, string project, params (string Source, string Output)[] sources) =>
        Put(relative, System.Text.Json.JsonSerializer.Serialize(new
        {
            sourceRoot = P(project + "wwwroot") + Path.DirectorySeparatorChar,
            outputRoot = P(project + "obj/pare") + Path.DirectorySeparatorChar,
            sources = sources.Select(s => new { source = P(project + s.Source), output = P(project + s.Output) }),
        }));

    private async Task RunDevelopment(Func<Task> whileRunning)
    {
        var builder = WebApplication.CreateSlimBuilder(new WebApplicationOptions
        {
            ContentRootPath = _dir.FullName,
            WebRootPath = "wwwroot",
            EnvironmentName = "Development",
        });
        builder.Logging.ClearProviders();
        builder.Logging.AddProvider(new ListLoggerProvider(_log));
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.AddPareDevelopment(o => o.DevelopmentMapPath = P("bin/Extrode.Pare.development.json"));
        await using var app = builder.Build();
        await app.StartAsync(TestContext.Current.CancellationToken);
        await whileRunning();
        await app.StopAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task AddPareDevelopment_restrips_the_sources_in_the_map_and_in_referenced_libraries()
    {
        Put("wwwroot/a.ts", "let a: T;\n");
        Put("Scripts/fmt.ts", "let f: T;\n");
        Put("Lib/wwwroot/w.ts", "let w: T;\n");
        Put("Lib/Scripts/x.ts", "let x: T;\n");
        PutMap("bin/Extrode.Pare.development.json", "", ("Scripts/fmt.ts", "obj/pare/js/fmt.js"));
        // Each project's own map, copied to the output of every project that references it.
        PutMap("bin/Extrode.Pare.development.App.json", "", ("Scripts/fmt.ts", "obj/pare/js/fmt.js"));
        PutMap("bin/Extrode.Pare.development.Lib.json", "Lib/", ("Scripts/x.ts", "obj/pare/x.js"));

        await RunDevelopment(async () =>
        {
            Assert.Equal("let a   ;\n", File.ReadAllText(P("obj/pare/a.js")));
            Assert.Equal("let f   ;\n", File.ReadAllText(P("obj/pare/js/fmt.js")));
            Assert.Equal("let w   ;\n", File.ReadAllText(P("Lib/obj/pare/w.js")));
            Assert.Equal("let x   ;\n", File.ReadAllText(P("Lib/obj/pare/x.js")));
            Assert.Single(_log.Lines, l => l.Contains("Stripped " + Path.Combine("Scripts", "fmt.ts")));

            Put("Scripts/fmt.ts", "let g: T;\n");
            Put("Lib/Scripts/x.ts", "let y: T;\n");
            WaitFor(() => ReadOrNull(P("obj/pare/js/fmt.js")) == "let g   ;\n" && ReadOrNull(P("Lib/obj/pare/x.js")) == "let y   ;\n");
            await Task.CompletedTask;
        });
    }

    [Fact]
    public async Task AddPareDevelopment_reads_library_maps_with_no_map_of_its_own()
    {
        Put("Lib/Scripts/x.ts", "let x: T;\n");
        PutMap("bin/Extrode.Pare.development.Lib.json", "Lib/", ("Scripts/x.ts", "obj/pare/x.js"));
        await RunDevelopment(() =>
        {
            Assert.Equal("let x   ;\n", File.ReadAllText(P("Lib/obj/pare/x.js")));
            return Task.CompletedTask;
        });
    }

    [Theory]
    [InlineData("Development", true)]
    [InlineData("Production", false)]
    public async Task AddPareDevelopment_runs_only_in_Development(string environment, bool strips)
    {
        Put("wwwroot/a.ts", "let a: T;\n");
        var builder = WebApplication.CreateSlimBuilder(new WebApplicationOptions
        {
            ContentRootPath = _dir.FullName,
            WebRootPath = "wwwroot",
            EnvironmentName = environment,
        });
        builder.Logging.ClearProviders();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.AddPareDevelopment();
        await using var app = builder.Build();
        await app.StartAsync(TestContext.Current.CancellationToken);
        Assert.Equal(strips, File.Exists(P("wwwroot/a.js")));
        await app.StopAsync(TestContext.Current.CancellationToken);
    }

    private sealed class ListLogger : ILogger
    {
        private readonly Lock _lock = new();
        private readonly List<string> _lines = [];

        public string[] Lines { get { lock (_lock) return [.. _lines]; } }

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            lock (_lock) _lines.Add(formatter(state, exception));
        }
    }

    private sealed class ListLoggerProvider(ListLogger logger) : ILoggerProvider
    {
        public ILogger CreateLogger(string categoryName) => categoryName == "Extrode.Pare" ? logger : Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance;
        public void Dispose() { }
    }
}
