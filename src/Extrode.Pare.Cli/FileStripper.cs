using System.Buffers;
using System.Text;

namespace Extrode.Pare.Cli;

/// <summary><c>pare &lt;files|dirs|globs...&gt;</c>: strips each source to a sibling or <c>--outdir</c> file.</summary>
internal static class FileStripper
{
    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    public static int Run(List<string> inputs, string? outDir, bool checkOnly, TextWriter stderr)
    {
        var files = new SortedSet<string>(StringComparer.Ordinal);
        int exit = Program.ExitOk;
        foreach (var input in inputs)
        {
            if (!Collect(input, files, stderr))
                exit = Program.ExitUsage;
        }
        if (exit != Program.ExitOk)
            return exit;
        if (files.Count == 0)
        {
            stderr.WriteLine("pare: no .ts, .mts or .cts files matched");
            return Program.ExitUsage;
        }

        string? outRoot = outDir is null ? null : Path.GetFullPath(outDir);
        string baseDir = CommonDirectory(files);
        var output = new ArrayBufferWriter<char>();
        var sink = new Program.ListSink();
        foreach (var file in files)
        {
            string shown = Path.GetRelativePath(Environment.CurrentDirectory, file);
            try
            {
                string source = File.ReadAllText(file);
                output.ResetWrittenCount();
                sink.Items.Clear();
                if (!TypeStripper.Strip(source, output, sink, StripOptions.RewriteRelativeImportExtensions))
                {
                    foreach (var d in sink.Items)
                        stderr.WriteLine(DiagnosticFormatter.Format(shown, source, d));
                    exit = Math.Max(exit, Program.ExitRejected);
                    continue;
                }
                if (checkOnly)
                    continue;
                string target = OutputPath(file, baseDir, outRoot);
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                using var stream = new FileStream(target, FileMode.Create, FileAccess.Write);
                using var writer = new StreamWriter(stream, Utf8NoBom);
                writer.Write(output.WrittenSpan);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                stderr.WriteLine($"pare: {shown}: {e.Message}");
                exit = Program.ExitUsage;
            }
        }
        return exit;
    }

    /// <summary>Adds the source files named by a file, directory or glob. False when it names nothing usable.</summary>
    private static bool Collect(string input, SortedSet<string> files, TextWriter stderr)
    {
        if (Glob.HasWildcard(input))
        {
            string root = Glob.BaseDirectory(input);
            if (!Directory.Exists(root))
                return true;
            foreach (var file in Walk(root))
            {
                if (Glob.Matches(input, Path.GetRelativePath(root, file)))
                    files.Add(Path.GetFullPath(file));
            }
            return true;
        }
        if (Directory.Exists(input))
        {
            foreach (var file in Walk(input))
                files.Add(Path.GetFullPath(file));
            return true;
        }
        if (!File.Exists(input))
        {
            stderr.WriteLine($"pare: {input}: no such file or directory");
            return false;
        }
        if (IsDeclaration(input))
            return true;
        if (!IsSource(input))
        {
            stderr.WriteLine($"pare: {input}: not a .ts, .mts or .cts file");
            return false;
        }
        files.Add(Path.GetFullPath(input));
        return true;
    }

    /// <summary>Source files under <paramref name="dir"/>, recursively, skipping node_modules.</summary>
    private static IEnumerable<string> Walk(string dir)
    {
        foreach (var file in Directory.EnumerateFiles(dir))
        {
            if (IsSource(file) && !IsDeclaration(file))
                yield return file;
        }
        foreach (var sub in Directory.EnumerateDirectories(dir))
        {
            if (Path.GetFileName(sub) == "node_modules")
                continue;
            foreach (var file in Walk(sub))
                yield return file;
        }
    }

    private static bool IsSource(string path) =>
        path.EndsWith(".ts", StringComparison.Ordinal) || path.EndsWith(".mts", StringComparison.Ordinal) ||
        path.EndsWith(".cts", StringComparison.Ordinal);

    private static bool IsDeclaration(string path) =>
        path.EndsWith(".d.ts", StringComparison.Ordinal) || path.EndsWith(".d.mts", StringComparison.Ordinal) ||
        path.EndsWith(".d.cts", StringComparison.Ordinal);

    /// <summary><c>.ts</c> to <c>.js</c>, <c>.mts</c> to <c>.mjs</c>, <c>.cts</c> to <c>.cjs</c>, under <paramref name="outRoot"/> when given.</summary>
    internal static string OutputPath(string file, string baseDir, string? outRoot)
    {
        string js = file[..^2] + "js";
        return outRoot is null ? js : Path.Combine(outRoot, Path.GetRelativePath(baseDir, js));
    }

    private static string CommonDirectory(SortedSet<string> files)
    {
        string common = Path.GetDirectoryName(files.Min)!;
        foreach (var file in files)
        {
            while (!IsUnder(file, common))
                common = Path.GetDirectoryName(common)!;
        }
        return common;
    }

    private static bool IsUnder(string file, string dir) =>
        file.StartsWith(dir, StringComparison.Ordinal) &&
        (dir.EndsWith(Path.DirectorySeparatorChar) || file.Length > dir.Length && file[dir.Length] == Path.DirectorySeparatorChar);
}
