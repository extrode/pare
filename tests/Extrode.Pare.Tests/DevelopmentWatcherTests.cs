using Extrode.Pare.AspNetCore;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
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

    private async Task RunDevelopment(Func<Task> whileRunning, Action<IServiceCollection>? services = null)
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
        services?.Invoke(builder.Services);
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

    // Each output gets its extension and the source upper-cased; a source starting "bad" fails.
    private sealed class UpperCompiler(string name = "upper") : IPareCompiler
    {
        private int _calls;
        public int Calls => Volatile.Read(ref _calls);
        public string Name => name;

        public Task<IReadOnlyDictionary<string, string>?> CompileAsync(string sourcePath, string source, IReadOnlyList<string> outputPaths,
            ILogger logger, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _calls);
            if (source.StartsWith("bad", StringComparison.Ordinal))
            {
                logger.LogError("{Source}: bad source", sourcePath);
                return Task.FromResult<IReadOnlyDictionary<string, string>?>(null);
            }
            return Task.FromResult<IReadOnlyDictionary<string, string>?>(
                outputPaths.ToDictionary(o => o, o => Path.GetExtension(o) + ":" + source.ToUpperInvariant()));
        }
    }

    private TypeScriptWatcher CompiledWatcher(IPareCompiler compiler, params (string Source, string[] Outputs)[] sources) =>
        TypeScriptWatcher.ForBuildOutput(_dir.FullName, P("wwwroot"), P("obj/pare"), _log, null,
            sources.ToDictionary(s => P(s.Source), s => new CompiledSource(compiler, [.. s.Outputs.Select(P)])));

    [Fact]
    public void Compiled_sources_compile_to_every_output_on_start_and_again_on_save()
    {
        Put("Components/App.up", "a");
        using var watcher = CompiledWatcher(new UpperCompiler(), ("Components/App.up", ["obj/pare/App.up.js", "obj/pare/App.up.css"]));
        watcher.Start();
        Assert.Equal(".js:A", File.ReadAllText(P("obj/pare/App.up.js")));
        Assert.Equal(".css:A", File.ReadAllText(P("obj/pare/App.up.css")));
        Assert.Single(_log.Lines, l => l.Contains("Compiled " + Path.Combine("Components", "App.up")) && l.EndsWith("App.up.css", StringComparison.Ordinal));

        Put("Components/App.up", "b");
        WaitFor(() => ReadOrNull(P("obj/pare/App.up.js")) == ".js:B" && ReadOrNull(P("obj/pare/App.up.css")) == ".css:B");
    }

    [Fact]
    public void Compiled_sources_that_are_up_to_date_are_not_compiled_and_unchanged_text_is_not_written()
    {
        Put("App.up", "a");
        var compiler = new UpperCompiler();
        using (var first = CompiledWatcher(compiler, ("App.up", ["obj/pare/App.up.js"])))
            first.Start();
        Assert.Equal(1, compiler.Calls);

        using var watcher = CompiledWatcher(compiler, ("App.up", ["obj/pare/App.up.js"]));
        watcher.Start();
        Assert.Equal(1, compiler.Calls);
        int lines = _log.Lines.Length;
        watcher.Strip(P("App.up"), started: true);
        Assert.Equal(2, compiler.Calls);
        Assert.Equal(lines, _log.Lines.Length);
    }

    [Fact]
    public void Unchanged_text_on_save_makes_the_output_newer_than_its_source()
    {
        Put("App.up", "a");
        var compiler = new UpperCompiler();
        using (var first = CompiledWatcher(compiler, ("App.up", ["obj/pare/App.up.js"])))
        {
            first.Start();
            File.SetLastWriteTimeUtc(P("obj/pare/App.up.js"), DateTime.UtcNow.AddMinutes(-1));
            first.Strip(P("App.up"), started: true);
        }
        Assert.Equal(2, compiler.Calls);

        using var watcher = CompiledWatcher(compiler, ("App.up", ["obj/pare/App.up.js"]));
        watcher.Start();
        Assert.Equal(2, compiler.Calls);
    }

    [Fact]
    public void A_failed_compile_leaves_the_old_outputs()
    {
        Put("App.up", "a");
        using var watcher = CompiledWatcher(new UpperCompiler(), ("App.up", ["obj/pare/App.up.js"]));
        watcher.Start();

        Put("App.up", "bad");
        WaitFor(() => _log.Lines.Any(l => l.EndsWith("bad source", StringComparison.Ordinal)));
        Assert.Equal(".js:A", File.ReadAllText(P("obj/pare/App.up.js")));
    }

    [Fact]
    public void A_compiler_that_returns_no_text_for_an_output_is_an_error()
    {
        Put("App.up", "a");
        var compiler = new OneOutputCompiler();
        using var watcher = CompiledWatcher(compiler, ("App.up", ["obj/pare/App.up.js", "obj/pare/App.up.css"]));
        watcher.Start();
        Assert.Equal("a", File.ReadAllText(P("obj/pare/App.up.js")));
        Assert.False(File.Exists(P("obj/pare/App.up.css")));
        Assert.Single(_log.Lines, l => l.Contains("returned no text for") && l.EndsWith("App.up.css", StringComparison.Ordinal));
    }

    private sealed class OneOutputCompiler : IPareCompiler
    {
        public string Name => "one";
        public Task<IReadOnlyDictionary<string, string>?> CompileAsync(string sourcePath, string source, IReadOnlyList<string> outputPaths,
            ILogger logger, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyDictionary<string, string>?>(new Dictionary<string, string> { [outputPaths[0]] = source });
    }

    // Keys the output paths through a "." segment, as a compiler building its own paths might.
    private sealed class DotPathCompiler : IPareCompiler
    {
        public string Name => "dot";
        public Task<IReadOnlyDictionary<string, string>?> CompileAsync(string sourcePath, string source, IReadOnlyList<string> outputPaths,
            ILogger logger, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyDictionary<string, string>?>(
                outputPaths.ToDictionary(o => Path.Combine(Path.GetDirectoryName(o)!, ".", Path.GetFileName(o)), _ => source));
    }

    [Fact]
    public void Compiled_text_is_matched_to_its_output_whatever_the_path_form()
    {
        Put("App.up", "a");
        using var watcher = CompiledWatcher(new DotPathCompiler(), ("App.up", ["obj/pare/App.up.js"]));
        watcher.Start();
        Assert.Equal("a", File.ReadAllText(P("obj/pare/App.up.js")));
    }

    private sealed class ThrowingCompiler : IPareCompiler
    {
        public string Name => "throws";
        public Task<IReadOnlyDictionary<string, string>?> CompileAsync(string sourcePath, string source, IReadOnlyList<string> outputPaths,
            ILogger logger, CancellationToken cancellationToken) => throw new InvalidOperationException("compiler bug");
    }

    [Fact]
    public void A_compiler_that_throws_is_logged_with_its_exception()
    {
        Put("App.up", "a");
        using var watcher = CompiledWatcher(new ThrowingCompiler(), ("App.up", ["obj/pare/App.up.js"]));
        watcher.Start();
        Assert.False(File.Exists(P("obj/pare/App.up.js")));
        Assert.Single(_log.Lines, l => l.Contains("throws failed: compiler bug") && l.Contains(nameof(ThrowingCompiler)));
    }

    private sealed class Concurrency
    {
        private int _active;
        private int _max;
        public int Max => Volatile.Read(ref _max);

        public void Enter()
        {
            int active = Interlocked.Increment(ref _active);
            int max;
            while (active > (max = Volatile.Read(ref _max)) && Interlocked.CompareExchange(ref _max, active, max) != max) { }
        }

        public void Exit() => Interlocked.Decrement(ref _active);
    }

    // Takes 400 ms for a source starting "slow"; records how many compiles ran at once.
    private sealed class SlowCompiler(Concurrency? shared = null, string name = "slow") : IPareCompiler
    {
        public Concurrency Running { get; } = shared ?? new();
        public string Name => name;

        public async Task<IReadOnlyDictionary<string, string>?> CompileAsync(string sourcePath, string source, IReadOnlyList<string> outputPaths,
            ILogger logger, CancellationToken cancellationToken)
        {
            Running.Enter();
            try
            {
                await Task.Delay(source.StartsWith("slow", StringComparison.Ordinal) ? 400 : 0, cancellationToken);
                return outputPaths.ToDictionary(o => o, _ => source);
            }
            finally
            {
                Running.Exit();
            }
        }
    }

    [Fact]
    public void A_later_save_is_compiled_after_an_earlier_slow_one_and_compilers_run_one_source_at_a_time()
    {
        Put("App.up", "a");
        Put("Lib/Lib.up", "l");
        var compiler = new SlowCompiler();
        using var app = CompiledWatcher(compiler, ("App.up", ["obj/pare/App.up.js"]));
        using var lib = CompiledWatcher(compiler, ("Lib/Lib.up", ["Lib/obj/pare/Lib.up.js"]));
        app.Start();
        lib.Start();

        Put("App.up", "slow b");
        Put("Lib/Lib.up", "slow m");
        Thread.Sleep(150);
        Put("App.up", "c");
        WaitFor(() => ReadOrNull(P("obj/pare/App.up.js")) == "c" && ReadOrNull(P("Lib/obj/pare/Lib.up.js")) == "slow m");
        Thread.Sleep(600);
        Assert.Equal("c", File.ReadAllText(P("obj/pare/App.up.js")));
        Assert.Equal(1, compiler.Running.Max);
    }

    [Fact]
    public void A_watcher_compiles_one_source_at_a_time_across_compilers()
    {
        Put("A.up", "a");
        Put("B.up", "b");
        var running = new Concurrency();
        using var watcher = TypeScriptWatcher.ForBuildOutput(_dir.FullName, P("wwwroot"), P("obj/pare"), _log, null, new Dictionary<string, CompiledSource>
        {
            [P("A.up")] = new(new SlowCompiler(running, "one"), [P("obj/pare/A.up.js")]),
            [P("B.up")] = new(new SlowCompiler(running, "two"), [P("obj/pare/B.up.js")]),
        });
        watcher.Start();

        Put("A.up", "slow a");
        Thread.Sleep(150);
        Put("B.up", "slow b");
        WaitFor(() => ReadOrNull(P("obj/pare/A.up.js")) == "slow a" && ReadOrNull(P("obj/pare/B.up.js")) == "slow b");
        Assert.Equal(1, running.Max);
    }

    [Fact]
    public void Deleting_a_compiled_source_deletes_all_its_outputs()
    {
        Put("Components/A.up", "a");
        Put("Components/B.up", "b");
        using var watcher = CompiledWatcher(new UpperCompiler(),
            ("Components/A.up", ["obj/pare/A.up.js", "obj/pare/A.up.css"]), ("Components/B.up", ["obj/pare/B.up.js"]));
        watcher.Start();

        File.Delete(P("Components/A.up"));
        WaitFor(() => !File.Exists(P("obj/pare/A.up.js")) && !File.Exists(P("obj/pare/A.up.css")));
        Assert.Equal(["B.up.js"], Outputs());
    }

    private void PutCompiledMap(string relative, params (string Source, string Output, string? Compiler)[] sources) =>
        Put(relative, System.Text.Json.JsonSerializer.Serialize(new
        {
            sourceRoot = P("wwwroot") + Path.DirectorySeparatorChar,
            outputRoot = P("obj/pare") + Path.DirectorySeparatorChar,
            sources = sources.Where(s => s.Compiler is null).Select(s => new { source = P(s.Source), output = P(s.Output) }),
            compiled = sources.Where(s => s.Compiler is not null).Select(s => new { source = P(s.Source), output = P(s.Output), compiler = s.Compiler }),
        }));

    [Fact]
    public async Task AddPareDevelopment_runs_the_registered_compiler_for_its_sources_in_the_map()
    {
        Put("wwwroot/a.ts", "let a: T;\n");
        Put("Components/App.up", "a");
        Put("Components/Other.nope", "n");
        PutCompiledMap("bin/Extrode.Pare.development.json",
            ("Components/App.up", "obj/pare/App.up.js", "upper"), ("Components/App.up", "obj/pare/App.up.css", "upper"),
            ("Components/Other.nope", "obj/pare/Other.nope.js", "nope"));
        var compiler = new UpperCompiler();

        await RunDevelopment(async () =>
        {
            Assert.Equal("let a   ;\n", File.ReadAllText(P("obj/pare/a.js")));
            Assert.Equal(".js:A", File.ReadAllText(P("obj/pare/App.up.js")));
            Assert.Equal(".css:A", File.ReadAllText(P("obj/pare/App.up.css")));
            Assert.False(File.Exists(P("obj/pare/Other.nope.js")));
            Assert.Single(_log.Lines, l => l.Contains("no IPareCompiler named nope") && l.Contains(Path.Combine("Components", "Other.nope")));

            Put("Components/App.up", "b");
            WaitFor(() => ReadOrNull(P("obj/pare/App.up.js")) == ".js:B");
            await Task.CompletedTask;
        }, s => s.AddSingleton<IPareCompiler>(compiler));
    }

    [Fact]
    public async Task AddPareDevelopment_warns_of_two_compilers_with_one_name_and_of_a_source_with_two_compilers()
    {
        Put("App.up", "a");
        PutCompiledMap("bin/Extrode.Pare.development.json", ("App.up", "obj/pare/App.up.js", "upper"), ("App.up", "obj/pare/App.up.css", "dot"));
        var first = new UpperCompiler();

        await RunDevelopment(() =>
        {
            Assert.Equal(".js:A", File.ReadAllText(P("obj/pare/App.up.js")));
            Assert.False(File.Exists(P("obj/pare/App.up.css")));
            Assert.Equal(1, first.Calls);
            Assert.Single(_log.Lines, l => l.Contains("Two IPareCompiler named upper are registered"));
            Assert.Single(_log.Lines, l => l.Contains("is declared for two compilers, upper and dot"));
            return Task.CompletedTask;
        }, s => s.AddSingleton<IPareCompiler>(first).AddSingleton<IPareCompiler>(new UpperCompiler()).AddSingleton<IPareCompiler, DotPathCompiler>());
    }

    [Fact]
    public async Task AddPareDevelopment_called_twice_applies_the_second_calls_options()
    {
        Put("Scripts/fmt.ts", "let f: T;\n");
        PutMap("bin/map.json", "", ("Scripts/fmt.ts", "obj/pare/js/fmt.js"));
        var builder = WebApplication.CreateSlimBuilder(new WebApplicationOptions
        {
            ContentRootPath = _dir.FullName,
            EnvironmentName = "Development",
        });
        builder.Logging.ClearProviders();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.AddPareDevelopment();
        builder.AddPareDevelopment(o => o.DevelopmentMapPath = P("bin/map.json"));
        await using var app = builder.Build();
        await app.StartAsync(TestContext.Current.CancellationToken);
        Assert.Equal("let f   ;\n", File.ReadAllText(P("obj/pare/js/fmt.js")));
        await app.StopAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public void AddPareDevelopment_called_twice_adds_one_service()
    {
        var builder = WebApplication.CreateSlimBuilder(new WebApplicationOptions
        {
            ContentRootPath = _dir.FullName,
            EnvironmentName = "Development",
        });
        builder.AddPareDevelopment();
        int once = builder.Services.Count;
        builder.AddPareDevelopment();
        Assert.Equal(once, builder.Services.Count);
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
            lock (_lock) _lines.Add(exception is null ? formatter(state, exception) : formatter(state, exception) + "\n" + exception);
        }
    }

    private sealed class ListLoggerProvider(ListLogger logger) : ILoggerProvider
    {
        public ILogger CreateLogger(string categoryName) => categoryName == "Extrode.Pare" ? logger : Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance;
        public void Dispose() { }
    }
}
