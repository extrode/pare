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

    [Fact]
    public void Null_output_or_sink_throws()
    {
        Assert.Throws<ArgumentNullException>(() => TypeStripper.Strip("x", null!, new RecordingSink()));
        Assert.Throws<ArgumentNullException>(() => TypeStripper.Strip("x", new ArrayBufferWriter<char>(), null!));
    }
}

internal sealed class RecordingSink : IDiagnosticSink
{
    public List<Diagnostic> Items { get; } = [];
    public void Report(in Diagnostic diagnostic) => Items.Add(diagnostic);
}
