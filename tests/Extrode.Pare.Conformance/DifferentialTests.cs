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

    private const string Tsc702 = "tsc 7.0.2 differs, Pare keeps the source";

    // Files where tsc's emit is not the source minus types; each checked by hand.
    private static readonly Dictionary<string, string> TscDeviations = new()
    {
        ["decoratorOnClassProperty1.es6.ts"] = Tsc702 + ": names a decorated `export default class` default_1, changing .name",
        ["decoratorOnClassMethod1.es6.ts"] = Tsc702 + ": names a decorated `export default class` default_1, changing .name",
        ["decoratorOnClassAccessor1.es6.ts"] = Tsc702 + ": names a decorated `export default class` default_1, changing .name",
        ["quotedConstructors.ts"] = Tsc702 + ": unquotes `\"constructor\"()`, the same constructor by spec",
    };

    private sealed class Sink : IDiagnosticSink
    {
        public void Report(in Diagnostic diagnostic) { }
    }

    [Fact]
    public void Output_matches_tsc_emit()
    {
        Corpus.SkipIfMissing();
        Assert.SkipWhen(Tsc is null, "tsc missing: run `dotnet run scripts/get-tsc.cs`");

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
            var unparsed = RunTsc(dir.FullName);

            var parser = new Parser(StripCorpusTests.JsOptions);
            int same = 0, pareInvalid = 0, deviations = 0, tscSyntax = 0;
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
                // The corpus is pinned to TypeScript main; tsc 7.0.2 predates some syntax in it
                // (`import source`). Its emit for a file it cannot parse is error recovery.
                if (unparsed.Contains(name))
                {
                    tscSyntax++;
                    continue;
                }
                if (!File.Exists(emitted))
                {
                    missing.Add(where);
                    continue;
                }
                string tsc = File.ReadAllText(emitted);
                // TypeScript 7 always emits "use strict" in scripts (alwaysStrict=false is gone), which
                // also makes sloppy-mode code like `with` invalid. Compare without the added line:
                // it was added when, without it, both outputs hold the same number of them.
                var (pareTree, tscTree, mode) = (default(Node), default(Node), "");
                if (WithoutAddedUseStrict(tsc) is { } plain)
                {
                    (pareTree, tscTree, mode) = ParseBoth(parser, pare, plain);
                    if (pareTree is null || tscTree is null || UseStrictCount(tscTree) != UseStrictCount(pareTree))
                        tscTree = null;
                }
                if (tscTree is null)
                    (pareTree, tscTree, mode) = ParseBoth(parser, pare, tsc);
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
                else if (TscDeviations.ContainsKey(Path.GetFileName(where[..where.IndexOf(' ')])))
                    deviations++;
                else
                    different.Add($"{where}: {FirstDifference(a, b)}");
            }

            int compared = same + deviations + different.Count;
            output.WriteLine($"{files.Count} stripped .ts/.mts/.cts files: {same} match tsc, {deviations} known tsc deviations, " +
                             $"{different.Count} differ, {tscSyntax} tsc syntax errors, {missing.Count} not emitted, " +
                             $"{pareInvalid} invalid Pare output; " +
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

    /// <summary>Runs tsc over the project; returns the files it reported a TS1xxx syntax error for.</summary>
    private static HashSet<string> RunTsc(string dir)
    {
        var start = new ProcessStartInfo(Tsc!, ["-p", Path.Combine(dir, "tsconfig.json")])
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        using var process = Process.Start(start)!;
        var stderr = process.StandardError.ReadToEndAsync();
        // Other diagnostics are expected (noLib, and noCheck still reports some); only the emit matters.
        var unparsed = new HashSet<string>(StringComparer.Ordinal);
        while (process.StandardOutput.ReadLine() is { } line)
        {
            // in/00012.ts(1,15): error TS1005: '=' expected.
            int paren = line.IndexOf('(');
            int code = line.IndexOf("): error TS1", StringComparison.Ordinal);
            if (paren > 0 && code > paren && code + 16 <= line.Length && line[code + 15] == ':')
                unparsed.Add(Path.GetFileName(line[..paren]));
        }
        process.WaitForExit();
        _ = stderr.Result;
        return unparsed;
    }

    /// <summary>tsc's output with a leading <c>"use strict";</c> line blanked (after a hashbang), else null.</summary>
    private static string? WithoutAddedUseStrict(string tsc)
    {
        const string UseStrict = "\"use strict\";\n";
        int at = tsc.StartsWith("#!", StringComparison.Ordinal) ? tsc.IndexOf('\n') + 1 : 0;
        if (at < 0 || string.CompareOrdinal(tsc, at, UseStrict, 0, UseStrict.Length) != 0)
            return null;
        return string.Concat(tsc.AsSpan(0, at), new string(' ', UseStrict.Length - 1), "\n", tsc.AsSpan(at + UseStrict.Length));
    }

    private static int UseStrictCount(Node program)
    {
        int n = 0;
        foreach (var statement in ((Program)program).Body)
        {
            if (statement is not Directive directive)
                break;
            if (directive.Value == "use strict")
                n++;
        }
        return n;
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
            {
                // tsc lowercases hex BigInt literals (0xFFFFn); the BigInt property keeps the value.
                if (p.Name == "Raw" && node is BigIntLiteral)
                    continue;
                sb.Append(' ').Append(p.Name).Append('=').Append(p.GetValue(node));
            }
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
