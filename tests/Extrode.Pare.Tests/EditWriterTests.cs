using Extrode.Pare.Parsing;

namespace Extrode.Pare.Tests;

public class EditWriterTests
{
    [Fact]
    public void Replace_inside_an_erased_range_is_skipped()
    {
        const string source = "x \"./a.ts\";";
        Edit[] edits = [new Edit(7, 8, Replace: 'j'), new Edit(0, source.Length)];
        var dest = new char[source.Length];

        EditWriter.Write(source, edits, dest);

        Assert.Equal(new string(' ', source.Length), new string(dest));
    }
}
