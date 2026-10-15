using Extrode.Pare.AspNetCore;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
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

    [Theory]
    [InlineData("Development", true)]
    [InlineData("Production", false)]
    public void UsePareDevelopment_runs_only_in_Development(string environment, bool strips)
    {
        Put("wwwroot/a.ts", "let a: T;\n");
        var builder = WebApplication.CreateSlimBuilder(new WebApplicationOptions
        {
            ContentRootPath = _dir.FullName,
            WebRootPath = "wwwroot",
            EnvironmentName = environment,
        });
        builder.Logging.ClearProviders();
        using var app = builder.Build();
        app.UsePareDevelopment();
        Assert.Equal(strips, File.Exists(P("wwwroot/a.js")));
        app.Services.GetRequiredService<IHostApplicationLifetime>().StopApplication();
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
}
