using System.Buffers;

namespace Extrode.Pare.Tests;

public class TypeStripperTests
{
    [Theory]
    [InlineData("")]
    [InlineData("const x = 1;\nexport function f(a) { return a < x; }\n")]
    [InlineData("let s = `a${b}c`;\r\n/re/g.test(s);\u2028")]
    public void Plain_javascript_passes_through_unchanged(string source)
    {
        var output = new ArrayBufferWriter<char>();
        var sink = new RecordingSink();

        Assert.True(TypeStripper.Strip(source, output, sink));
        Assert.Equal(source, output.WrittenSpan.ToString());
        Assert.Empty(sink.Items);
    }

    [Theory]
    [InlineData("", "")]
    [InlineData("let a: T = 1;\n", "let a    = 1;\n")]
    public void String_overload_returns_the_output_and_no_diagnostics(string source, string expected)
    {
        Assert.Equal(expected, TypeStripper.Strip(source, out var diagnostics));
        Assert.Empty(diagnostics);
    }

    [Fact]
    public void String_overload_returns_null_and_every_diagnostic_when_rejected()
    {
        Assert.Null(TypeStripper.Strip("enum E { A }\nlet x = <T>y;\n", out var diagnostics));
        Assert.Equal([DiagnosticCode.Enum, DiagnosticCode.AngleBracketAssertion], diagnostics.Select(d => d.Code));
        Assert.Equal(0, diagnostics[0].Start);
    }

    [Theory]
    [InlineData("x = ", "(", "a", ")")]
    [InlineData("x = ", "[", "a", "]")]
    [InlineData("", "{", "", "}")]
    [InlineData("", "function f() {", "", "}")]
    [InlineData("let x: ", "A<", "B", ">")]
    [InlineData("let x: ", "{ a: ", "B", " }")]
    [InlineData("", "(a = ", "1", ") => a")]
    [InlineData("", "<T>(a: T = ", "1", ") => a")]
    public void Nesting_too_deep_for_the_stack_is_a_syntax_error(string head, string open, string inner, string close)
    {
        const int depth = 200_000;
        string source = head + string.Concat(Enumerable.Repeat(open, depth)) + inner + string.Concat(Enumerable.Repeat(close, depth)) + ";";

        Assert.Null(TypeStripper.Strip(source, out var diagnostics));
        Assert.Equal([DiagnosticCode.SyntaxError], diagnostics.Select(d => d.Code));
    }

    [Fact]
    public void Null_output_or_sink_throws()
    {
        Assert.Throws<ArgumentNullException>(() => TypeStripper.Strip("x", null!, new RecordingSink()));
        Assert.Throws<ArgumentNullException>(() => TypeStripper.Strip("x", new ArrayBufferWriter<char>(), null!));
        Assert.Throws<ArgumentNullException>(() => TypeStripper.Strip(null!, out _));
    }
}

internal sealed class RecordingSink : IDiagnosticSink
{
    public List<Diagnostic> Items { get; } = [];
    public void Report(in Diagnostic diagnostic) => Items.Add(diagnostic);
}
