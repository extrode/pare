using System.Buffers;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Acornima;
using Acornima.Ast;
using Extrode.Pare.Conformance;

namespace Extrode.Pare.Bench;

/// <summary>
/// Runs Pare and other TypeScript-to-JavaScript tools over the real-world corpus files Pare strips,
/// checks each tool's output against Pare's and times them. Whitespace strippers (Node's
/// <c>stripTypeScriptTypes</c>, ts-blank-space) are compared byte for byte; emitters that reprint
/// the code (esbuild, oxc, Deno's swc emit) as Acornima trees without locations or literal raw text.
/// Every tool runs in-process over strings already in memory, except Deno, which is timed as one
/// <c>deno cache</c> process against one <c>pare</c> CLI process over the same files.
/// </summary>
internal static partial class Compare
{
    private static readonly string[] NodeTools = ["node-strip", "ts-blank-space", "esbuild", "oxc"];

    public static int Run(string[] args)
    {
        int passes = 10;
        for (int i = 0; i < args.Length; i++)
        {
            if (args[i] == "--passes" && i + 1 < args.Length && int.TryParse(args[++i], out passes) && passes > 0)
                continue;
            Console.Error.WriteLine("usage: compare [--passes <n>]");
            return 2;
        }
        string tools = Path.Combine(Inputs.RepoRoot, "tests", "Extrode.Pare.Bench", "compare");
        if (!Directory.Exists(Path.Combine(tools, "node_modules")))
        {
            Console.Error.WriteLine($"comparison tools missing: npm ci --prefix {Path.GetRelativePath(Inputs.RepoRoot, tools)}");
            return 2;
        }

        string outRoot = Path.Combine(Inputs.RepoRoot, "tmp", "compare");
        if (Directory.Exists(outRoot))
            Directory.Delete(outRoot, recursive: true);
        Directory.CreateDirectory(outRoot);

        var files = Inputs.Strippable();
        files.Add((Path.Combine(outRoot, "checker.ambient.ts"), "typescript/checker.ambient.ts", Inputs.AmbientCheckerTs()));
        File.WriteAllText(files[^1].Path, files[^1].Text);
        string list = Path.Combine(outRoot, "files.txt");
        File.WriteAllLines(list, files.Select(f => $"{f.Path}\t{f.Relative}"));
        Console.WriteLine($"{files.Count} files Pare strips, {files.Sum(f => (long)Encoding.UTF8.GetByteCount(f.Text)) / 1e6:F1} MB; {passes} timed passes each");

        var results = new List<Result> { RunPare(files, outRoot, passes) };
        foreach (string tool in NodeTools)
            results.Add(RunNode(tools, list, outRoot, tool, passes));
        var deno = RunDeno(files, outRoot);

        var report = new StringBuilder();
        var parser = new Parser(new ParserOptions { AllowReturnOutsideFunction = false });
        var rows = new List<string>();
        // Pare output that does not parse is reported once; the per-tool counts leave those files out.
        var pareInvalid = files.Where(f => !Parses(parser, File.ReadAllText(Path.Combine(outRoot, "pare", f.Relative + ".js"))))
            .Select(f => f.Relative).ToHashSet();
        foreach (var r in results.Skip(1).Append(deno.Result))
        {
            // Strippers keep the source text; printers reprint literals and `{ a: a }` as `{ a }`.
            bool reprints = r.Tool is not ("node-strip" or "ts-blank-space");
            int bytes = 0, tree = 0, loose = 0, differ = 0, missing = 0;
            foreach (var f in files.Where(f => !pareInvalid.Contains(f.Relative)))
            {
                string pare = File.ReadAllText(Path.Combine(outRoot, "pare", f.Relative + ".js"));
                string path = Path.Combine(outRoot, r.Tool, f.Relative + ".js");
                if (!File.Exists(path))
                {
                    missing++;
                    continue;
                }
                string other = File.ReadAllText(path);
                if (other == pare)
                {
                    bytes++;
                    continue;
                }
                string? why = TreeDifference(parser, pare, other, reprints);
                if (why is null)
                    tree++;
                else if (reprints && TreeDifference(parser, pare, other, reprints, loose: true) is null)
                    loose++;
                else
                {
                    differ++;
                    report.AppendLine($"{r.Tool}\t{f.Relative}\t{why}");
                }
            }
            rows.Add($"| {r.Tool} {r.Version} | {bytes} | {tree} | {(reprints ? loose.ToString() : "-")} | {differ} | {missing} |");
        }
        File.WriteAllText(Path.Combine(outRoot, "differences.tsv"), report.ToString());

        Console.WriteLine();
        Console.WriteLine($"Pare output that does not parse: {pareInvalid.Count} files{(pareInvalid.Count > 0 ? ": " : "")}{string.Join(", ", pareInvalid.Order())}");
        Console.WriteLine($"Output against Pare's (the other {files.Count - pareInvalid.Count} files):");
        Console.WriteLine("| Tool | Same bytes | Same tree | Same but imports elided or bindings renamed | Differ | Failed or skipped |");
        Console.WriteLine("|---|---|---|---|---|---|");
        rows.ForEach(Console.WriteLine);

        Console.WriteLine();
        Console.WriteLine("In-process throughput, strings in memory, best and median of the timed passes:");
        Console.WriteLine("| Tool | Files | MB | Best ms | Median ms | MB/s (best) | Pare is |");
        Console.WriteLine("|---|---|---|---|---|---|---|");
        double pareRate = results[0].Rate;
        foreach (var r in results)
            Console.WriteLine($"| {r.Tool} {r.Version} | {r.Ok} | {r.Bytes / 1e6:F1} | {r.Best:F0} | {r.Median:F0} | {r.Rate:F1} | {(r.Tool == "pare" ? "-" : $"{pareRate / r.Rate:F1}x")} |");

        Console.WriteLine();
        Console.WriteLine("Whole process, all files, wall clock (one run each):");
        Console.WriteLine($"| pare CLI (NativeAOT) | {deno.PareCliMs:F0} ms |");
        Console.WriteLine($"| deno cache --no-check (transpile, cold cache) | {deno.DenoMs:F0} ms |");
        Console.WriteLine();
        Console.WriteLine($"Differences: {Path.GetRelativePath(Inputs.RepoRoot, Path.Combine(outRoot, "differences.tsv"))}");
        return 0;
    }

