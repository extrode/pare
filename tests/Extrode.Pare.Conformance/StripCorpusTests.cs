using System.Buffers;
using System.Text;
using Acornima;

namespace Extrode.Pare.Conformance;

/// <summary>
/// Strips every corpus file TypeScript parses without a syntax error and checks that: Pare parses
/// it too, the output is valid JavaScript (Acornima), positions are preserved, and plain
/// <c>.js</c> files come back byte-identical.
/// </summary>
public class StripCorpusTests(ITestOutputHelper output)
{
    private const string Nel = "U+0085 NEL: TypeScript scans it as whitespace, ECMAScript and browsers do not";
    private const string Invalid = "invalid input TypeScript reports only from the checker";

    // Files TypeScript parses without a TS1xxx error but Pare rejects as a syntax error.
    private static readonly Dictionary<string, string> SyntaxDeviations = new()
    {
        ["sourceMap-LineBreaks.ts"] = Nel,
        ["fileWithNextLine2.ts"] = Nel,
        ["duplicatePackage.ts"] = "a .d.ts under node_modules holding prose",
        ["exportNonInitializedVariablesUMD.ts"] = Invalid + ": `var;`",
        ["exportNonInitializedVariablesSystem.ts"] = Invalid + ": `var;`",
        ["invalidSyntaxNamespaceImportWithSystem.ts"] = Invalid + ": `import * from Zero from`",
        ["importDeferInvalidDefault.ts"] = Invalid + ": `import defer \"m\"`",
        ["interfacesWithPredefinedTypesAsNames.ts"] = Invalid + ": `interface void {}`",
        ["parserErrorRecovery_ParameterList6.ts"] = Invalid + ": `(x: break)`",
    };

    // .js files TypeScript accepts with TS syntax in them and no TS8xxx baseline.
    private static readonly Dictionary<string, string> JsDeviations = new()
    {
        ["jsFileCompilationWithoutJsExtensions.ts"] = "`declare var v;` in a .js file",
        ["jsdocThisTagWithThisParameter.ts"] = "`function f(this)` in a .js file",
    };

    // Acornima errors that stripping cannot cause: early errors TypeScript reports from the checker
    // (TS2xxx, TS17xxx) on invalid input, and proposals Acornima 1.8.0 does not parse yet.
    private static readonly string[] AcceptedErrors =
    [
        "has already been declared",
        "is not defined in module",
        "Duplicate export of",
        "Duplicate parameter name",
        "A class may only have one constructor",
        "Rest element must be last element",
        "Invalid left-hand side",
        "Invalid destructuring assignment target",
        "must be followed by an assignable reference",
        "Unary operator used immediately before exponentiation",
        "Yield expression not allowed in formal parameter",
        "Illegal await-expression in formal parameters",
        "await is only valid in async functions",
        "directive in function with non-simple parameter list",
        "Classes may not have",
        "Class constructor may not be a private method",
        "Private fields can not be deleted",
        "is not allowed in class field initializer",
        "'super' keyword unexpected here",
        "Unexpected reserved word",
        // Private names outside a class body.
        "Unexpected identifier '#",
        // Deprecated `assert` import attributes.
        "Unexpected identifier 'assert'",
        // Acornima: decorators with a computed member name or before `export`.
        "Invalid decorator member expression",
        "Unexpected token 'export'",
        "Unexpected token '@'",
    ];

