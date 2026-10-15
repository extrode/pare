namespace Extrode.Pare.AspNetCore;

/// <summary>Settings for <see cref="PareDevelopmentExtensions.AddPareDevelopment"/>. Match them to the project's MSBuild properties.</summary>
public sealed class PareDevelopmentOptions
{
    /// <summary>Same as <c>PareOutDir</c>: write under this directory, relative to the content root, keeping
    /// each source's path from the content root. Empty writes next to each source.</summary>
    /// <remarks>Used only when the build wrote no development map, which it does when <c>PareOutDir</c> is set.</remarks>
    public string? OutDir { get; set; }

    /// <summary>Where the build says sources and outputs are: <c>Extrode.Pare.development.json</c> in the build output.</summary>
    internal string DevelopmentMapPath { get; set; } = Path.Combine(AppContext.BaseDirectory, "Extrode.Pare.development.json");
}