    private sealed record Result(string Tool, string Version, int Ok, long Bytes, double[] Passes)
    {
        public double Best => Passes.Length == 0 ? double.NaN : Passes.Min();
        public double Median => Passes.Length == 0 ? double.NaN : Passes.Order().ElementAt(Passes.Length / 2);
        public double Rate => Bytes / (Best / 1e3) / 1e6;
    }

    private static Result RunPare(List<(string Path, string Relative, string Text)> files, string outRoot, int passes)
    {
        var output = new ArrayBufferWriter<char>(4 << 20);
        var sink = new Inputs.NullSink();
        foreach (var f in files)
        {
            output.ResetWrittenCount();
            if (!TypeStripper.Strip(f.Text, output, sink))
                throw new InvalidOperationException($"{f.Relative} no longer strips");
            string target = Path.Combine(outRoot, "pare", f.Relative + ".js");
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.WriteAllText(target, output.WrittenSpan.ToString());
        }
        var times = new List<double>();
        for (int pass = 0; pass <= passes; pass++)
        {
            long start = Stopwatch.GetTimestamp();
            foreach (var f in files)
            {
                output.ResetWrittenCount();
                TypeStripper.Strip(f.Text, output, sink);
            }
            if (pass > 0)
                times.Add(Stopwatch.GetElapsedTime(start).TotalMilliseconds);
        }
        string version = typeof(TypeStripper).Assembly.GetName().Version?.ToString(3) ?? "";
        return new Result("pare", version, files.Count, files.Sum(f => (long)Encoding.UTF8.GetByteCount(f.Text)), [.. times]);
    }

