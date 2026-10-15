using System.Diagnostics.CodeAnalysis;
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
    /// older when the app starts, then re-strips each one as it is saved until the app stops.
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
        private TypeScriptWatcher? _watcher;

        public Task StartAsync(CancellationToken cancellationToken)
        {
            if (!string.IsNullOrEmpty(env.WebRootPath) && Directory.Exists(env.WebRootPath))
            {
                _watcher = new TypeScriptWatcher(env.ContentRootPath, env.WebRootPath, options.OutDir, logger);
                _watcher.Start();
            }
            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            _watcher?.Dispose();
            return Task.CompletedTask;
        }
    }
}
