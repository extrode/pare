using System.Buffers;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Unicode;

namespace Extrode.Pare.Native;

/// <summary>One rejected construct as <c>pare_diagnostic</c> in pare.h lays it out.</summary>
[StructLayout(LayoutKind.Sequential)]
internal readonly record struct NativeDiagnostic(int Code, int Line, int Column, int Start, int Length);

/// <summary>
/// The managed half of <c>pare_strip</c>: UTF-8 in, UTF-8 out, diagnostics with byte offsets.
/// Kept free of pointers so the unit tests can call it under the JIT.
/// </summary>
internal static class NativeStrip
{
    public const int Ok = 0;
    public const int Rejected = 1;
    public const int Invalid = 2;
    public const int Internal = 3;

    public const uint KnownOptions = (uint)StripOptions.RewriteRelativeImportExtensions;

    private static ReadOnlySpan<byte> Bom => [0xEF, 0xBB, 0xBF];

    /// <summary>
    /// Strips <paramref name="utf8"/> into <paramref name="output"/>, which must hold at least
    /// <c>utf8.Length</c> bytes (stripping never grows UTF-8). A leading BOM is dropped, as
    /// <c>File.ReadAllText</c> does for the CLI; diagnostic offsets still count it.
    /// </summary>
    public static int Strip(ReadOnlySpan<byte> utf8, uint options, Span<byte> output, out int outputLength,
        List<NativeDiagnostic> diagnostics)
    {
        outputLength = 0;
        if ((options & ~KnownOptions) != 0)
            return Invalid;

        int bom = utf8.StartsWith(Bom) ? Bom.Length : 0;
        var body = utf8[bom..];
        char[] source = ArrayPool<char>.Shared.Rent(Math.Max(body.Length, 1));
        char[] stripped = ArrayPool<char>.Shared.Rent(Math.Max(body.Length, 1));
        try
        {
            if (Utf8.ToUtf16(body, source, out _, out int length, replaceInvalidSequences: false) != OperationStatus.Done)
                return Invalid;
            var text = source.AsSpan(0, length);

            var writer = new SpanWriter(stripped);
            var sink = new ListSink();
            if (TypeStripper.Strip(text, writer, sink, (StripOptions)options))
            {
                Utf8.FromUtf16(stripped.AsSpan(0, writer.Written), output, out _, out outputLength);
                return Ok;
            }
            Locate(text, bom, sink.Items, diagnostics);
            return Rejected;
        }
        finally
        {
            ArrayPool<char>.Shared.Return(source);
            ArrayPool<char>.Shared.Return(stripped);
        }
    }

    /// <summary>
    /// Adds byte offsets, lines and UTF-16 columns in one forward pass: the stripper reports in
    /// source order, so the cursor never moves back. Line rules match
    /// <see cref="SourceLocation.FromOffset"/>.
    /// </summary>
    private static void Locate(ReadOnlySpan<char> text, int bom, List<Diagnostic> found, List<NativeDiagnostic> located)
    {
        int i = 0, bytes = bom, line = 1, lineStart = 0;
        foreach (var d in found)
        {
            if (d.Start < i)
                (i, bytes, line, lineStart) = (0, bom, 1, 0);
            for (; i < d.Start; i++)
            {
                char c = text[i];
                bytes += Utf8Length(text, i);
                if (c is '\n' or '\u2028' or '\u2029' || (c == '\r' && (i + 1 >= text.Length || text[i + 1] != '\n')))
                {
                    line++;
                    lineStart = i + 1;
                }
            }
            int length = Encoding.UTF8.GetByteCount(text.Slice(d.Start, d.Length));
            located.Add(new NativeDiagnostic((int)d.Code, line, d.Start - lineStart + 1, bytes, length));
        }
    }

    // Bytes for text[i] in UTF-8; a surrogate pair counts 4 on its high half and 0 on its low.
    private static int Utf8Length(ReadOnlySpan<char> text, int i)
    {
        char c = text[i];
        if (c < 0x80) return 1;
        if (c < 0x800) return 2;
        if (char.IsHighSurrogate(c) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1])) return 4;
        if (char.IsLowSurrogate(c) && i > 0 && char.IsHighSurrogate(text[i - 1])) return 0;
        return 3;
    }

    private sealed class SpanWriter(char[] buffer) : IBufferWriter<char>
    {
        public int Written { get; private set; }
        public void Advance(int count) => Written += count;
        public Memory<char> GetMemory(int sizeHint = 0) => buffer.AsMemory(Written);
        public Span<char> GetSpan(int sizeHint = 0) => buffer.AsSpan(Written);
    }

    private sealed class ListSink : IDiagnosticSink
    {
        public List<Diagnostic> Items { get; } = [];
        public void Report(in Diagnostic diagnostic) => Items.Add(diagnostic);
    }
}