    // Invalid input whose Acornima error is too generic to accept by message.
    private static readonly Dictionary<string, string> InvalidDeviations = new()
    {
        ["objectBindingPattern_restElementWithPropertyName.ts"] = Invalid + ": `{...a: b}`",
        ["misspelledNewMetaProperty.ts"] = Invalid + ": `new.targ`",
        ["importAssertionNonstring.ts"] = Invalid + ": non-string attribute value",
        ["importAttributes6.ts"] = Invalid + ": non-string attribute value",
        ["importDeferInvalidNamed.ts"] = Invalid + ": `import defer {}`",
        ["importDeferFromInvalid.ts"] = Invalid + ": `import defer from`",
        ["importMetaPropertyInvalidInCall.ts"] = Invalid + ": `import.foo`",
        ["importSource17.ts"] = Invalid + ": non-string attribute value",
        ["importSource2.ts"] = Invalid + ": `import source {}`",
        ["nullishCoalescingOperator5.ts"] = Invalid + ": `??` mixed with `||`",
        ["asyncFunctionDeclaration8_es5.ts"] = "`await` as a script identifier; Acornima allows top-level await",
        ["asyncFunctionDeclaration8_es6.ts"] = "`await` as a script identifier; Acornima allows top-level await",
        ["asyncFunctionDeclaration8_es2017.ts"] = "`await` as a script identifier; Acornima allows top-level await",
        ["asyncArrowFunction2_es5.ts"] = "`await` as a script identifier; Acornima allows top-level await",
        ["asyncArrowFunction2_es6.ts"] = "`await` as a script identifier; Acornima allows top-level await",
        ["asyncArrowFunction2_es2017.ts"] = "`await` as a script identifier; Acornima allows top-level await",
    };

    private sealed class Sink : IDiagnosticSink
    {
        public readonly List<Diagnostic> Items = [];
        public void Report(in Diagnostic diagnostic) => Items.Add(diagnostic);
    }

    internal static readonly ParserOptions JsOptions = new()
    {
        EcmaVersion = EcmaVersion.Latest,
        ExperimentalESFeatures = ExperimentalESFeatures.Decorators | ExperimentalESFeatures.SourcePhaseImports |
            ExperimentalESFeatures.DeferImportEvaluation,
        AllowReturnOutsideFunction = true,
        AllowImportExportEverywhere = true,
        AllowAwaitOutsideFunction = true,
        AllowNewTargetOutsideFunction = true,
        AllowSuperOutsideMethod = true,
        AllowSuperCallOutsideConstructor = true,
        AllowTopLevelUsing = true,
        CheckPrivateFields = false,
    };

