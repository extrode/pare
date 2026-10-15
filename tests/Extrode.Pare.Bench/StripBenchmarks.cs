using System.Buffers;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Columns;
using BenchmarkDotNet.Reports;
using BenchmarkDotNet.Running;

namespace Extrode.Pare.Bench;

/// <summary>Handoff section 10.7: throughput and allocations per <c>Strip</c> call, pools warm.</summary>
[MemoryDiagnoser]
public class StripBenchmarks
{
    private string[] _sources = [];
    private readonly ArrayBufferWriter<char> _output = new(4 << 20);
    private readonly Inputs.NullSink _sink = new();

    [ParamsSource(nameof(Names))]
    public string Input { get; set; } = "";

    public static IEnumerable<string> Names => [Inputs.Checker, Inputs.RealWorld, Inputs.Nested];

    [GlobalSetup]
    public void Setup() => _sources = Inputs.Load(Input);

    [Benchmark]
    public int Strip()
    {
        int ok = 0;
        foreach (string source in _sources)
        {
            _output.ResetWrittenCount();
            if (TypeStripper.Strip(source, _output, _sink))
                ok++;
        }
        return ok;
    }
}

/// <summary>Input size divided by the mean time.</summary>
public sealed class ThroughputColumn : IColumn
{
    private readonly Dictionary<string, long> _bytes = [];

    public string Id => "Throughput";
    public string ColumnName => "MB/s";
    public bool AlwaysShow => true;
    public ColumnCategory Category => ColumnCategory.Custom;
    public int PriorityInCategory => 0;
    public bool IsNumeric => true;
    public UnitType UnitType => UnitType.Dimensionless;
    public string Legend => "Input bytes (UTF-8) / mean time, in 10^6 bytes per second";

    public string GetValue(Summary summary, BenchmarkCase benchmarkCase)
    {
        var mean = summary[benchmarkCase]?.ResultStatistics?.Mean;
        if (mean is null || benchmarkCase.Parameters["Input"] is not string input)
            return "-";
        if (!_bytes.TryGetValue(input, out long bytes))
            _bytes[input] = bytes = Inputs.Bytes(input);
        return (bytes / (mean.Value / 1e9) / 1e6).ToString("F1");
    }

    public string GetValue(Summary summary, BenchmarkCase benchmarkCase, SummaryStyle style) => GetValue(summary, benchmarkCase);
    public bool IsDefault(Summary summary, BenchmarkCase benchmarkCase) => false;
    public bool IsAvailable(Summary summary) => true;
}
