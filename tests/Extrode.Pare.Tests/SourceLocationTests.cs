namespace Extrode.Pare.Tests;

public class SourceLocationTests
{
    [Theory]
    [InlineData("abc", 0, 1, 1)]
    [InlineData("abc", 3, 1, 4)]
    [InlineData("a\nb", 2, 2, 1)]
    [InlineData("a\r\nb", 3, 2, 1)]
    [InlineData("a\r\nb", 2, 1, 3)]
    [InlineData("a\rb", 2, 2, 1)]
    [InlineData("a\u2028b\u2029c", 4, 3, 1)]
    [InlineData("\n\n\nxy", 4, 4, 2)]
    [InlineData("a\n", 2, 2, 1)]
    public void FromOffset_counts_every_ecmascript_line_terminator(string source, int offset, int line, int column)
    {
        Assert.Equal(new SourceLocation(line, column), SourceLocation.FromOffset(source, offset));
    }

    [Fact]
    public void FromOffset_counts_columns_in_utf16_code_units()
    {
        // U+1F600 is a surrogate pair, two code units, as a JS engine counts it.
        Assert.Equal(new SourceLocation(1, 4), SourceLocation.FromOffset("\U0001F600x", 3));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(4)]
    public void FromOffset_rejects_offsets_outside_the_source(int offset)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => SourceLocation.FromOffset("abc", offset));
    }
}
