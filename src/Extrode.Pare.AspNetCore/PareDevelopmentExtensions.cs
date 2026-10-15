using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Extrode.Pare.AspNetCore;

public static class PareDevelopmentExtensions
{
    private const string BrowserRefreshAssembly = "Microsoft.AspNetCore.Watch.BrowserRefresh";

    /// <summary>
    /// In Development, strips every <c>wwwroot/**/*.ts</c> and <c>*.mts</c> whose output is missing or
    /// older when the app starts, then re-strips each one as it is saved until the app stops. Outputs
    /// go where the build put them: under <c>obj/</c> in a web project, as the development map in the
    /// build output says, else next to each source or under <see cref="PareDevelopmentOptions.OutDir"/>.
    /// The map also names sources served with <c>PareServeAt</c>, and the build output holds one map
    /// per referenced project, so a Razor class library's sources are re-stripped too.
    /// <c>dotnet watch</c> in its default hot reload mode does not build on a <c>.ts</c> save; it sees
    /// the new <c>.js</c> and refreshes the browser. Does nothing in any other environment.
    /// </summary>
    /// <remarks>
    /// <c>dotnet watch</c> refreshes the browser through a hosting startup that
    /// <see cref="WebApplication.CreateSlimBuilder()"/> never runs; this applies it, so slim and
    /// NativeAOT apps get the refresh too. Applying it twice adds the script once.
    /// </remarks>
    public static WebApplicationBuilder AddPareDevelopment(this WebApplicationBuilder builder, Action<PareDevelopmentOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        if (!builder.Environment.IsDevelopment())
            return builder;

        var options = new PareDevelopmentOptions();
        configure?.Invoke(options);
        builder.Services.AddHostedService(services => new PareDevelopmentService(
            services.GetRequiredService<IWebHostEnvironment>(), options,
            services.GetRequiredService<ILoggerFactory>().CreateLogger("Extrode.Pare")));
        ApplyBrowserRefresh(builder);
        return builder;
    }

    [UnconditionalSuppressMessage("Trimming", "IL2026", Justification = "Development only: dotnet watch loads the assembly into a JIT process; a trimmed app never has it.")]
    [UnconditionalSuppressMessage("Trimming", "IL2072", Justification = "As above.")]
    private static void ApplyBrowserRefresh(WebApplicationBuilder builder)
    {
        var hook = AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(a => a.GetName().Name == BrowserRefreshAssembly);
        var startup = hook?.GetType(BrowserRefreshAssembly + ".HostingStartup");
        if (startup is not null && Activator.CreateInstance(startup) is IHostingStartup hostingStartup)
            hostingStartup.Configure(builder.WebHost);
    }

    private sealed class PareDevelopmentService(IWebHostEnvironment env, PareDevelopmentOptions options, ILogger logger) : IHostedService
    {
        private readonly List<TypeScriptWatcher> _watchers = [];

        // This project's map, then each referenced project's (Extrode.Pare.development.<project>.json),
        // which the build copies here too. The project's own copy names the same output root as its map.
        public Task StartAsync(CancellationToken cancellationToken)
        {
            string directory = Path.GetDirectoryName(Path.GetFullPath(options.DevelopmentMapPath))!;
            var paths = new List<string> { options.DevelopmentMapPath };
            if (Directory.Exists(directory))
                paths.AddRange(Directory.GetFiles(directory, "Extrode.Pare.development.*.json").Order(StringComparer.Ordinal));
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (string path in paths)
            {
                if (ReadMap(path) is not var (sourceRoot, outputRoot, sources) || !seen.Add(Path.GetFullPath(outputRoot)))
                    continue;
                if (Directory.Exists(sourceRoot) || sources.Count > 0)
                    _watchers.Add(TypeScriptWatcher.ForBuildOutput(env.ContentRootPath, sourceRoot, outputRoot, logger, sources));
            }
            if (seen.Count == 0 && !string.IsNullOrEmpty(env.WebRootPath) && Directory.Exists(env.WebRootPath))
                _watchers.Add(new TypeScriptWatcher(env.ContentRootPath, env.WebRootPath, options.OutDir, logger));
            foreach (var watcher in _watchers)
                watcher.Start();
            return Task.CompletedTask;
        }

        // { "sourceRoot", "outputRoot", "sources": [{ "source", "output" }] }; Pare 0.3 wrote no sources.
        private static (string SourceRoot, string OutputRoot, Dictionary<string, string> Sources)? ReadMap(string path)
        {
            if (!File.Exists(path))
                return null;
            using var map = JsonDocument.Parse(File.ReadAllText(path));
            var root = map.RootElement;
            if (!(root.TryGetProperty("sourceRoot", out var source) && source.GetString() is { } sourceRoot &&
                  root.TryGetProperty("outputRoot", out var output) && output.GetString() is { } outputRoot))
                return null;
            var sources = new Dictionary<string, string>();
            if (root.TryGetProperty("sources", out var list) && list.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in list.EnumerateArray())
                {
                    if (item.TryGetProperty("source", out var s) && s.GetString() is { } from &&
                        item.TryGetProperty("output", out var o) && o.GetString() is { } to)
                        sources[from] = to;
                }
            }
            return (sourceRoot, outputRoot, sources);
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            foreach (var watcher in _watchers)
                watcher.Dispose();
            return Task.CompletedTask;
        }
    }
}
