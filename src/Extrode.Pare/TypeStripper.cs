using System.Buffers;

namespace Extrode.Pare;

/// <summary>
/// Strips TypeScript type syntax by overwriting it with spaces, keeping every line terminator,
/// so each surviving character stays at the same line and column.
/// </summary>
public static class TypeStripper
{
    /// <summary>
    /// Writes the stripped form of <paramref name="source"/> to <paramref name="output"/>.
    /// Returns false when non-erasable syntax was found; each occurrence goes to
    /// <paramref name="diagnostics"/>. Output is written only when the result is true.
    /// </summary>
    public static bool Strip(ReadOnlySpan<char> source, IBufferWriter<char> output, IDiagnosticSink diagnostics)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(diagnostics);

        // M0: no recognizer yet, so input passes through unchanged.
        output.Write(source);
        return true;
    }
}
