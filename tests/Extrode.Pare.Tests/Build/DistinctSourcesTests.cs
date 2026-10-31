using Extrode.Pare.Build;
using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;

namespace Extrode.Pare.Tests.Build;

public sealed class DistinctSourcesTests
{
    private readonly Engine _engine = new();

    private static TaskItem Item(string spec, string serveAt = "")
    {
        var item = new TaskItem(spec);
        if (serveAt != "")
            item.SetMetadata("PareServeAt", serveAt);
        return item;
    }

    private DistinctSources Run(TaskItem[] sources, string[] recursiveDirs)
    {
        var task = new DistinctSources
        {
            BuildEngine = _engine,
            Sources = sources,
            RecursiveDirs = [.. recursiveDirs.Select(d => (ITaskItem)new TaskItem("|" + d))],
        };
        Assert.True(task.Execute());
        return task;
    }

    [Fact]
    public void The_last_declaration_wins_in_the_first_ones_place()
    {
        var task = Run([Item("wwwroot/js/a.ts"), Item("wwwroot/js/b.ts"), Item("wwwroot/js/a.ts", "lib")], ["js/", "js/", ""]);

        Assert.Equal(["wwwroot/js/a.ts", "wwwroot/js/b.ts"], task.Distinct.Select(i => i.ItemSpec));
        Assert.Equal("lib", task.Distinct[0].GetMetadata("PareServeAt"));
        Assert.Equal("", task.Distinct[0].GetMetadata("_PareRecursiveDir"));
        Assert.Equal("js/", task.Distinct[1].GetMetadata("_PareRecursiveDir"));
    }

    [Fact]
    public void Distinct_sources_keep_their_order_and_recursive_dirs()
    {
        var task = Run([Item("c.ts"), Item("x/a.ts"), Item("b.ts")], ["", "x/", ""]);

        Assert.Equal(["c.ts", "x/a.ts", "b.ts"], task.Distinct.Select(i => i.ItemSpec));
        Assert.Equal(["", "x/", ""], task.Distinct.Select(i => i.GetMetadata("_PareRecursiveDir")));
    }

    [Fact]
    public void Both_separators_name_one_file()
    {
        var task = Run([Item("wwwroot/js/a.ts"), Item(@"wwwroot\js\a.ts", "lib")], ["js/", ""]);

        Assert.Equal("lib", Assert.Single(task.Distinct).GetMetadata("PareServeAt"));
    }

    [Fact]
    public void Case_decides_on_linux_only()
    {
        var task = Run([Item("a.ts"), Item("A.ts")], ["", ""]);

        Assert.Equal(OperatingSystem.IsLinux() ? 2 : 1, task.Distinct.Length);
    }

    [Fact]
    public void Mismatched_lists_fail_the_task()
    {
        var task = new DistinctSources { BuildEngine = _engine, Sources = [Item("a.ts")], RecursiveDirs = [] };

        Assert.False(task.Execute());
    }
}
