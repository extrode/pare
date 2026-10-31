using System.Runtime.Versioning;
using Extrode.Pare.Cli;

namespace Extrode.Pare.Tests;

public sealed class FileModeTests : IDisposable
{
    private readonly DirectoryInfo _dir = Directory.CreateTempSubdirectory("pare-cli-");

    public void Dispose() => _dir.Delete(recursive: true);

    private string P(string relative) => Path.Combine(_dir.FullName, relative);

    private void Put(string relative, string text)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(P(relative))!);
        File.WriteAllText(P(relative), text);
    }

    private static (int Exit, string Err) Run(params string[] args)
    {
        var o = new StringWriter();
        var e = new StringWriter();
        int exit = Program.Run(args, new StringReader(""), o, e);
        Assert.Equal("", o.ToString());
        return (exit, e.ToString());
    }

    [Fact]
    public void Writes_js_next_to_each_source()
    {
        Put("a.ts", "import b from './b.mts';\nlet x: number = b;\n");
        Put("b.mts", "export default 1 as number;\n");
        Put("c.cts", "let c!: string;\n");

        var (exit, err) = Run(P("a.ts"), P("b.mts"), P("c.cts"));

        Assert.Equal(0, exit);
        Assert.Equal("", err);
        Assert.Equal("import b from './b.mjs';\nlet x         = b;\n", File.ReadAllText(P("a.js")));
        Assert.Equal("export default 1          ;\n", File.ReadAllText(P("b.mjs")));
        Assert.Equal("let c         ;\n", File.ReadAllText(P("c.cjs")));
    }

    [Fact]
    public void Outdir_mirrors_the_common_directory()
    {
        Put("src/a.ts", "let a: T;");
        Put("src/lib/b.ts", "let b: T;");

        var (exit, _) = Run(P("src/a.ts"), P("src/lib/b.ts"), "--outdir", P("out"));

        Assert.Equal(0, exit);
        Assert.Equal("let a   ;", File.ReadAllText(P("out/a.js")));
        Assert.Equal("let b   ;", File.ReadAllText(P("out/lib/b.js")));
        Assert.False(File.Exists(P("src/a.js")));
    }

    [Fact]
    public void Outdir_base_is_the_deepest_directory_shared_by_all_inputs()
    {
        Put("src/a/x.ts", "x;");
        Put("src/b/y.ts", "y;");

        var (exit, _) = Run(P("src/a/x.ts"), P("src/b/y.ts"), "--outdir", P("out"));

        Assert.Equal(0, exit);
        Assert.True(File.Exists(P("out/a/x.js")));
        Assert.True(File.Exists(P("out/b/y.js")));
    }

    [Fact]
    public void Output_is_utf8_without_a_byte_order_mark()
    {
        Put("a.ts", "let \u00e9: T = '\u00e9';");

        Assert.Equal(0, Run(P("a.ts")).Exit);

        Assert.Equal("let \u00e9    = '\u00e9';"u8.ToArray(), File.ReadAllBytes(P("a.js")));
    }

    [Fact]
    public void Declaration_file_arguments_are_skipped()
    {
        Put("a.ts", "a;");
        Put("t.d.ts", "declare let t: T;");

        var (exit, err) = Run(P("a.ts"), P("t.d.ts"));

        Assert.Equal(0, exit);
        Assert.Equal("", err);
        Assert.True(File.Exists(P("a.js")));
        Assert.False(File.Exists(P("t.d.js")));
    }

    [Fact]
    public void Unusable_input_stops_before_anything_is_written()
    {
        Put("a.ts", "a;");

        var (exit, err) = Run(P("a.ts"), P("missing.ts"));

        Assert.Equal(2, exit);
        Assert.Contains("no such file", err);
        Assert.False(File.Exists(P("a.js")));
    }

    [Fact]
    public void Directory_input_recurses_and_skips_declarations_and_node_modules()
    {
        Put("src/a.ts", "a;");
        Put("src/deep/b.ts", "b;");
        Put("src/types.d.ts", "declare let t: T;");
        Put("src/node_modules/m/index.ts", "m;");
        Put("src/c.js", "c;");

        var (exit, _) = Run(P("src"), "--outdir=" + P("out"));

        Assert.Equal(0, exit);
        Assert.Equal(["a.js", "deep/b.js"],
            Directory.GetFiles(P("out"), "*", SearchOption.AllDirectories)
                .Select(f => Path.GetRelativePath(P("out"), f).Replace('\\', '/')).Order());
    }

    [Fact]
    public void Directory_input_enters_dot_directories_and_symlinked_directories()
    {
        if (OperatingSystem.IsWindows())
            Assert.Skip("symlinks need Developer Mode");
        Put("src/.hidden/h.ts", "h;");
        Put("linked/l.ts", "l;");
        Directory.CreateSymbolicLink(P("src/link"), P("linked"));

        var (exit, _) = Run(P("src"), "--outdir=" + P("out"));

        Assert.Equal(0, exit);
        Assert.Equal([".hidden/h.js", "link/l.js"],
            Directory.GetFiles(P("out"), "*", SearchOption.AllDirectories)
                .Select(f => Path.GetRelativePath(P("out"), f).Replace('\\', '/')).Order());
    }

    [Fact]
    public void A_symlink_back_to_a_directory_already_walked_is_skipped()
    {
        if (OperatingSystem.IsWindows())
            Assert.Skip("symlinks need Developer Mode");
        Put("src/a.ts", "a;");
        Put("src/sub/b.ts", "b;");
        Directory.CreateSymbolicLink(P("src/sub/loop"), P("src"));

        var (exit, _) = Run(P("src"), "--outdir=" + P("out"));

        Assert.Equal(0, exit);
        Assert.Equal(["a.js", "sub/b.js"],
            Directory.GetFiles(P("out"), "*", SearchOption.AllDirectories)
                .Select(f => Path.GetRelativePath(P("out"), f).Replace('\\', '/')).Order());
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public void A_symlink_loop_is_skipped_whatever_the_form_of_the_input_path(bool throughAlias, bool trailingSeparator)
    {
        if (OperatingSystem.IsWindows())
            Assert.Skip("symlinks need Developer Mode");
        Put("real/src/a.ts", "a;");
        Put("real/src/sub/b.ts", "b;");
        Directory.CreateSymbolicLink(P("real/src/sub/loop"), P("real/src"));
        Directory.CreateSymbolicLink(P("alias"), P("real"));
        string input = P(throughAlias ? "alias/src" : "real/src") + (trailingSeparator ? "/" : "");

        var (exit, _) = Run(input, "--outdir=" + P("out"));

        Assert.Equal(0, exit);
        Assert.Equal(["a.js", "sub/b.js"],
            Directory.GetFiles(P("out"), "*", SearchOption.AllDirectories)
                .Select(f => Path.GetRelativePath(P("out"), f).Replace('\\', '/')).Order());
    }

    [Fact]
    public void Outdir_keeps_the_input_directory_when_one_subfolder_has_every_file()
    {
        Put("src/pages/home.ts", "h;");

        var (exit, _) = Run(P("src"), "--outdir", P("out"));

        Assert.Equal(0, exit);
        Assert.True(File.Exists(P("out/pages/home.js")));
    }

    [Fact]
    public void Outdir_for_a_glob_keeps_the_path_below_the_glob_base()
    {
        Put("src/pages/home.ts", "h;");

        var (exit, _) = Run(P("src") + "/**/*.ts", "--outdir", P("out"));

        Assert.Equal(0, exit);
        Assert.True(File.Exists(P("out/pages/home.js")));
    }

    [Theory]
    [InlineData("--outdir=")]
    [InlineData("--outdir ")]
    public void An_empty_outdir_is_a_usage_error(string option)
    {
        Put("src/a.ts", "a;");

        var (exit, err) = option.EndsWith('=') ? Run(P("src"), option) : Run(P("src"), "--outdir", "");

        Assert.Equal(2, exit);
        Assert.Contains("--outdir needs a directory", err);
    }

    [Fact]
    public void Glob_input_selects_matching_sources()
    {
        Put("src/a.ts", "a;");
        Put("src/x/b.ts", "b;");
        Put("src/x/c.mts", "c;");

        var (exit, _) = Run(P("src") + "/**/*.ts");

        Assert.Equal(0, exit);
        Assert.True(File.Exists(P("src/a.js")));
        Assert.True(File.Exists(P("src/x/b.js")));
        Assert.False(File.Exists(P("src/x/c.mjs")));
    }

    [Fact]
    public void Rejected_file_reports_with_its_path_and_exits_1_while_others_are_written()
    {
        Put("bad.ts", "let a = 1;\nenum E { A }\n");
        Put("good.ts", "let b: T;");

        var (exit, err) = Run(P("bad.ts"), P("good.ts"));

        Assert.Equal(1, exit);
        Assert.Contains("bad.ts:2:1: error PARE100: ", err);
        Assert.False(File.Exists(P("bad.js")));
        Assert.Equal("let b   ;", File.ReadAllText(P("good.js")));
    }

    [Fact]
    public void Check_only_writes_nothing()
    {
        Put("a.ts", "let a: T;");

        var (exit, err) = Run(P("a.ts"), "--check-only");

        Assert.Equal(0, exit);
        Assert.Equal("", err);
        Assert.False(File.Exists(P("a.js")));
    }

    [Fact]
    public void Syntax_error_exits_1()
    {
        Put("a.ts", "let = ;");
        var (exit, err) = Run(P("a.ts"), "--check-only");
        Assert.Equal(1, exit);
        Assert.Contains("error PARE001", err);
    }

    [Theory]
    [InlineData("missing.ts", "no such file")]
    [InlineData("view.tsx", "not a .ts")]
    [InlineData("none/**/*.ts", "no .ts")]
    public void Unusable_inputs_exit_2(string input, string message)
    {
        Put("view.tsx", "<div/>;");
        var (exit, err) = Run(P(input));
        Assert.Equal(2, exit);
        Assert.Contains(message, err);
    }

    [Fact]
    public void Unwritable_output_exits_2()
    {
        Put("a.ts", "a;");
        Directory.CreateDirectory(P("a.js"));
        var (exit, err) = Run(P("a.ts"));
        Assert.Equal(2, exit);
        Assert.Contains("a.ts: ", err);
    }

    [Fact]
    [UnsupportedOSPlatform("windows")]
    public void Unreadable_directory_exits_2()
    {
        if (OperatingSystem.IsWindows())
            Assert.Skip("Unix permissions");
        Put("src/ok.ts", "a;");
        Directory.CreateDirectory(P("src/locked"));
        File.SetUnixFileMode(P("src/locked"), UnixFileMode.None);
        try
        {
            var (exit, err) = Run(P("src"));
            Assert.Equal(2, exit);
            Assert.StartsWith($"pare: {P("src")}: ", err);
            Assert.False(File.Exists(P("src/ok.js")));
        }
        finally
        {
            File.SetUnixFileMode(P("src/locked"), UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
    }
}
