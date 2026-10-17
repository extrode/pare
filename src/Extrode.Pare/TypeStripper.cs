using System.Buffers;
using Extrode.Pare.Parsing;

namespace Extrode.Pare;

/// <summary>
/// Strips TypeScript type syntax by overwriting it with spaces, keeping every line terminator,
/// so each surviving character stays at the same line and column.
/// </summary>
public static class TypeStripper
{
    /// <summary>
    /// Writes the stripped form of <paramref name="source"/> to <paramref name="output"/>.
    /// Returns false when the source has a syntax error or non-erasable syntax; each problem goes
    /// to <paramref name="diagnostics"/> in source order. Output is written only when the result is true.
    /// </summary>
    public static bool Strip(ReadOnlySpan<char> source, IBufferWriter<char> output, IDiagnosticSink diagnostics) =>
        Strip(source, output, diagnostics, StripOptions.None);

    /// <summary>
    /// Returns the stripped form of <paramref name="source"/>, or null when the source has a syntax
    /// error or non-erasable syntax; <paramref name="diagnostics"/> holds each problem in source
    /// order. Allocates the result; the <see cref="IBufferWriter{T}"/> overload does not.
    /// </summary>
    public static string? Strip(string source, out IReadOnlyList<Diagnostic> diagnostics)
    {
        ArgumentNullException.ThrowIfNull(source);
        var output = new ArrayBufferWriter<char>(Math.Max(source.Length, 1));
        var sink = new ListSink();
        bool ok = Strip(source, output, sink);
        diagnostics = sink.Items;
        return ok ? output.WrittenSpan.ToString() : null;
    }

    /// <inheritdoc cref="Strip(ReadOnlySpan{char}, IBufferWriter{char}, IDiagnosticSink)"/>
    /// <param name="options">Optional rewrites; see <see cref="StripOptions"/>.</param>
    public static bool Strip(ReadOnlySpan<char> source, IBufferWriter<char> output, IDiagnosticSink diagnostics,
        StripOptions options)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(diagnostics);

        var parser = new Parser(source, (options & StripOptions.RewriteRelativeImportExtensions) != 0);
        try
        {
            parser.ParseModule();
            var rejected = parser.Diagnostics;
            if (!parser.Ok || rejected.Length > 0)
            {
                bool syntaxReported = parser.Ok;
                foreach (var d in rejected)
                {
                    if (!syntaxReported && parser.ErrorStart < d.Start)
                    {
                        diagnostics.Report(new Diagnostic(DiagnosticCode.SyntaxError, parser.ErrorStart, parser.ErrorLength));
                        syntaxReported = true;
                    }
                    diagnostics.Report(d);
                }
                if (!syntaxReported)
                    diagnostics.Report(new Diagnostic(DiagnosticCode.SyntaxError, parser.ErrorStart, parser.ErrorLength));
                return false;
            }

            var dest = output.GetSpan(source.Length);
            EditWriter.Write(source, parser.MutableEdits, dest);
            output.Advance(source.Length);
            return true;
        }
        finally
        {
            parser.Dispose();
        }
    }

    private sealed class ListSink : IDiagnosticSink
    {
        public List<Diagnostic> Items { get; } = [];
        public void Report(in Diagnostic diagnostic) => Items.Add(diagnostic);
    }
}
