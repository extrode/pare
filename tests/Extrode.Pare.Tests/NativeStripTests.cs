using System.Text;
using Extrode.Pare.Native;

namespace Extrode.Pare.Tests;

public class NativeStripTests
{
    private static (int Status, string Output, List<NativeDiagnostic> Diagnostics) Strip(byte[] source, uint options = 0)
    {
        var output = new byte[source.Length];
        var diagnostics = new List<NativeDiagnostic>();
        int status = NativeStrip.Strip(source, options, output, out int written, diagnostics);
        return (status, Encoding.UTF8.GetString(output, 0, written), diagnostics);
    }

    private static (int Status, string Output, List<NativeDiagnostic> Diagnostics) Strip(string source, uint options = 0) =>
        Strip(Encoding.UTF8.GetBytes(source), options);

    [Theory]
    [InlineData("", "")]
    [InlineData("let a: T = 1;\n", "let a    = 1;\n")]
    [InlineData("let s = '\u00E9\U0001F600';\u2028x as T;", "let s = '\u00E9\U0001F600';\u2028x     ;")]
    public void Strips_utf8(string source, string expected)
    {
        var (status, output, diagnostics) = Strip(source);

        Assert.Equal(NativeStrip.Ok, status);
        Assert.Equal(expected, output);
        Assert.Empty(diagnostics);
    }

    [Fact]
    public void Non_ascii_in_a_blanked_type_shortens_the_bytes_but_not_the_columns()
    {
        var (status, output, _) = Strip("let a: Caf\u00E9 = 1;");

        Assert.Equal(NativeStrip.Ok, status);
        Assert.Equal("let a       = 1;", output);
        Assert.True(Encoding.UTF8.GetByteCount(output) < Encoding.UTF8.GetByteCount("let a: Caf\u00E9 = 1;"));
    }

    [Fact]
    public void Rejection_reports_byte_offsets_and_utf16_columns()
    {
        var (status, output, diagnostics) = Strip("let \u00E9 = 1;\r\nx; enum E { A }\n");

        Assert.Equal(NativeStrip.Rejected, status);
        Assert.Equal("", output);
        var d = Assert.Single(diagnostics);
        Assert.Equal((int)DiagnosticCode.Enum, d.Code);
        Assert.Equal(2, d.Line);
        Assert.Equal(4, d.Column);
        Assert.Equal(Encoding.UTF8.GetByteCount("let \u00E9 = 1;\r\nx; "), d.Start);
    }

    [Theory]
    [InlineData("enum A {}\nenum B {}\nlet x = <T>y;\n")]
    [InlineData("let s = '\u00E9\u00E9\u00E9';\r\nenum A {}\r\n\u2028namespace N { let v = 1; }\u2029let = ;")]
    [InlineData("/* \U0001F600\U0001F600 */ enum A {}\renum B {}\r\n\r\nclass C { constructor(private x: \u00E9) {} }")]
    [InlineData("\uFEFFenum A {}\nlet y = <\u00E9>z;")]
    public void Located_diagnostics_match_a_recount_from_the_start(string source)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(source);
        int bom = bytes.AsSpan().StartsWith((byte[])[0xEF, 0xBB, 0xBF]) ? 3 : 0;
        string text = Encoding.UTF8.GetString(bytes, bom, bytes.Length - bom);
        TypeStripper.Strip(text, out var expected);

        var (status, _, diagnostics) = Strip(bytes);

        Assert.Equal(NativeStrip.Rejected, status);
        Assert.True(expected.Count > 1);
        Assert.Equal(expected.Select(d =>
        {
            var at = SourceLocation.FromOffset(text, d.Start);
            return new NativeDiagnostic((int)d.Code, at.Line, at.Column,
                bom + Encoding.UTF8.GetByteCount(text.AsSpan(0, d.Start)),
                Encoding.UTF8.GetByteCount(text.AsSpan(d.Start, d.Length)));
        }), diagnostics);
    }

    [Fact]
    public void Leading_bom_is_dropped_but_still_counted_in_byte_offsets()
    {
        byte[] bom = [0xEF, 0xBB, 0xBF];

        var (status, output, _) = Strip([.. bom, .. "let a: T;"u8]);
        Assert.Equal(NativeStrip.Ok, status);
        Assert.Equal("let a   ;", output);

        (status, _, var diagnostics) = Strip([.. bom, .. "enum E {}"u8]);
        Assert.Equal(NativeStrip.Rejected, status);
        Assert.Equal(new NativeDiagnostic((int)DiagnosticCode.Enum, 1, 1, 3, diagnostics[0].Length), diagnostics[0]);
    }

    [Theory]
    [InlineData(new byte[] { 0x6C, 0xFF })]
    [InlineData(new byte[] { 0x6C, 0xC3 })]
    [InlineData(new byte[] { 0xED, 0xA0, 0x80 })]
    public void Invalid_utf8_is_invalid(byte[] source)
    {
        var (status, output, diagnostics) = Strip(source);

        Assert.Equal(NativeStrip.Invalid, status);
        Assert.Equal("", output);
        Assert.Empty(diagnostics);
    }

    [Theory]
    [InlineData(2u)]
    [InlineData(0x80000000u)]
    public void Unknown_option_bits_are_invalid(uint options)
    {
        Assert.Equal(NativeStrip.Invalid, Strip("x;", options).Status);
    }

    [Fact]
    public void Rewrite_option_rewrites_relative_imports()
    {
        var (status, output, _) = Strip("import a from './a.ts';", NativeStrip.KnownOptions);

        Assert.Equal(NativeStrip.Ok, status);
        Assert.Equal("import a from './a.js';", output);
    }
}
