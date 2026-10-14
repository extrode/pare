using System.Buffers;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Acornima;
using Acornima.Ast;
using Extrode.Pare.Conformance;

namespace Extrode.Pare.Bench;

/// <summary>
/// Runs Pare and other TypeScript-to-JavaScript tools over the real-world corpus files Pare strips,
/// checks each tool's output against Pare's and times them. Whitespace strippers (Node's
/// <c>stripTypeScriptTypes</c>, ts-blank-space) are compared byte for byte; emitters that reprint
/// the code (esbuild, oxc, Deno) as Acornima trees without locations or literal raw text.
/// Every tool runs in-process over strings already in memory, except Deno, which is timed as one
/// <c>deno bundle</c> process against one <c>pare</c> CLI process over the same files.
/// </summary>
internal static class Compare
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
        foreach (var r in results.Skip(1).Append(deno.Result))
        {
            bool bytewise = r.Tool is "node-strip" or "ts-blank-space";
            int same = 0, guards = 0, differ = 0, missing = 0;
            foreach (var f in files)
            {
                string pare = File.ReadAllText(Path.Combine(outRoot, "pare", f.Relative + ".js"));
                string path = r.Tool == "deno" ? Path.Combine(outRoot, "deno", Path.ChangeExtension(f.Relative, ".js")) : Path.Combine(outRoot, r.Tool, f.Relative + ".js");
                if (r.Tool == "deno" && f.Relative.StartsWith("typescript/checker.ambient", StringComparison.Ordinal))
                    path = Path.Combine(outRoot, "deno", "checker.ambient.js");
                if (!File.Exists(path))
                {
                    missing++;
                    continue;
                }
                string other = File.ReadAllText(path);
                if (bytewise && other != pare && OnlyAsiGuards(pare, other))
                {
                    guards++;
                    continue;
                }
                string? why = bytewise ? (other == pare ? null : FirstDifference(pare, other, skipGuards: true)) : TreeDifference(parser, pare, other);
                if (why is null)
                    same++;
                else
                {
                    differ++;
                    report.AppendLine($"{r.Tool}\t{f.Relative}\t{why}");
                }
            }
            rows.Add($"| {r.Tool} {r.Version} | {(bytewise ? "bytes" : "tree")} | {same} | {(bytewise ? guards.ToString() : "-")} | {differ} | {missing} |");
        }
        File.WriteAllText(Path.Combine(outRoot, "differences.tsv"), report.ToString());

        Console.WriteLine();
        Console.WriteLine("Output against Pare's (files Pare strips):");
        Console.WriteLine("| Tool | Compared as | Same | Same but Pare's ASI ; | Differ | Failed or skipped |");
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
        Console.WriteLine($"| deno bundle --inline-imports=false | {deno.DenoMs:F0} ms |");
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

    private static (Result Result, double DenoMs, double PareCliMs) RunDeno(List<(string Path, string Relative, string Text)> files, string outRoot)
    {
        string version = RunProcess("deno", ["--version"], out _).Split(' ', 3) is [_, var v, ..] ? v : "";
        // The corpus keeps its layout under .corpus/realworld; deno mirrors it from the common root.
        var inputs = files.Where(f => !f.Relative.StartsWith("typescript/checker.ambient", StringComparison.Ordinal)).Select(f => f.Path)
            .Append(files[^1].Path).ToList();
        long t0 = Stopwatch.GetTimestamp();
        string denoOutput = RunProcess("deno", ["bundle", "--no-config", "--no-lock", "--quiet", "--inline-imports=false", "--packages", "external",
            "--platform", "browser", "--outdir", Path.Combine(outRoot, "deno-raw"), .. inputs], out int exit);
        if (exit != 0)
            File.WriteAllText(Path.Combine(outRoot, "deno-errors.txt"), denoOutput);
        double denoMs = Stopwatch.GetElapsedTime(t0).TotalMilliseconds;
        // Two roots (the corpus and tmp/compare) give deno a common root above both; flatten it.
        string raw = Path.Combine(outRoot, "deno-raw");
        string dest = Path.Combine(outRoot, "deno");
        if (Directory.Exists(raw))
        {
            foreach (string file in Directory.EnumerateFiles(raw, "*.js", SearchOption.AllDirectories))
            {
                string rel = Path.GetRelativePath(raw, file).Replace('\\', '/');
                int at = rel.IndexOf(".corpus/realworld/", StringComparison.Ordinal);
                string target = at >= 0 ? rel[(at + ".corpus/realworld/".Length)..] : Path.GetFileName(rel);
                Directory.CreateDirectory(Path.GetDirectoryName(Path.Combine(dest, target))!);
                File.Copy(file, Path.Combine(dest, target), overwrite: true);
            }
        }

        string pare = Path.Combine(Inputs.RepoRoot, "tmp", "aot", OperatingSystem.IsWindows() ? "pare.exe" : "pare");
        double pareMs = double.NaN;
        if (File.Exists(pare))
        {
            t0 = Stopwatch.GetTimestamp();
            RunProcess(pare, [.. inputs, "--outdir", Path.Combine(outRoot, "pare-cli")], out _);
            pareMs = Stopwatch.GetElapsedTime(t0).TotalMilliseconds;
        }
        int ok = Directory.Exists(dest) ? Directory.EnumerateFiles(dest, "*.js", SearchOption.AllDirectories).Count() : 0;
        return (new Result("deno", version + (exit == 0 ? "" : $" (exit {exit})"), ok, 0, []), denoMs, pareMs);
    }

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

    /// <summary>
    /// Same length, and every difference is a <c>;</c> in Pare's output where the other has a space:
    /// Pare's guard at the start of an erased statement (handoff 7.5), an empty statement.
    /// </summary>
    private static bool OnlyAsiGuards(string pare, string other)
    {
        if (pare.Length != other.Length)
            return false;
        for (int i = 0; i < pare.Length; i++)
        {
            if (pare[i] != other[i] && !(pare[i] == ';' && other[i] == ' '))
                return false;
        }
        return true;
    }

    /// <summary>Null when both parse to the same tree (as modules, else as scripts); otherwise why not.</summary>
    private static string? TreeDifference(Parser parser, string pare, string other)
    {
        var (a, b) = (TryParse(() => parser.ParseModule(pare)), TryParse(() => parser.ParseModule(other)));
        if (a is null || b is null)
            (a, b) = (TryParse(() => parser.ParseScript(pare)) ?? a, TryParse(() => parser.ParseScript(other)) ?? b);
        if (a is null)
            return "Pare output does not parse";
        if (b is null)
            return "tool output does not parse";
        string x = AstDump.Dump(a, ignoreRaw: true), y = AstDump.Dump(b, ignoreRaw: true);
        return x == y ? null : FirstDifference(x, y);
    }

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

    private static string FirstDifference(string a, string b, bool skipGuards = false)
    {
        int i = 0;
        while (i < a.Length && i < b.Length && (a[i] == b[i] || (skipGuards && a[i] == ';' && b[i] == ' ')))
            i++;
        int from = Math.Max(0, i - 50);
        string Clip(string s) => s[from..Math.Min(s.Length, i + 50)].Replace('\n', '~').Replace('\t', ' ');
        return $"at {i}: pare «{Clip(a)}» other «{Clip(b)}»";
    }
}
