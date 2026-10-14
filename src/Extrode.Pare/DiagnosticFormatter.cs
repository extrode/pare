namespace Extrode.Pare;

/// <summary>Formats a diagnostic as <c>file:line:col: error PARE100: message (hint)</c>.</summary>
public static class DiagnosticFormatter
{
    public static string Format(string fileName, ReadOnlySpan<char> source, in Diagnostic diagnostic)
    {
        var at = SourceLocation.FromOffset(source, diagnostic.Start);
        return $"{fileName}:{at.Line}:{at.Column}: error PARE{(int)diagnostic.Code:000}: " +
               $"{DiagnosticMessages.Message(diagnostic.Code)} ({DiagnosticMessages.Hint(diagnostic.Code)})";
    }
}
