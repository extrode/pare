using System.Buffers;
using System.Diagnostics;
using System.Reflection;
using System.Text;
using Acornima;
using Acornima.Ast;

namespace Extrode.Pare.Conformance;

/// <summary>
/// Handoff section 10.4: for every corpus file Pare strips, compares Pare's output with the
/// TypeScript 7 compiler's emit by parsing both with Acornima and comparing the trees without
/// locations. tsc runs once over all files with <c>module: preserve</c>, which keeps import and
/// export syntax as written, as a type stripper does.
/// </summary>
public class DifferentialTests(ITestOutputHelper output)
{
    private static readonly string? Tsc = FindTsc();

    private sealed class Sink : IDiagnosticSink
    {
        public void Report(in Diagnostic diagnostic) { }
    }

    [Fact]
    public void Output_matches_tsc_emit()
    {
        Corpus.SkipIfMissing();
        Assert.SkipWhen(Tsc is null, "tsc missing: run `dotnet run scripts/get-tsgo.cs`");

        var dir = Directory.CreateTempSubdirectory("pare-differential-");
        try
        {
            Directory.CreateDirectory(Path.Combine(dir.FullName, "in"));
            var files = new List<(string Where, string Name, string Pare)>();
            var buffer = new ArrayBufferWriter<char>();
            var sink = new Sink();
            foreach (var (relative, name, unit) in Corpus.StripUnits())
            {
                string ext = Path.GetExtension(name).ToLowerInvariant();
                if (ext is not (".ts" or ".mts" or ".cts"))
                    continue;
                buffer.ResetWrittenCount();
                if (!TypeStripper.Strip(unit, buffer, sink))
                    continue;
                string file = $"{files.Count:D5}{ext}";
                File.WriteAllText(Path.Combine(dir.FullName, "in", file), unit);
                files.Add(($"{relative} ({name})", file, buffer.WrittenSpan.ToString()));
            }
            File.WriteAllText(Path.Combine(dir.FullName, "tsconfig.json"), """
                {
                  "compilerOptions": {
                    "target": "esnext",
                    "module": "preserve",
                    "verbatimModuleSyntax": true,
                    "noCheck": true,
                    "noResolve": true,
                    "noLib": true,
                    "types": [],
                    "noEmitOnError": false,
                    "newLine": "lf",
                    "rootDir": "in",
                    "outDir": "out"
                  },
                  "include": ["in"]
                }
                """);
            RunTsc(dir.FullName);

            var parser = new Parser(StripCorpusTests.JsOptions);
            int same = 0, pareInvalid = 0;
            var missing = new List<string>();
            var different = new List<string>();
            foreach (var (where, name, pare) in files)
            {
                string emitted = Path.Combine(dir.FullName, "out", Path.ChangeExtension(name, name[^3..] switch
                {
                    "mts" => ".mjs",
                    "cts" => ".cjs",
                    _ => ".js",
                }));
                if (!File.Exists(emitted))
                {
                    missing.Add(where);
                    continue;
                }
                string tsc = File.ReadAllText(emitted);
                // TypeScript 7 always emits "use strict" in scripts (alwaysStrict=false is gone).
                const string UseStrict = "\"use strict\";\n";
                if (tsc.StartsWith(UseStrict, StringComparison.Ordinal) && !pare.TrimStart().StartsWith("\"use strict\"", StringComparison.Ordinal))
                    tsc = tsc[UseStrict.Length..];
                var (pareTree, tscTree, mode) = ParseBoth(parser, pare, tsc);
                if (pareTree is null)
                {
                    // StripCorpusTests owns invalid output.
                    pareInvalid++;
                    continue;
                }
                if (tscTree is null)
                {
                    different.Add($"{where}: tsc output does not parse as a {mode}");
                    continue;
                }
                string a = Dump(pareTree), b = Dump(tscTree);
                if (a == b)
                    same++;
                else
                    different.Add($"{where}: {FirstDifference(a, b)}");
            }

            int compared = same + different.Count;
            output.WriteLine($"{files.Count} stripped .ts/.mts/.cts files: {same} match tsc, {different.Count} differ, " +
                             $"{missing.Count} not emitted, {pareInvalid} invalid Pare output; " +
                             $"{100.0 * same / Math.Max(1, compared):F2}% match");
            var report = Environment.GetEnvironmentVariable("PARE_DIFFERENTIAL_REPORT");
            if (report is not null)
                File.WriteAllLines(report, [.. different, "--- not emitted", .. missing]);

            Assert.True(missing.Count == 0, Report("not emitted by tsc", missing));
            Assert.True(different.Count == 0, Report("differ from tsc", different));
        }
        finally
        {
            dir.Delete(recursive: true);
        }
    }

