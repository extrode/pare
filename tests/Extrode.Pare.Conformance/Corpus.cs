using System.Text;

namespace Extrode.Pare.Conformance;

/// <summary>
/// The TypeScript test corpus fetched by <c>scripts/get-ts-corpus.cs</c> into
/// <c>.corpus/TypeScript/tsc/testdata</c>.
/// </summary>
internal static class Corpus
{
    public static string? Root { get; } = FindRoot();

    public static void SkipIfMissing()
    {
        Assert.SkipWhen(Root is null, "corpus missing: run `dotnet run scripts/get-ts-corpus.cs`");
    }

    /// <summary>Every <c>.ts</c>/<c>.mts</c>/<c>.cts</c>/<c>.js</c> case, as a path relative to the testdata root.</summary>
    public static IEnumerable<string> Cases()
    {
        foreach (var suite in new[] { "compiler", "conformance" })
        {
            var dir = Path.Combine(Root!, "tests", "cases", suite);
            foreach (var file in Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories))
            {
                if (Path.GetExtension(file) is ".ts" or ".mts" or ".cts" or ".js")
                    yield return Path.GetRelativePath(Root!, file);
            }
        }
    }

    // Diagnostics TypeScript's scanner reports (diagnosticMessages.json): unterminated literals and
    // comments, invalid characters and escapes, malformed numbers.
    private static readonly string[] ScannerErrors =
    [
        "TS1002", "TS1010", "TS1124", "TS1125", "TS1126", "TS1127", "TS1160", "TS1161", "TS1177", "TS1178",
        "TS1198", "TS1199", "TS1351", "TS1489", "TS1490", "TS6188", "TS6189", "TS18026",
    ];

    /// <summary>
    /// True when TypeScript's baseline for the case (<c>name.errors.txt</c> or
    /// <c>name(options).errors.txt</c>) records a scanner error.
    /// </summary>
    public static bool HasScannerErrorBaseline(string relativeCase)
    {
        var parts = relativeCase.Split(Path.DirectorySeparatorChar);
        var suite = parts[2];
        var name = Path.GetFileNameWithoutExtension(relativeCase);
        var dir = Path.Combine(Root!, "baselines", "reference", suite);
        var plain = Path.Combine(dir, name + ".errors.txt");
        var baselines = Directory.EnumerateFiles(dir, name + "(*).errors.txt");
        if (File.Exists(plain))
            baselines = baselines.Append(plain);
        foreach (var baseline in baselines)
        {
            var text = File.ReadAllText(baseline);
            foreach (var code in ScannerErrors)
            {
                if (text.Contains("error " + code + ":", StringComparison.Ordinal))
                    return true;
            }
        }
        return false;
    }

    /// <summary>
    /// True when TypeScript's baseline for the case records any TS1xxx error: syntax and grammar
    /// errors. Type errors (TS2xxx and up) do not matter to a type stripper.
    /// </summary>
    public static bool HasSyntaxErrorBaseline(string relativeCase)
    {
        foreach (var baseline in Baselines(relativeCase))
        {
            var text = File.ReadAllText(baseline);
            for (int i = text.IndexOf("error TS1", StringComparison.Ordinal); i >= 0;
                 i = text.IndexOf("error TS1", i + 1, StringComparison.Ordinal))
            {
                // "error TS1xxx:" with exactly four digits.
                if (i + 12 < text.Length && text[i + 12] == ':')
                    return true;
            }
            // "Declaration or statement expected" for `{ a } = b`, reported by the checker.
            if (text.Contains("error TS2809:", StringComparison.Ordinal))
                return true;
        }
        return false;
    }

    /// <summary>True when any baseline for the case records an error whose code starts with <paramref name="prefix"/>.</summary>
    public static bool HasBaselineCode(string relativeCase, string prefix)
    {
        foreach (var baseline in Baselines(relativeCase))
        {
            if (File.ReadAllText(baseline).Contains("error " + prefix, StringComparison.Ordinal))
                return true;
        }
        return false;
    }

    private static IEnumerable<string> Baselines(string relativeCase)
    {
        var parts = relativeCase.Split(Path.DirectorySeparatorChar);
        var suite = parts[2];
        var name = Path.GetFileNameWithoutExtension(relativeCase);
        var dir = Path.Combine(Root!, "baselines", "reference", suite);
        var plain = Path.Combine(dir, name + ".errors.txt");
        var baselines = Directory.EnumerateFiles(dir, name + "(*).errors.txt");
        return File.Exists(plain) ? baselines.Append(plain) : baselines;
    }

    /// <summary>
    /// Splits a case into the files it declares, the way TypeScript's harness does
    /// (<c>makeUnitsFromTest</c>): a <c>// @filename: x</c> line starts a new file, and every
    /// <c>// @option: value</c> line is metadata, not content. Content before the first
    /// <c>@filename</c> belongs to a file named after the case.
    /// </summary>
    public static List<(string Name, string Text)> Units(string relativeCase, string text)
    {
        var units = new List<(string, string)>();
        var name = Path.GetFileName(relativeCase);
        var content = new StringBuilder();
        foreach (var line in text.Split('\n'))
        {
            var directive = Directive(line);
            if (directive is ("filename", var file))
            {
                if (content.Length > 0 || units.Count > 0)
                    units.Add((name, content.ToString()));
                name = file;
                content.Clear();
                continue;
            }
            if (directive is not null)
                continue;
            content.Append(line).Append('\n');
        }
        units.Add((name, content.ToString()));
        return units;
    }

    public static bool IsScript(string name) =>
        name.EndsWith(".ts", StringComparison.OrdinalIgnoreCase) ||
        name.EndsWith(".mts", StringComparison.OrdinalIgnoreCase) ||
        name.EndsWith(".cts", StringComparison.OrdinalIgnoreCase) ||
        name.EndsWith(".js", StringComparison.OrdinalIgnoreCase) ||
        name.EndsWith(".mjs", StringComparison.OrdinalIgnoreCase) ||
        name.EndsWith(".cjs", StringComparison.OrdinalIgnoreCase);

    /// <summary>True when any line of the case is the directive <c>// @key: ...</c>.</summary>
    public static bool HasDirective(string text, string key)
    {
        foreach (var line in text.Split('\n'))
        {
            if (Directive(line) is ({ } k, _) && k == key)
                return true;
        }
        return false;
    }

    // TypeScript's harness: /^\/{2}\s*@(\w+)\s*:\s*([^\r]*)/
    private static (string Key, string Value)? Directive(string line)
    {
        var s = line.AsSpan().TrimStart();
        if (!s.StartsWith("//"))
            return null;
        s = s[2..].TrimStart();
        if (s.Length == 0 || s[0] != '@')
            return null;
        s = s[1..];
        int n = 0;
        while (n < s.Length && (char.IsAsciiLetterOrDigit(s[n]) || s[n] == '_'))
            n++;
        if (n == 0)
            return null;
        var key = s[..n].ToString().ToLowerInvariant();
        var rest = s[n..].TrimStart();
        if (rest.Length == 0 || rest[0] != ':')
            return null;
        var value = rest[1..].Trim().ToString();
        int cr = value.IndexOf('\r');
        return (key, cr < 0 ? value : value[..cr]);
    }

    private static string? FindRoot()
    {
        for (var dir = AppContext.BaseDirectory; dir is not null; dir = Path.GetDirectoryName(dir))
        {
            if (File.Exists(Path.Combine(dir, "Pare.slnx")))
            {
                var root = Path.Combine(dir, ".corpus", "TypeScript", "tsc", "testdata");
                return Directory.Exists(Path.Combine(root, "tests", "cases")) ? root : null;
            }
        }
        return null;
    }
}
