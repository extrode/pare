using System.Buffers;
using Extrode.Pare;

namespace Extrode.Pare.Cli;

internal static class Program
{
    internal const int ExitOk = 0;
    internal const int ExitRejected = 1;
    internal const int ExitUsage = 2;

    private const string Usage = """
        usage: pare <files|globs...> [--outdir dir] [--check-only] [--stdin]

          --stdin       read TypeScript from stdin, write JavaScript to stdout
          --version     print the version
          --help        print this help

        exit codes: 0 ok, 1 rejected syntax, 2 I/O or usage error
        """;

    private static int Main(string[] args) => Run(args, Console.In, Console.Out, Console.Error);

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

        // File and glob mode lands in M6.
        stderr.WriteLine("pare: only --stdin is implemented so far");
        return ExitUsage;
    }

    private static int StripStdin(TextReader stdin, TextWriter stdout, TextWriter stderr)
    {
        string source = stdin.ReadToEnd();
        var output = new ArrayBufferWriter<char>(source.Length);
        var sink = new ListSink();
        if (!TypeStripper.Strip(source, output, sink))
        {
            foreach (var d in sink.Items)
                stderr.WriteLine(DiagnosticFormatter.Format("<stdin>", source, d));
            return ExitRejected;
        }
        stdout.Write(output.WrittenSpan);
        return ExitOk;
    }

    private sealed class ListSink : IDiagnosticSink
    {
        public List<Diagnostic> Items { get; } = [];
        public void Report(in Diagnostic diagnostic) => Items.Add(diagnostic);
    }
}
