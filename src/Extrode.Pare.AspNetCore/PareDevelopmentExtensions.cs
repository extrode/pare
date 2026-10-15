using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Extrode.Pare.AspNetCore;

public static class PareDevelopmentExtensions
{
    /// <summary>
    /// In Development, strips every <c>wwwroot/**/*.ts</c> and <c>*.mts</c> whose output is missing or
    /// older, then re-strips each one as it is saved until the app stops. <c>dotnet watch</c> in its
    /// default hot reload mode does not build on a <c>.ts</c> save; it sees the new <c>.js</c> and
    /// refreshes the browser. Does nothing in any other environment.
    /// </summary>
    public static IApplicationBuilder UsePareDevelopment(this IApplicationBuilder app, Action<PareDevelopmentOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(app);
        var services = app.ApplicationServices;
        var env = services.GetRequiredService<IWebHostEnvironment>();
        if (!env.IsDevelopment() || string.IsNullOrEmpty(env.WebRootPath) || !Directory.Exists(env.WebRootPath))
            return app;

        var options = new PareDevelopmentOptions();
        configure?.Invoke(options);
        var logger = services.GetRequiredService<ILoggerFactory>().CreateLogger("Extrode.Pare");
        var watcher = new TypeScriptWatcher(env.ContentRootPath, env.WebRootPath, options.OutDir, logger);
        watcher.Start();
        services.GetRequiredService<IHostApplicationLifetime>().ApplicationStopping.Register(watcher.Dispose);
        return app;
    }
}
