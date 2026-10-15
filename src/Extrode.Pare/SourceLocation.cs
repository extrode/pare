using System.Buffers;

namespace Extrode.Pare;

/// <summary>
/// A 1-based line and column. Columns count UTF-16 code units, which is what JS engines report
/// in stack traces, so a column here matches the column a browser shows for the stripped output.
/// </summary>
public readonly record struct SourceLocation(int Line, int Column)
{
    // ECMAScript LineTerminator: LF, CR, LS, PS. CRLF counts as one terminator.
    private static readonly SearchValues<char> LineTerminators = SearchValues.Create("\n\r\u2028\u2029");

    public static SourceLocation FromOffset(ReadOnlySpan<char> source, int offset)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(offset);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(offset, source.Length);

        int line = 1;
        int lineStart = 0;
        int i = 0;
        while (i < offset)
        {
            int next = source[i..offset].IndexOfAny(LineTerminators);
            if (next < 0)
                break;
            i += next;
            if (source[i] == '\r' && i + 1 < source.Length && source[i + 1] == '\n')
            {
                // An offset pointing at the LF of a CRLF is still on the CR's line.
                if (i + 1 == offset)
                    break;
                i++;
            }
            i++;
            line++;
            lineStart = i;
        }
        return new SourceLocation(line, offset - lineStart + 1);
    }
}
