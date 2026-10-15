using Extrode.Pare.Cli;

namespace Extrode.Pare.Tests;

public class CliTests
{
    private static (int Exit, string Out, string Err) Run(string stdin, params string[] args)
    {
        var o = new StringWriter();
        var e = new StringWriter();
        int exit = Program.Run(args, new StringReader(stdin), o, e);
        return (exit, o.ToString(), e.ToString());
    }

    [Fact]
    public void Stdin_mode_writes_stripped_source_to_stdout()
    {
        var (exit, stdout, stderr) = Run("let a = 1;\n", "--stdin");
        Assert.Equal(0, exit);
        Assert.Equal("let a = 1;\n", stdout);
        Assert.Equal("", stderr);
    }

    [Fact]
    public void No_arguments_prints_usage_to_stderr_and_exits_2()
    {
        var (exit, stdout, stderr) = Run("");
        Assert.Equal(2, exit);
        Assert.Equal("", stdout);
        Assert.StartsWith("usage: pare", stderr);
    }

    [Fact]
    public void Help_prints_usage_to_stdout_and_exits_0()
    {
        var (exit, stdout, _) = Run("", "--help");
        Assert.Equal(0, exit);
        Assert.StartsWith("usage: pare", stdout);
    }

    [Fact]
    public void Stdin_mode_rewrites_relative_ts_imports()
    {
        var (exit, stdout, _) = Run("import a from \"./a.ts\";\n", "--stdin");
        Assert.Equal(0, exit);
        Assert.Equal("import a from \"./a.js\";\n", stdout);
    }

    [Fact]
    public void Stdin_mode_reports_rejections_and_exits_1()
    {
        var (exit, stdout, stderr) = Run("let a = 1;\nenum E {}\n", "--stdin");
        Assert.Equal(1, exit);
        Assert.Equal("", stdout);
        Assert.StartsWith("<stdin>:2:1: error PARE100: ", stderr);
    }

    [Theory]
    [InlineData("unknown option '--bogus'", "--bogus")]
    [InlineData("--outdir needs a directory", "--outdir")]
    [InlineData("--stdin takes no other arguments", "a.ts", "--stdin")]
    [InlineData("no input files", "--check-only")]
    [InlineData("--outdir and --check-only exclude each other", "a.ts", "--check-only", "--outdir", "out")]
    public void Bad_arguments_exit_2(string message, params string[] args)
    {
        var (exit, stdout, stderr) = Run("", args);
        Assert.Equal(2, exit);
        Assert.Equal("", stdout);
        Assert.StartsWith("pare: " + message + Environment.NewLine, stderr);
    }
}
