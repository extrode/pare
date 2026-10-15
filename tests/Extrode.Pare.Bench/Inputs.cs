using System.Buffers;
using System.Text.RegularExpressions;

namespace Extrode.Pare.Bench;

/// <summary>Benchmark and comparison inputs from <c>.corpus/realworld</c> (<c>scripts/get-bench-corpus.cs</c>).</summary>
internal static partial class Inputs
{
    public const string Checker = "checker.ts";
    public const string RealWorld = "real-world corpus";
    public const string Nested = "nested parens x1000";

    public static readonly string RepoRoot = FindRepoRoot();
    public static readonly string CorpusRoot = Path.Combine(RepoRoot, ".corpus", "realworld");

    public static bool CorpusPresent => Directory.Exists(Path.Combine(CorpusRoot, "typescript"));

    /// <summary>One input: a name and its sources (one per file).</summary>
    public static string[] Load(string name) => name switch
    {
        Checker => [AmbientCheckerTs()],
        RealWorld => [.. Strippable().Select(f => f.Text)],
        Nested => [string.Concat(Enumerable.Repeat("(a = ", 1000)) + "b" + new string(')', 1000) + ";\n"],
        _ => throw new ArgumentException(name),
    };

    /// <summary>UTF-8 size of an input, the unit of the MB/s column.</summary>
    public static long Bytes(string name) => Load(name).Sum(s => (long)System.Text.Encoding.UTF8.GetByteCount(s));

    /// <summary>
    /// TypeScript 5.9.3's <c>checker.ts</c> (3.1 MB) with its 19 enums and 2 namespaces made
    /// ambient (<c>declare enum</c>, <c>declare namespace</c>), so it strips instead of being
    /// rejected. Nothing else changes.
    /// </summary>
    public static string AmbientCheckerTs()
    {
        string text = File.ReadAllText(Path.Combine(CorpusRoot, "typescript", "src", "compiler", "checker.ts"));
        return ValueDeclaration().Replace(text, "$1$2declare $3");
    }

    [GeneratedRegex(@"^(\s*)(export\s+)?((?:const\s+)?enum\s+(?=[\w$])|namespace\s+(?=[\w$]))", RegexOptions.Multiline)]
    private static partial Regex ValueDeclaration();

    /// <summary>Every .ts, .mts and .cts file (not .d.ts, not node_modules) that Pare strips, sorted by path.</summary>
    public static List<(string Path, string Relative, string Text)> Strippable()
    {
        var result = new List<(string, string, string)>();
        var output = new ArrayBufferWriter<char>();
        var sink = new NullSink();
        foreach (string file in Directory.EnumerateFiles(CorpusRoot, "*", SearchOption.AllDirectories).Order(StringComparer.Ordinal))
        {
            string relative = Path.GetRelativePath(CorpusRoot, file);
            string ext = Path.GetExtension(file);
            if (ext is not (".ts" or ".mts" or ".cts") || file.EndsWith(".d.ts", StringComparison.Ordinal) ||
                file.EndsWith(".d.mts", StringComparison.Ordinal) || file.EndsWith(".d.cts", StringComparison.Ordinal) ||
                relative.Contains("node_modules", StringComparison.Ordinal) || relative.Contains(".git", StringComparison.Ordinal))
                continue;
            string text = File.ReadAllText(file);
            output.ResetWrittenCount();
            if (TypeStripper.Strip(text, output, sink))
                result.Add((file, relative, text));
        }
        return result;
    }

    internal sealed class NullSink : IDiagnosticSink
    {
        public void Report(in Diagnostic diagnostic) { }
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Pare.slnx")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("Pare.slnx not found above the bench output");
    }
}
