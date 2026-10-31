using Extrode.Pare.Build;
using Microsoft.Build.Utilities;

namespace Extrode.Pare.Tests.Build;

public sealed class LogPareDiagnosticsTests
{
    private static TaskItem Item(string spec, string serveAt)
    {
        var item = new TaskItem(spec);
        item.SetMetadata("PareServeAt", serveAt);
        return item;
    }

    [Fact]
    public void Logs_one_error_per_item_with_its_metadata_in_the_text()
    {
        var engine = new Engine();
        var task = new LogPareDiagnostics
        {
            BuildEngine = engine,
            Code = "PARE999",
            Text = "bad {PareServeAt} in {Identity}",
            Items = [Item("a.ts", "../x"), Item("b.ts", "c:/y")],
        };

        Assert.False(task.Execute());

        Assert.Collection(engine.Errors,
            e => { Assert.Equal("PARE999", e.Code); Assert.Equal("a.ts", e.File); Assert.Equal("bad ../x in a.ts", e.Message); },
            e => { Assert.Equal("b.ts", e.File); Assert.Equal("bad c:/y in b.ts", e.Message); });
    }

    [Fact]
    public void No_items_is_success()
    {
        var engine = new Engine();

        Assert.True(new LogPareDiagnostics { BuildEngine = engine, Code = "PARE999", Text = "x" }.Execute());

        Assert.Empty(engine.Errors);
    }
}
