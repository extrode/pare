namespace Extrode.Pare;

/// <summary>Options for <see cref="TypeStripper.Strip(ReadOnlySpan{char}, System.Buffers.IBufferWriter{char}, IDiagnosticSink, StripOptions)"/>.</summary>
[Flags]
public enum StripOptions
{
    None = 0,

    /// <summary>
    /// Rewrite relative module specifiers ending in <c>.ts</c>, <c>.mts</c> or <c>.cts</c> to
    /// <c>.js</c>, <c>.mjs</c> or <c>.cjs</c> in <c>import</c>, <c>export ... from</c> and
    /// <c>import("...")</c> with a lone string argument. Declaration files (<c>.d.ts</c>) are left
    /// alone. Same as TypeScript's <c>rewriteRelativeImportExtensions</c>. The rewrite keeps
    /// the length, so positions still hold.
    /// </summary>
    RewriteRelativeImportExtensions = 1,
}
