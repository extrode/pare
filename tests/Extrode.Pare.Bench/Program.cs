using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Running;
using Extrode.Pare.Bench;

if (!Inputs.CorpusPresent)
{
    Console.Error.WriteLine("real-world corpus missing: run `dotnet run scripts/get-bench-corpus.cs`");
    return 2;
}
if (args.Length > 0 && args[0] == "compare")
    return Compare.Run(args[1..]);

// BenchmarkDotNet's out-of-process run finds its project file by name and stops ("Found more than
// one matching project file") when Claude Code worktrees under .claude/worktrees hold copies of
// it. Run in process then, as the 0.5 and later numbers were.
if (!args.Contains("--inProcess") && !args.Contains("-i") && WorktreeCopies())
{
    Console.Error.WriteLine("note: .claude/worktrees holds copies of this project; running --inProcess (remove the worktrees for an out-of-process run)");
    args = [.. args, "--inProcess"];
}

var config = DefaultConfig.Instance.AddColumn(new ThroughputColumn());
BenchmarkSwitcher.FromAssembly(typeof(StripBenchmarks).Assembly).Run(args, config);
return 0;

static bool WorktreeCopies()
{
    for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
    {
        if (File.Exists(Path.Combine(dir.FullName, "Pare.slnx")))
        {
            string worktrees = Path.Combine(dir.FullName, ".claude", "worktrees");
            return Directory.Exists(worktrees) &&
                Directory.EnumerateFiles(worktrees, "Extrode.Pare.Bench.csproj", SearchOption.AllDirectories).Any();
        }
    }
    return false;
}
