using Extrode.Pare.Cli;

namespace Extrode.Pare.Tests;

public class GlobTests
{
    [Theory]
    [InlineData("*.ts", 1)]
    [InlineData("src/*.ts", 1)]
    [InlineData("src/*/x?.ts", 2)]
    [InlineData("src/**/*.ts", int.MaxValue)]
    [InlineData("**/*.ts", int.MaxValue)]
    [InlineData("C:\\*.ts", 1)]
    public void Levels_is_how_deep_a_match_can_be(string pattern, int expected) =>
        Assert.Equal(expected, Glob.Levels(pattern));

    [Theory]
    [InlineData("src/**/*.ts", "src")]
    [InlineData("src/*.ts", "src")]
    [InlineData("*.ts", ".")]
    [InlineData("a/b/c?.ts", "a/b")]
    [InlineData("/abs/**/x.ts", "/abs")]
    [InlineData("C:\\*.ts", "C:/")]
    [InlineData("/C:/*.ts", "/C:")]
    [InlineData("C:\\src\\*.ts", "C:/src")]
    [InlineData("\\\\srv\\share\\src\\*.ts", "//srv/share/src")]
    public void Base_directory_is_the_part_before_the_first_wildcard(string pattern, string expected) =>
        Assert.Equal(expected, Glob.BaseDirectory(pattern));

    [Theory]
    [InlineData("src/**/*.ts", "a.ts", true)]
    [InlineData("src/**/*.ts", "x/y/a.ts", true)]
    [InlineData("src/**/*.ts", "x/a.mts", false)]
    [InlineData("src/*.ts", "x/a.ts", false)]
    [InlineData("src/*.ts", "a.ts", true)]
    [InlineData("src/a?.ts", "ab.ts", true)]
    [InlineData("src/a?.ts", "a.ts", false)]
    [InlineData("src/**/lib/*.ts", "lib/a.ts", true)]
    [InlineData("src/**/lib/*.ts", "x\\lib\\a.ts", true)]
    [InlineData("src/*/*.ts", "a.ts", false)]
    [InlineData("src/a*.ts", "a.ts", true)]
    [InlineData("src/*", "x/a.ts", false)]
    public void Matches_relative_paths(string pattern, string relative, bool expected) =>
        Assert.Equal(expected, Glob.Matches(pattern, relative));
}
