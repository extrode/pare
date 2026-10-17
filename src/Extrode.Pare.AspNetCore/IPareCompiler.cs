using Microsoft.Extensions.Logging;

namespace Extrode.Pare.AspNetCore;

/// <summary>
/// Another compile-to-JavaScript step that <see cref="PareDevelopmentExtensions.AddPareDevelopment"/>
/// re-runs on save: the sources the build declared as <c>PareCompileSource</c> with
/// <c>PareCompiler</c> equal to <see cref="Name"/>. Register it in DI as a singleton
/// (<c>services.AddSingleton&lt;IPareCompiler, MyCompiler&gt;()</c>); Pare calls it from its watcher
/// thread, one source at a time.
/// </summary>
public interface IPareCompiler
{
    /// <summary>The <c>PareCompiler</c> metadata value this compiler handles.</summary>
    string Name { get; }

    /// <summary>
    /// Compiles one source. <paramref name="outputPaths"/> are the full paths the build gave it, one
    /// per <c>PareCompileSource</c> item (for example a <c>.js</c> and a <c>.css</c>). Returns the
    /// text for each of them, keyed by path; Pare writes each one that changed. Returns null after
    /// logging the errors, which leaves the old outputs in place.
    /// </summary>
    IReadOnlyDictionary<string, string>? Compile(string sourcePath, string source, IReadOnlyList<string> outputPaths, ILogger logger);
}