    private static Result RunNode(string tools, string list, string outRoot, string tool, int passes)
    {
        var start = new ProcessStartInfo("node", ["--no-warnings", Path.Combine(tools, "tools.mjs"), list, outRoot, tool, passes.ToString()])
        {
            WorkingDirectory = tools,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        using var process = Process.Start(start)!;
        var stderr = process.StandardError.ReadToEndAsync();
        string stdout = process.StandardOutput.ReadToEnd();
        process.WaitForExit();
        if (process.ExitCode != 0)
            throw new InvalidOperationException($"{tool} failed: {stderr.Result}");
        using var json = JsonDocument.Parse(stdout);
        var root = json.RootElement;
        return new Result(tool, root.GetProperty("version").GetString() ?? "", root.GetProperty("ok").GetInt32(),
            root.GetProperty("bytes").GetInt64(), [.. root.GetProperty("passes").EnumerateArray().Select(e => e.GetDouble())]);
    }

    /// <summary>
    /// Deno's own transpiler (swc, as <c>deno run</c> uses it): <c>deno cache --no-check</c> writes
    /// the emit of every module in the graph to <c>$DENO_DIR/gen</c> without running anything. The
    /// graph must resolve, so an import map points every specifier that is not a JS or TS file on
    /// disk (packages, extensionless paths, .svelte, .css, URLs) at an empty module.
    /// </summary>
    private static (Result Result, double DenoMs, double PareCliMs) RunDeno(List<(string Path, string Relative, string Text)> files, string outRoot)
    {
        string version = RunProcess("deno", ["--version"], out _).Split(' ', 3) is [_, var v, ..] ? v : "";
        const string Stub = "data:application/javascript,export%20default%200";
        var map = new SortedDictionary<string, string>(StringComparer.Ordinal);
        foreach (var f in files)
        {
            foreach (Match m in Specifier().Matches(f.Text))
            {
                string spec = m.Groups["s"].Value;
                if (spec.StartsWith("./", StringComparison.Ordinal) || spec.StartsWith("../", StringComparison.Ordinal))
                {
                    string target = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(f.Path)!, spec));
                    if (!SloppyResolves(target))
                        map[new Uri(target).AbsoluteUri] = Stub;
                }
                else
                {
                    map[spec] = Stub;
                }
            }
        }

        string denoDir = Path.Combine(outRoot, "deno-dir");
        string importMap = Path.Combine(outRoot, "deno-imports.json");
        string[] Args() => ["cache", "--no-check", "--sloppy-imports", "--no-config", "--no-lock", "--import-map", importMap, .. files.Select(f => f.Path)];
        var errors = new StringBuilder();
        int exit = 0;
        // Specifiers the scan misses (JSDoc `@import`, `require` in odd places) come back as errors.
        for (int round = 0; round < 50; round++)
        {
            File.WriteAllText(importMap, JsonSerializer.Serialize(new Dictionary<string, object> { ["imports"] = map }));
            string output = RunDenoCache(denoDir, Args(), out exit);
            if (exit == 0)
                break;
            var missing = Unresolved().Match(output);
            if (!missing.Success)
            {
                errors.Append(output);
                break;
            }
            map[missing.Groups["s"].Value] = Stub;
        }
        if (exit != 0)
            File.WriteAllText(Path.Combine(outRoot, "deno-errors.txt"), errors.ToString());

        // Timed from a cold cache: graph, transpile and write.
        if (Directory.Exists(denoDir))
            Directory.Delete(denoDir, recursive: true);
        long t0 = Stopwatch.GetTimestamp();
        RunDenoCache(denoDir, Args(), out _);
        double denoMs = Stopwatch.GetElapsedTime(t0).TotalMilliseconds;

        string dest = Path.Combine(outRoot, "deno");
        int ok = 0;
        foreach (var f in files)
        {
            string full = Path.GetFullPath(f.Path);
            string gen = Path.Combine(denoDir, "gen", "file", full.Replace(":", "").TrimStart('/', '\\') + ".js");
            if (!File.Exists(gen))
                continue;
            string js = File.ReadAllText(gen);
            // Deno appends an inline source map and a cache stamp.
            int map0 = js.LastIndexOf("//# sourceMappingURL=", StringComparison.Ordinal);
            if (map0 >= 0)
                js = js[..map0];
            Directory.CreateDirectory(Path.GetDirectoryName(Path.Combine(dest, f.Relative))!);
            File.WriteAllText(Path.Combine(dest, f.Relative + ".js"), js);
            ok++;
        }