    private static void RunTsc(string dir)
    {
        var start = new ProcessStartInfo(Tsc!, ["-p", Path.Combine(dir, "tsconfig.json")])
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        using var process = Process.Start(start)!;
        var stderr = process.StandardError.ReadToEndAsync();
        // Diagnostics are expected (noCheck still reports some); only the emit matters.
        process.StandardOutput.ReadToEnd();
        process.WaitForExit();
        _ = stderr.Result;
    }

    /// <summary>Parses both as a module, or both as a script when either is not a valid module.</summary>
    private static (Node? Pare, Node? Tsc, string Mode) ParseBoth(Parser parser, string pare, string tsc)
    {
        var a = TryParse(() => parser.ParseModule(pare));
        var b = TryParse(() => parser.ParseModule(tsc));
        if (a is not null && b is not null)
            return (a, b, "module");
        a = TryParse(() => parser.ParseScript(pare)) ?? a;
        b = TryParse(() => parser.ParseScript(tsc)) ?? b;
        return (a, b, "script");
    }

    private static Node? TryParse(Func<Node> parse)
    {
        try
        {
            return parse();
        }
        catch (Exception)
        {
            // Acornima 1.8.0 can throw NullReferenceException on `export` in script mode.
            return null;
        }
    }

    /// <summary>
    /// The tree as text: node types and scalar properties, no locations. Leaves out what is not
    /// behavior: empty statements in a statement list (Pare's ASI guard <c>;</c>, handoff 7.5) and
    /// a bare <c>export {}</c> (tsc keeps or adds it to mark a module).
    /// </summary>
    internal static string Dump(Node root)
    {
        var sb = new StringBuilder();
        // Iterative: corpus files nest deep enough to overflow a recursive walk.
        var stack = new Stack<object>();
        stack.Push(root);
        while (stack.Count > 0)
        {
            if (stack.Pop() is not Node node)
            {
                sb.Append(')');
                continue;
            }
            sb.Append(" (").Append(node.Type);
            foreach (var p in ScalarProperties(node.GetType()))
                sb.Append(' ').Append(p.Name).Append('=').Append(p.GetValue(node));
            stack.Push(")");
            var children = new List<Node>();
            foreach (var child in node.ChildNodes)
                children.Add(child);
            bool list = node.Type is NodeType.Program or NodeType.BlockStatement or NodeType.StaticBlock or NodeType.SwitchCase;
            for (int i = children.Count - 1; i >= 0; i--)
            {
                if (list && (children[i].Type == NodeType.EmptyStatement || IsEmptyExport(children[i])))
                    continue;
                stack.Push(children[i]);
            }
        }
        return sb.ToString();
    }

    private static bool IsEmptyExport(Node node) =>
        node is ExportNamedDeclaration { Declaration: null, Source: null, Specifiers.Count: 0 };

    private static readonly Dictionary<Type, PropertyInfo[]> Scalars = [];

    private static PropertyInfo[] ScalarProperties(Type type)
    {
        if (!Scalars.TryGetValue(type, out var props))
        {
            props = [.. type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Where(p => p.GetIndexParameters().Length == 0 && IsScalar(p.PropertyType) &&
                            p.Name is not ("Type" or "Start" or "End" or "Range" or "Location"))
                .OrderBy(p => p.Name, StringComparer.Ordinal)];
            Scalars[type] = props;
        }
        return props;
    }

    private static bool IsScalar(Type t) => t == typeof(string) || t == typeof(bool) || t.IsEnum;

    private static string FirstDifference(string a, string b)
    {
        int i = 0;
        while (i < a.Length && i < b.Length && a[i] == b[i])
            i++;
        int from = Math.Max(0, i - 60);
        return $"pare ...{a[from..Math.Min(a.Length, i + 60)]}... tsc ...{b[from..Math.Min(b.Length, i + 60)]}...";
    }

    private static string Report(string what, List<string> lines)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"{lines.Count} files {what}:");
        foreach (var line in lines.Take(50))
            sb.AppendLine(line);
        return sb.ToString();
    }

    private static string? FindTsc()
    {
        for (var dir = AppContext.BaseDirectory; dir is not null; dir = Path.GetDirectoryName(dir))
        {
            if (File.Exists(Path.Combine(dir, "Pare.slnx")))
            {
                var tsc = Path.Combine(dir, ".tools", "typescript", OperatingSystem.IsWindows() ? "tsc.exe" : "tsc");
                return File.Exists(tsc) ? tsc : null;
            }
        }
        return null;
    }
}
