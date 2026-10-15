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

var config = DefaultConfig.Instance.AddColumn(new ThroughputColumn());
BenchmarkSwitcher.FromAssembly(typeof(StripBenchmarks).Assembly).Run(args, config);
return 0;
