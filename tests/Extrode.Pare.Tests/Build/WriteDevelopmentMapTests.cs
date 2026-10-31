using System.Text.Json;
using Extrode.Pare.Build;
using Microsoft.Build.Utilities;

namespace Extrode.Pare.Tests.Build;

public sealed class WriteDevelopmentMapTests : IDisposable
{
    private readonly DirectoryInfo _dir = Directory.CreateTempSubdirectory("pare-map-");
    private readonly Engine _engine = new();

    public void Dispose() => _dir.Delete(recursive: true);

    private static TaskItem Item(string source, string output, string? compiler = null)
    {
        var item = new TaskItem(source);
        item.SetMetadata("PareOutputPath", output);
        if (compiler is not null)
            item.SetMetadata("PareCompiler", compiler);
        return item;
    }

    private WriteDevelopmentMap Task(string file, params TaskItem[] sources) => new()
    {
        BuildEngine = _engine,
        File = file,
        SourceRoot = "/app/wwwroot/",
        OutputRoot = "/app/obj/pare/",
        Sources = sources,
    };

    [Fact]
    public void Paths_with_quotes_backslashes_and_apostrophes_survive_a_round_trip()
    {
        string file = Path.Combine(_dir.FullName, "map.json");
        string name = OperatingSystem.IsWindows() ? "b's\\x" : "q\"uote's";
        var item = Item(Path.Combine(_dir.FullName, name + ".ts"), "obj/pare/" + name + ".js");
        string source = item.GetMetadata("FullPath");
        string output = item.GetMetadata("PareOutputPath");

        Assert.True(Task(file, item).Execute());

        using var map = JsonDocument.Parse(File.ReadAllText(file));
        var entry = map.RootElement.GetProperty("sources")[0];
        Assert.Equal(source, entry.GetProperty("source").GetString());
        Assert.Equal(output, entry.GetProperty("output").GetString());
        Assert.Equal("/app/wwwroot/", map.RootElement.GetProperty("sourceRoot").GetString());
    }

    [Fact]
    public void Compiled_sources_carry_their_compiler()
    {
        string file = Path.Combine(_dir.FullName, "map.json");
        var task = Task(file);
        task.Compiled = [Item(Path.Combine(_dir.FullName, "A.up"), "obj/A.up.js", "upper")];

        Assert.True(task.Execute());

        using var map = JsonDocument.Parse(File.ReadAllText(file));
        Assert.Equal("upper", map.RootElement.GetProperty("compiled")[0].GetProperty("compiler").GetString());
        Assert.Equal(0, map.RootElement.GetProperty("sources").GetArrayLength());
    }

    [Fact]
    public void An_unchanged_map_is_not_written_again()
    {
        string file = Path.Combine(_dir.FullName, "map.json");
        var item = Item(Path.Combine(_dir.FullName, "a.ts"), "obj/a.js");
        Assert.True(Task(file, item).Execute());
        var stamp = new DateTime(2001, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        File.SetLastWriteTimeUtc(file, stamp);

        Assert.True(Task(file, item).Execute());

        Assert.Equal(stamp, File.GetLastWriteTimeUtc(file));
    }

    [Fact]
    public void A_file_that_cannot_be_written_is_an_error()
    {
        string blocked = Path.Combine(_dir.FullName, "dir");
        Directory.CreateDirectory(blocked);

        Assert.False(Task(blocked).Execute());

        Assert.Single(_engine.Errors);
    }
}
