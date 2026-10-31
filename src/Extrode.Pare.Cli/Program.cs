using System.Buffers;
using System.Runtime.ExceptionServices;
using Extrode.Pare;

namespace Extrode.Pare.Cli;

internal static class Program
{
    internal const int ExitOk = 0;
    internal const int ExitRejected = 1;
    internal const int ExitUsage = 2;

    private const string Usage = """
        usage: pare <files|dirs|globs...> [--outdir dir] [--check-only]
               pare --stdin

          Strips .ts, .mts and .cts files to .js, .mjs and .cjs next to each source, or under
          --outdir with the directory structure kept. Directories are searched recursively;
          .d.ts files and node_modules are skipped. Relative './x.ts' imports become './x.js'.
          An existing .js output is replaced.

          --outdir dir  write output under dir, mirroring the inputs' common directory
          --check-only  report diagnostics, write nothing
          --stdin       read TypeScript from stdin, write JavaScript to stdout
          --version     print the version
          --help        print this help

        exit codes: 0 ok, 1 rejected syntax, 2 I/O or usage error
        """;

    // The parser's depth guard (RuntimeHelpers.TryEnsureSufficientExecutionStack) needs to know
    // the stack's size. On linux-musl-arm64 NativeAOT, .NET misreads the main thread's (musl
    // reports only the part mapped so far), so about one process in four rejected every file. A
    // thread .NET creates has a known size; 8 MB matches the Linux and macOS main thread.
    internal const int StackSize = 8 * 1024 * 1024;

    private static int Main(string[] args) => OnOwnStack(() => Run(args, Console.In, Console.Out, Console.Error));

    internal static int OnOwnStack(Func<int> run)
    {
        int result = 0;
        ExceptionDispatchInfo? error = null;
        var thread = new Thread(() =>
        {
            try { result = run(); }
            catch (Exception e) { error = ExceptionDispatchInfo.Capture(e); }
        }, StackSize);
        thread.Start();
        thread.Join();
        error?.Throw();
        return result;
    }

    internal static int Run(string[] args, TextReader stdin, TextWriter stdout, TextWriter stderr)
    {
        if (args.Length == 0 || args is ["--help"] or ["-h"])
        {
            (args.Length == 0 ? stderr : stdout).WriteLine(Usage);
            return args.Length == 0 ? ExitUsage : ExitOk;
        }
        if (args is ["--version"])
        {
            stdout.WriteLine(typeof(TypeStripper).Assembly.GetName().Version?.ToString(3) ?? "0.0.0");
            return ExitOk;
        }
        if (args is ["--stdin"])
            return StripStdin(stdin, stdout, stderr);

        string? outDir = null;
        bool checkOnly = false;
        var inputs = new List<string>();
        for (int i = 0; i < args.Length; i++)
        {
            string arg = args[i];
            if (arg == "--outdir")
            {
                if (i + 1 == args.Length)
                    return UsageError(stderr, "--outdir needs a directory");
                outDir = args[++i];
            }
            else if (arg.StartsWith("--outdir=", StringComparison.Ordinal))
            {
                outDir = arg["--outdir=".Length..];
            }
            else if (arg == "--check-only")
            {
                checkOnly = true;
            }
            else if (arg.StartsWith('-') && arg.Length > 1)
            {
                return UsageError(stderr, arg == "--stdin" ? "--stdin takes no other arguments" : $"unknown option '{arg}'");
            }
            else
            {
                inputs.Add(arg);
            }
        }
        if (inputs.Count == 0)
            return UsageError(stderr, "no input files");
        if (outDir is not null && checkOnly)
            return UsageError(stderr, "--outdir and --check-only exclude each other");
        return FileStripper.Run(inputs, outDir, checkOnly, stderr);
    }

    private static int UsageError(TextWriter stderr, string message)
    {
        stderr.WriteLine($"pare: {message}");
        stderr.WriteLine("run 'pare --help' for usage");
        return ExitUsage;
    }

    private static int StripStdin(TextReader stdin, TextWriter stdout, TextWriter stderr)
    {
        string source = stdin.ReadToEnd();
        var output = new ArrayBufferWriter<char>(source.Length);
        var sink = new ListSink();
        if (!TypeStripper.Strip(source, output, sink, StripOptions.RewriteRelativeImportExtensions))
        {
            foreach (var d in sink.Items)
                stderr.WriteLine(DiagnosticFormatter.Format("<stdin>", source, d));
            return ExitRejected;
        }
        stdout.Write(output.WrittenSpan);
        return ExitOk;
    }

    internal sealed class ListSink : IDiagnosticSink
    {
        public List<Diagnostic> Items { get; } = [];
        public void Report(in Diagnostic diagnostic) => Items.Add(diagnostic);
    }
}