    [Fact]
    public void Strip_handles_every_corpus_file_TypeScript_parses()
    {
        Corpus.SkipIfMissing();

        int eligible = 0, stripped = 0, rejected = 0, deviations = 0, jsFiles = 0, jsInvalidBoth = 0, acceptedInvalid = 0;
        var syntaxErrors = new List<string>();
        var invalidOutput = new List<string>();
        var moved = new List<string>();
        var jsChanged = new List<string>();
        var trace = Environment.GetEnvironmentVariable("PARE_CORPUS_TRACE");
        var parser = new Parser(JsOptions);

        foreach (var (relative, name, unit) in Corpus.StripUnits())
        {
            eligible++;
            if (trace is not null)
                File.WriteAllText(trace, $"{relative} ({name})");
            string where = $"{relative} ({name})";

            var sink = new Sink();
            var buffer = new ArrayBufferWriter<char>();
            if (!TypeStripper.Strip(unit, buffer, sink))
            {
                var syntax = sink.Items.FirstOrDefault(d => d.Code == DiagnosticCode.SyntaxError);
                if (syntax.Code != DiagnosticCode.SyntaxError)
                {
                    rejected++;
                }
                else if (SyntaxDeviations.ContainsKey(Path.GetFileName(relative)))
                {
                    deviations++;
                }
                else
                {
                    var at = SourceLocation.FromOffset(unit, syntax.Start);
                    syntaxErrors.Add($"{where}:{at.Line}:{at.Column}  {Truncate(LineAt(unit, at.Line).Trim())}");
                }
                continue;
            }
            stripped++;
            var js = buffer.WrittenSpan.ToString();

            if (PositionMoved(unit, js) is int offset)
            {
                var at = SourceLocation.FromOffset(unit, offset);
                moved.Add($"{where}:{at.Line}:{at.Column}");
            }

            bool isJs = name.EndsWith(".js", StringComparison.OrdinalIgnoreCase) ||
                name.EndsWith(".mjs", StringComparison.OrdinalIgnoreCase) ||
                name.EndsWith(".cjs", StringComparison.OrdinalIgnoreCase);
            // TS8xxx: "... can only be used in TypeScript files", so the .js holds TS syntax.
            if (isJs && !Corpus.HasBaselineCode(relative, "TS8") && !JsDeviations.ContainsKey(Path.GetFileName(relative)))
            {
                jsFiles++;
                if (js != unit)
                    jsChanged.Add($"{where}:{FirstDifference(unit, js)}");
            }

            var error = ParseError(parser, js);
            if (error is not null)
            {
                // Input JS that Acornima rejects too is a checker-level error in the corpus.
                if (isJs && ParseError(parser, unit) is not null)
                {
                    jsInvalidBoth++;
                    continue;
                }
                if (InvalidDeviations.ContainsKey(Path.GetFileName(relative)) ||
                    AcceptedErrors.Any(e => error.Contains(e, StringComparison.Ordinal)))
                {
                    acceptedInvalid++;
                    continue;
                }
                invalidOutput.Add($"{where}: {error}");
            }
        }

        output.WriteLine($"{eligible} files without a TS1xxx baseline: {stripped} stripped, {rejected} rejected (section 6), " +
                         $"{deviations} known deviations, {syntaxErrors.Count} syntax errors");
        output.WriteLine($"output: {invalidOutput.Count} not valid JS ({acceptedInvalid} accepted invalid input), {moved.Count} with moved positions; " +
                         $"{jsFiles} .js files, {jsChanged.Count} changed, {jsInvalidBoth} invalid JS input");
        var report = Environment.GetEnvironmentVariable("PARE_CORPUS_REPORT");
        if (report is not null)
            File.WriteAllLines(report, [.. syntaxErrors, "--- invalid output", .. invalidOutput, "--- moved", .. moved, "--- js changed", .. jsChanged]);

        Assert.True(syntaxErrors.Count == 0, Report("failed with a syntax error", syntaxErrors));
        Assert.True(jsChanged.Count == 0, Report(".js files changed", jsChanged));
        Assert.True(moved.Count == 0, Report("moved a surviving character", moved));
        Assert.True(invalidOutput.Count == 0, Report("produced invalid JS", invalidOutput));
    }

    private static string? ParseError(Parser parser, string js)
    {
        try
        {
            parser.ParseModule(js);
            return null;
        }
        catch (ParseErrorException module)
        {
            try
            {
                parser.ParseScript(js);
                return null;
            }
            catch (Exception)
            {
                // Acornima 1.8.0 can throw NullReferenceException on `export` in script mode.
                return $"{module.LineNumber}:{module.Column} {module.Message}";
            }
        }
    }

    /// <summary>
    /// The first offset where the output keeps a character that differs from the input, other than
    /// the documented ASI fixes (<c>;</c> written over an erased range, <c>(</c> or <c>)</c> moved).
    /// </summary>
    private static int? PositionMoved(string input, string js)
    {
        if (input.Length != js.Length)
            return 0;
        for (int i = 0; i < input.Length; i++)
        {
            char o = js[i];
            if (o == input[i] || o == ' ')
                continue;
            if (o is ';' or '(' or ')')
                continue;
            return i;
        }
        return null;
    }

    private static string FirstDifference(string a, string b)
    {
        int i = 0;
        while (i < a.Length && i < b.Length && a[i] == b[i])
            i++;
        var at = SourceLocation.FromOffset(a, i);
        return $"{at.Line}:{at.Column}";
    }

    private static string LineAt(string text, int line) => text.Split('\n')[line - 1].TrimEnd('\r');

    private static string Report(string what, List<string> lines)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"{lines.Count} files {what}:");
        foreach (var line in lines.Take(50))
            sb.AppendLine(line);
        return sb.ToString();
    }

    private static string Truncate(string s) => s.Length <= 100 ? s : s[..100] + "...";
}
