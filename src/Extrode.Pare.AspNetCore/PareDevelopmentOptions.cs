namespace Extrode.Pare.AspNetCore;

/// <summary>Settings for <see cref="PareDevelopmentExtensions.UsePareDevelopment"/>. Match them to the project's MSBuild properties.</summary>
public sealed class PareDevelopmentOptions
{
    /// <summary>Same as <c>PareOutDir</c>: write under this directory, relative to the content root, keeping
    /// each source's path from the content root. Empty writes next to each source.</summary>
    public string? OutDir { get; set; }
}
