using System.Buffers;
using System.Text.RegularExpressions;

namespace Extrode.Pare.Tests;

/// <summary>Keeps docs/06-reference/error-codes.md in step with the codes Pare emits.</summary>
public partial class ErrorCodesDocTests
{
    private static readonly string RepoRoot = FindRepoRoot();
    private static readonly string Doc = File.ReadAllText(Path.Combine(RepoRoot, "docs", "06-reference", "error-codes.md")).ReplaceLineEndings("\n");
    private static readonly string Targets = File.ReadAllText(Path.Combine(RepoRoot, "src", "Extrode.Pare.Build", "build", "Extrode.Pare.Build.targets"));

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (!File.Exists(Path.Combine(dir.FullName, "Pare.slnx")))
            dir = dir.Parent ?? throw new InvalidOperationException("Pare.slnx not found above " + AppContext.BaseDirectory);
        return dir.FullName;
    }

    private static string Name(DiagnosticCode code) => $"PARE{(int)code:000}";

    private static string Section(string name)
    {
        int start = Doc.IndexOf("\n## " + name + "\n", StringComparison.Ordinal);
        Assert.True(start >= 0, $"no section for {name}");
        int end = Doc.IndexOf("\n## ", start + 1, StringComparison.Ordinal);
        return end < 0 ? Doc[start..] : Doc[start..end];
    }

    private static List<string> TsBlocks(string section) =>
        [.. TsBlock().Matches(section).Select(m => m.Groups[1].Value)];

    public static TheoryData<DiagnosticCode> Codes() => new(Enum.GetValues<DiagnosticCode>());

    [Fact]
    public void Documents_every_code_and_no_other()
    {
        var emitted = Enum.GetValues<DiagnosticCode>().Select(Name)
            .Concat(TargetsCode().Matches(Targets).Select(m => m.Groups[1].Value))
            .Order().ToList();
        var documented = SectionHeading().Matches(Doc).Select(m => m.Groups[1].Value).Order().ToList();
        Assert.Equal(emitted, documented);
    }

    [Theory]
    [MemberData(nameof(Codes))]
    public void Section_quotes_the_message_and_hint(DiagnosticCode code)
    {
        string section = Section(Name(code));
        Assert.Contains($"\n`{DiagnosticMessages.Message(code)} ({DiagnosticMessages.Hint(code)})`\n", section);
        Assert.Contains($"| [{Name(code)}](#{Name(code).ToLowerInvariant()}) | error | {DiagnosticMessages.Message(code)} |", Doc);
    }

    [Theory]
    [MemberData(nameof(Codes))]
    public void First_example_triggers_the_code_and_the_second_strips(DiagnosticCode code)
    {
        var blocks = TsBlocks(Section(Name(code)));
        Assert.Equal(2, blocks.Count);

        var sink = new RecordingSink();
        Assert.False(TypeStripper.Strip(blocks[0], new ArrayBufferWriter<char>(), sink));
        Assert.NotEmpty(sink.Items);
        Assert.All(sink.Items, d => Assert.Equal(code, d.Code));

        sink = new RecordingSink();
        Assert.True(TypeStripper.Strip(blocks[1], new ArrayBufferWriter<char>(), sink), $"rejected: {string.Join(", ", sink.Items)}");
    }

    [Theory]
    [InlineData("PARE200", "warning")]
    [InlineData("PARE201", "error")]
    [InlineData("PARE202", "warning")]
    [InlineData("PARE203", "error")]
    [InlineData("PARE204", "error")]
    public void Build_diagnostic_section_quotes_the_targets_text(string code, string kind)
    {
        var task = BuildDiagnostic().Matches(Targets).Single(m => m.Groups[2].Value == code);
        Assert.Equal(kind, task.Groups[1].Value.ToLowerInvariant());
        Assert.Contains($"| [{code}](#{code.ToLowerInvariant()}) | {kind} (MSBuild) |", Doc);

        // MSBuild expressions in the text (%(...), @(...)) stand for the example's values.
        string text = System.Net.WebUtility.HtmlDecode(task.Groups[3].Value);
        string pattern = "\n`" + Regex.Escape($"{kind} {code}: ") +
            string.Join(".+", MSBuildExpression().Split(text).Select(Regex.Escape)) + "`\n";
        Assert.Matches(pattern, Section(code));
    }

    [GeneratedRegex(@"^## (PARE\d{3})$", RegexOptions.Multiline)]
    private static partial Regex SectionHeading();

    [GeneratedRegex(@"Code=""(PARE\d{3})""")]
    private static partial Regex TargetsCode();

    [GeneratedRegex(@"<(Warning|Error) Code=""(PARE\d{3})""[^<]*?Text=""([^""]*)""", RegexOptions.Singleline)]
    private static partial Regex BuildDiagnostic();

    [GeneratedRegex(@"[%@]\([^()]*(?:\([^()]*\)[^()]*)*\)")]
    private static partial Regex MSBuildExpression();

    [GeneratedRegex(@"```ts\n(.*?)```", RegexOptions.Singleline)]
    private static partial Regex TsBlock();
}
