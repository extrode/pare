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
    public void Unimplemented_file_mode_exits_2()
    {
        var (exit, _, stderr) = Run("", "a.ts");
        Assert.Equal(2, exit);
        Assert.Contains("--stdin", stderr);
    }
}
