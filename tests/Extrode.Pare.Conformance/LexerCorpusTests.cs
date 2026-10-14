using System.Text;
using Extrode.Pare.Lexing;
using Extrode.Pare.Tests.Lexing;

namespace Extrode.Pare.Conformance;

public class LexerCorpusTests(ITestOutputHelper output)
{
    private const TokenFlags Error = TokenFlags.Invalid | TokenFlags.Unterminated;

    private const string Nel = "U+0085 NEL: TypeScript scans it as whitespace, ECMAScript and browsers do not";
    private const string RegexGuess = "harness guesses regex vs division after '}' or ')'; only the grammar knows (M2 recognizer)";

    // Flagged files TypeScript's scanner accepts, with the reason. Each is a deliberate
    // difference or a limit of the Tokenizer harness, not of the lexer.
    private static readonly Dictionary<string, string> Deviations = new()
    {
        ["fileWithNextLine2.ts"] = Nel,
        ["fileWithNextLine3.ts"] = Nel,
        ["sourceMap-LineBreaks.ts"] = Nel,
        ["arithmeticOperatorWithNullValueAndInvalidOperands.ts"] = RegexGuess,
        ["arithmeticOperatorWithUndefinedValueAndInvalidOperands.ts"] = RegexGuess,
        ["parserRegularExpression4.ts"] = RegexGuess,
        ["parserRegularExpression5.ts"] = RegexGuess,
        ["parseJsxElementInUnaryExpressionNoCrash2.ts"] = "JSX in a .js file, out of scope",
    };

    [Fact]
    public void Lexer_round_trips_every_corpus_file()
    {
        Corpus.SkipIfMissing();

        int cases = 0, total = 0, skipped = 0, flaggedExpected = 0, deviations = 0;
        var unexpected = new List<string>();
        var coverage = new List<string>();

        foreach (var relative in Corpus.Cases())
        {
            cases++;
            var text = File.ReadAllText(Path.Combine(Corpus.Root!, relative));
            // With @jsx set, .js files may contain JSX, which is out of scope.
            bool jsx = Corpus.HasDirective(text, "jsx");
            foreach (var (name, unit) in Corpus.Units(relative, text))
            {
                if (!Corpus.IsScript(name) || (jsx && name.Contains(".js", StringComparison.OrdinalIgnoreCase)))
                {
                    skipped++;
                    continue;
                }
                total++;

                var tokens = Tokenizer.All(unit);
                if (!CoversExactly(tokens, unit.Length))
                    coverage.Add($"{relative} ({name})");

                var bad = tokens.FirstOrDefault(t => (t.Flags & Error) != 0);
                if (bad.Flags == TokenFlags.None)
                {
                    if (Environment.GetEnvironmentVariable("PARE_CORPUS_VERBOSE") is not null && Corpus.HasScannerErrorBaseline(relative))
                        output.WriteLine($"MISSED {relative} ({name})");
                    continue;
                }
                if (Corpus.HasScannerErrorBaseline(relative))
                {
                    if (Environment.GetEnvironmentVariable("PARE_CORPUS_VERBOSE") is not null)
                    {
                        var where = SourceLocation.FromOffset(unit, bad.Start);
                        output.WriteLine($"EXPECTED {relative} ({name}):{where.Line}:{where.Column} {bad.Kind} {bad.Flags} '{Truncate(bad.Text)}'");
                    }
                    flaggedExpected++;
                    continue;
                }
                if (Deviations.ContainsKey(Path.GetFileName(relative)))
                {
                    deviations++;
                    continue;
                }
                var at = SourceLocation.FromOffset(unit, bad.Start);
                unexpected.Add($"{relative} ({name}):{at.Line}:{at.Column} {bad.Kind} {bad.Flags} '{Truncate(bad.Text)}'");
            }
        }

        output.WriteLine($"{cases} cases, {total} script files lexed, {skipped} non-script files skipped (tsx, jsx, json, ...); " +
                         $"flagged: {flaggedExpected} with a scanner error baseline, {deviations} known deviations, {unexpected.Count} unexpected");

        Assert.True(coverage.Count == 0, "token spans do not cover:\n" + string.Join('\n', coverage.Take(50)));
        Assert.True(unexpected.Count == 0, Report(unexpected));
    }

    private static bool CoversExactly(List<Tok> tokens, int length)
    {
        int pos = 0;
        foreach (var t in tokens)
        {
            if (t.FullStart != pos || t.Start < t.FullStart || t.End < t.Start)
                return false;
            pos = t.End;
        }
        return pos == length;
    }

    private static string Report(List<string> unexpected)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"{unexpected.Count} files without an error baseline produced error tokens:");
        foreach (var line in unexpected.Take(100))
            sb.AppendLine(line);
        return sb.ToString();
    }

    private static string Truncate(string s) => s.Length <= 40 ? s : s[..40] + "...";
}