        string pare = Path.Combine(Inputs.RepoRoot, "tmp", "aot", OperatingSystem.IsWindows() ? "pare.exe" : "pare");
        double pareMs = double.NaN;
        if (File.Exists(pare))
        {
            long p0 = Stopwatch.GetTimestamp();
            RunProcess(pare, [.. files.Select(f => f.Path), "--outdir", Path.Combine(outRoot, "pare-cli")], out _);
            pareMs = Stopwatch.GetElapsedTime(p0).TotalMilliseconds;
        }
        return (new Result("deno", version + (exit == 0 ? "" : $" (exit {exit})"), ok, 0, []), denoMs, pareMs);
    }

    private static readonly string[] Modules = [".ts", ".mts", ".cts", ".js", ".mjs", ".cjs", ".json"];

    /// <summary>Whether <c>--sloppy-imports</c> finds a module: as written, with an extension, as a
    /// directory's index, or <c>.js</c> written for a <c>.ts</c> file.</summary>
    private static bool SloppyResolves(string target)
    {
        string bare = target.TrimEnd('/', '\\');
        if (File.Exists(bare) && Modules.Contains(Path.GetExtension(bare)))
            return true;
        if (Path.GetExtension(bare) is ".js" or ".mjs" or ".cjs" && File.Exists(bare[..^Path.GetExtension(bare).Length] + Path.GetExtension(bare).Replace('j', 't')))
            return true;
        return Modules.Any(ext => File.Exists(bare + ext) || File.Exists(Path.Combine(bare, "index" + ext)));
    }

    private static string RunDenoCache(string denoDir, string[] args, out int exit)
    {
        var start = new ProcessStartInfo("deno", args) { RedirectStandardOutput = true, RedirectStandardError = true };
        start.Environment["DENO_DIR"] = denoDir;
        start.Environment["NO_COLOR"] = "1";
        using var process = Process.Start(start)!;
        var stderr = process.StandardError.ReadToEndAsync();
        string stdout = process.StandardOutput.ReadToEnd();
        process.WaitForExit();
        exit = process.ExitCode;
        return stdout + stderr.Result;
    }

    // import/export ... from "x", import "x", import("x"), require("x"), /// <reference path|types="x" />.
    [GeneratedRegex("""(?:\bfrom|\bimport|\brequire)\s*\(?\s*(["'])(?<s>[^"'\r\n]+)\1|<reference\s+(?:path|types)\s*=\s*(["'])(?<s>[^"'\r\n]+)\2""")]
    private static partial Regex Specifier();

    // Module not found "file:///...", or Relative import path "x" not prefixed..., or Import "x" not a dependency.
    [GeneratedRegex(""""(?:Module not found|Relative import path|Import)\s+"(?<s>[^"]+)"""")]
    private static partial Regex Unresolved();

    private static string RunProcess(string file, string[] args, out int exit)
    {
        var start = new ProcessStartInfo(file, args) { RedirectStandardOutput = true, RedirectStandardError = true };
        using var process = Process.Start(start)!;
        var stderr = process.StandardError.ReadToEndAsync();
        string stdout = process.StandardOutput.ReadToEnd();
        process.WaitForExit();
        exit = process.ExitCode;
        return stdout + stderr.Result;
    }

    /// <summary>Null when both parse to the same tree (as modules, else as scripts); otherwise why not.</summary>
    private static string? TreeDifference(Parser parser, string pare, string other, bool reprints, bool loose = false)
    {
        var (a, b) = (TryParse(() => parser.ParseModule(pare)), TryParse(() => parser.ParseModule(other)));
        if (a is null || b is null)
            (a, b) = (TryParse(() => parser.ParseScript(pare)) ?? a, TryParse(() => parser.ParseScript(other)) ?? b);
        if (a is null)
            return "Pare output does not parse";
        if (b is null)
            return "tool output does not parse";
        string x = AstDump.Dump(a, reprints, loose), y = AstDump.Dump(b, reprints, loose);
        return x == y ? null : FirstDifference(x, y);
    }

    private static bool Parses(Parser parser, string js) =>
        TryParse(() => parser.ParseModule(js)) is not null || TryParse(() => parser.ParseScript(js)) is not null;

    private static Node? TryParse(Func<Node> parse)
    {
        try
        {
            return parse();
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static string FirstDifference(string a, string b)
    {
        int i = 0;
        while (i < a.Length && i < b.Length && a[i] == b[i])
            i++;
        int from = Math.Max(0, i - 50);
        string Clip(string s) => s[from..Math.Min(s.Length, i + 50)].Replace('\n', '~').Replace('\t', ' ');
        return $"at {i}: pare «{Clip(a)}» other «{Clip(b)}»";
    }
}
