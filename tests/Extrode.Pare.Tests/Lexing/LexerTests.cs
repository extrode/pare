using Extrode.Pare.Lexing;
using static Extrode.Pare.Lexing.TokenKind;

namespace Extrode.Pare.Tests.Lexing;

public class LexerTests
{
    private static Tok Single(string source)
    {
        var tokens = Tokenizer.All(source);
        Assert.Equal(2, tokens.Count);
        return tokens[0];
    }

    [Theory]
    [InlineData("{ } ( ) [ ] ; , ~ @ :", new[] { OpenBrace, CloseBrace, OpenParen, CloseParen, OpenBracket, CloseBracket, Semicolon, Comma, Tilde, At, Colon })]
    [InlineData(". ... ?. ? ?? ??=", new[] { Dot, DotDotDot, QuestionDot, Question, QuestionQuestion, QuestionQuestionEquals })]
    [InlineData("< <= << <<=", new[] { LessThan, LessThanEquals, LessThanLessThan, LessThanLessThanEquals })]
    [InlineData("= == === => ! != !==", new[] { TokenKind.Equals, EqualsEquals, EqualsEqualsEquals, EqualsGreaterThan, Exclamation, ExclamationEquals, ExclamationEqualsEquals })]
    [InlineData("+ ++ += - -- -=", new[] { Plus, PlusPlus, PlusEquals, Minus, MinusMinus, MinusEquals })]
    [InlineData("* ** *= **= % %=", new[] { Asterisk, AsteriskAsterisk, AsteriskEquals, AsteriskAsteriskEquals, Percent, PercentEquals })]
    [InlineData("& && &= &&= | || |= ||= ^ ^=", new[] { Ampersand, AmpersandAmpersand, AmpersandEquals, AmpersandAmpersandEquals, Bar, BarBar, BarEquals, BarBarEquals, Caret, CaretEquals })]
    [InlineData("a / b /= c", new[] { Identifier, Slash, Identifier, SlashEquals, Identifier })]
    public void Punctuators(string source, object expected)
    {
        Assert.Equal((TokenKind[])expected, Tokenizer.Kinds(source));
    }

    [Fact]
    public void Question_dot_before_a_digit_is_a_conditional()
    {
        Assert.Equal([Identifier, Question, NumericLiteral, Colon, Identifier], Tokenizer.Kinds("a?.5:b"));
    }

    [Theory]
    [InlineData(">", GreaterThan)]
    [InlineData(">=", GreaterThanEquals)]
    [InlineData(">>", GreaterThanGreaterThan)]
    [InlineData(">>=", GreaterThanGreaterThanEquals)]
    [InlineData(">>>", GreaterThanGreaterThanGreaterThan)]
    [InlineData(">>>=", GreaterThanGreaterThanGreaterThanEquals)]
    public void Greater_than_rescans_to_the_longest_operator(string source, object expected)
    {
        Assert.Equal((TokenKind)expected, Single(source).Kind);
    }

    [Fact]
    public void Greater_than_is_scanned_alone_without_rescan_so_type_arguments_can_close_twice()
    {
        Assert.Equal([Identifier, LessThan, Identifier, LessThan, Identifier, GreaterThan, GreaterThan],
            Tokenizer.Kinds("A<B<C>>", rescan: false));
    }

    [Theory]
    [InlineData("type", Keyword.Type)]
    [InlineData("satisfies", Keyword.Satisfies)]
    [InlineData("instanceof", Keyword.Instanceof)]
    [InlineData("constructor", Keyword.Constructor)]
    [InlineData("accessor", Keyword.Accessor)]
    [InlineData("types", Keyword.None)]
    [InlineData("Type", Keyword.None)]
    [InlineData("$", Keyword.None)]
    [InlineData("_a1", Keyword.None)]
    public void Identifiers_are_classified_as_keywords(string source, object expected)
    {
        var t = Single(source);
        Assert.Equal(Identifier, t.Kind);
        Assert.Equal((Keyword)expected, t.Keyword);
        Assert.Equal(source, t.Text);
    }

    [Theory]
    [InlineData("\\u0074ype")]
    [InlineData("typ\\u{65}")]
    [InlineData("ty\\u0070e")]
    public void Escaped_identifier_is_never_a_keyword(string source)
    {
        var t = Single(source);
        Assert.Equal(Identifier, t.Kind);
        Assert.Equal(Keyword.None, t.Keyword);
        Assert.Equal(TokenFlags.HasEscape, t.Flags);
    }

    [Theory]
    [InlineData("\u00FCn\u00EFcode")]
    [InlineData("\U0001D400x")]
    [InlineData("a\u200Db")]
    [InlineData("\u2118")]
    [InlineData("abc\u00E9")]
    [InlineData("x\U0001D400")]
    public void Unicode_identifiers(string source)
    {
        var t = Single(source);
        Assert.Equal(Identifier, t.Kind);
        Assert.Equal(source, t.Text);
        Assert.Equal(TokenFlags.None, t.Flags);
    }

    [Theory]
    [InlineData("\\u12")]
    [InlineData("\\u{110000}")]
    [InlineData("a\\u0020")]
    public void Malformed_identifier_escape_is_invalid(string source)
    {
        Assert.True(Tokenizer.All(source)[0].Flags.HasFlag(TokenFlags.Invalid));
    }

    [Fact]
    public void Private_names()
    {
        var t = Single("#count");
        Assert.Equal(PrivateName, t.Kind);
        Assert.Equal("#count", t.Text);

        var lone = Tokenizer.All("# x")[0];
        Assert.Equal(Unknown, lone.Kind);
        Assert.Equal(TokenFlags.Invalid, lone.Flags);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("1_000")]
    [InlineData(".5")]
    [InlineData("1.")]
    [InlineData("1e10")]
    [InlineData("1E-3")]
    [InlineData("1.5e+3")]
    [InlineData("0xFF")]
    [InlineData("0B1010")]
    [InlineData("0o17")]
    [InlineData("017")]
    [InlineData("089")]
    public void Numeric_literals(string source)
    {
        var t = Single(source);
        Assert.Equal(NumericLiteral, t.Kind);
        Assert.Equal(source, t.Text);
        Assert.Equal(TokenFlags.None, t.Flags);
    }

    [Theory]
    [InlineData("10n")]
    [InlineData("0xFFn")]
    public void BigInt_literals(string source)
    {
        var t = Single(source);
        Assert.Equal(BigIntLiteral, t.Kind);
        Assert.Equal(source, t.Text);
    }

    [Fact]
    public void Dot_after_a_number_with_a_fraction_dot_is_member_access()
    {
        Assert.Equal(["1.", ".", "toString"], Tokenizer.Texts("1..toString"));
    }

    [Theory]
    [InlineData("3in")]
    [InlineData("0x")]
    [InlineData("1_000n_")]
    public void Malformed_numbers_are_invalid_but_consumed(string source)
    {
        var t = Single(source);
        Assert.Equal(source, t.Text);
        Assert.True(t.Flags.HasFlag(TokenFlags.Invalid));
    }

    [Theory]
    [InlineData("\"a\\\"b\"")]
    [InlineData("'it\\'s'")]
    [InlineData("'a\\\r\nb'")]
    [InlineData("'a\\\nb'")]
    [InlineData("'line\u2028sep'")]
    [InlineData("'\\u{1F600}\\x41\\0'")]
    [InlineData("\"\"")]
    public void String_literals(string source)
    {
        var t = Single(source);
        Assert.Equal(StringLiteral, t.Kind);
        Assert.Equal(source, t.Text);
        Assert.Equal(TokenFlags.None, t.Flags);
    }

    [Theory]
    [InlineData("\"abc\nx", "\"abc")]
    [InlineData("'abc\r\nx", "'abc")]
    [InlineData("'abc", "'abc")]
    [InlineData("'abc\\", "'abc\\")]
    public void Unterminated_string_stops_at_line_end(string source, string text)
    {
        var t = Tokenizer.All(source)[0];
        Assert.Equal(StringLiteral, t.Kind);
        Assert.Equal(text, t.Text);
        Assert.Equal(TokenFlags.Unterminated, t.Flags);
    }

    [Fact]
    public void Template_without_substitution()
    {
        var t = Single("`a\nb \\` $ {}`");
        Assert.Equal(NoSubstitutionTemplate, t.Kind);
        Assert.Equal(TokenFlags.None, t.Flags);
    }

    [Fact]
    public void Template_with_substitutions()
    {
        Assert.Equal(["`a${", "b", "}c${", "d", "}e`"], Tokenizer.Texts("`a${b}c${d}e`"));
        Assert.Equal([TemplateHead, Identifier, TemplateMiddle, Identifier, TemplateTail], Tokenizer.Kinds("`a${b}c${d}e`"));
    }

    [Fact]
    public void Template_continuation_starts_right_after_the_brace()
    {
        Assert.Equal(["`${", "a", "}${", "b", "}`"], Tokenizer.Texts("`${a}${b}`"));
        Assert.Equal(TokenFlags.None, Tokenizer.All("`${a}`")[2].Flags);
    }

    [Fact]
    public void Nested_templates_and_braces_inside_substitutions()
    {
        Assert.Equal(
            ["`a${", "`b${", "c", "}d`", "}e${", "{", "x", ":", "1", "}", ".", "x", "}`"],
            Tokenizer.Texts("`a${`b${c}d`}e${{x:1}.x}`"));
    }

    [Fact]
    public void Escaped_dollar_brace_does_not_open_a_substitution()
    {
        var t = Single("`\\${x}`");
        Assert.Equal(NoSubstitutionTemplate, t.Kind);
    }

    [Fact]
    public void Unterminated_template()
    {
        var t = Tokenizer.All("`abc")[0];
        Assert.Equal(NoSubstitutionTemplate, t.Kind);
        Assert.Equal(TokenFlags.Unterminated, t.Flags);

        var tail = Tokenizer.All("`a${b}c").Last(x => x.Kind != EndOfFile);
        Assert.Equal(TemplateTail, tail.Kind);
        Assert.Equal(TokenFlags.Unterminated, tail.Flags);
    }

    [Theory]
    [InlineData("x = /a[/]b\\/c/gi;", "/a[/]b\\/c/gi")]
    [InlineData("return /=/.test(s)", "/=/")]
    [InlineData("(/x/u)", "/x/u")]
    [InlineData("}/x/", "/x/")]
    [InlineData("typeof /x/", "/x/")]
    public void Regex_where_an_expression_may_start(string source, string regex)
    {
        var t = Tokenizer.All(source).Single(t => t.Kind == RegularExpression);
        Assert.Equal(regex, t.Text);
        Assert.Equal(TokenFlags.None, t.Flags);
    }

    [Theory]
    [InlineData("a / b / c")]
    [InlineData("f(x) / 2 / y")]
    [InlineData("a[0] /= 2")]
    [InlineData("this / 2")]
    [InlineData("i++ / 2")]
    public void Division_after_an_expression(string source)
    {
        Assert.DoesNotContain(Tokenizer.All(source), t => t.Kind == RegularExpression);
    }

    [Fact]
    public void Unterminated_regex_stops_at_line_end()
    {
        var t = Tokenizer.All("x = /abc\ny")[2];
        Assert.Equal(RegularExpression, t.Kind);
        Assert.Equal("/abc", t.Text);
        Assert.Equal(TokenFlags.Unterminated, t.Flags);
    }

    [Theory]
    [InlineData("a // c\nb", true)]
    [InlineData("a /* \n */ b", true)]
    [InlineData("a /* */ b", false)]
    [InlineData("a\u2028b", true)]
    [InlineData("a\u2029b", true)]
    [InlineData("a\rb", true)]
    [InlineData("a \t\v\f\u00A0\uFEFF\u3000b", false)]
    public void Trivia_and_line_breaks(string source, bool lineBreak)
    {
        var tokens = Tokenizer.All(source);
        Assert.Equal([Identifier, Identifier, EndOfFile], tokens.Select(t => t.Kind));
        Assert.Equal(lineBreak, tokens[1].Flags.HasFlag(TokenFlags.PrecededByLineBreak));
    }

    [Fact]
    public void Hashbang_is_trivia_only_at_the_start()
    {
        Assert.Equal(["x"], Tokenizer.Texts("#!/usr/bin/env node\nx"));
        Assert.Equal(Unknown, Tokenizer.All(" #!x")[0].Kind);
    }

    [Fact]
    public void Unterminated_block_comment_flags_end_of_file()
    {
        var tokens = Tokenizer.All("a /* b");
        Assert.Equal(EndOfFile, tokens[1].Kind);
        Assert.Equal(TokenFlags.Unterminated, tokens[1].Flags);
    }

    [Fact]
    public void Unknown_characters_are_consumed_whole()
    {
        var t = Tokenizer.All("\U0001F600 x")[0];
        Assert.Equal(Unknown, t.Kind);
        Assert.Equal("\U0001F600", t.Text);
    }

    [Theory]
    [InlineData("")]
    [InlineData("let x: Map<string, Array<number>> = new Map(); // done\n")]
    [InlineData("  /* a */ `t${ {a:[1,2]}.a }` /x/g.test(s) \r\n #!x")]
    public void Tokens_and_trivia_cover_the_source_exactly(string source)
    {
        var tokens = Tokenizer.All(source);
        int pos = 0;
        foreach (var t in tokens)
        {
            Assert.Equal(pos, t.FullStart);
            Assert.True(t.Start >= t.FullStart);
            pos = t.End;
        }
        Assert.Equal(source.Length, pos);
    }

    [Fact]
    public void End_of_file_repeats()
    {
        var lexer = new Lexer("a");
        lexer.Next();
        lexer.Next();
        Assert.Equal(EndOfFile, lexer.Kind);
        lexer.Next();
        Assert.Equal(EndOfFile, lexer.Kind);
        Assert.Equal(1, lexer.Start);
    }

    [Fact]
    public void Restore_rewinds_to_the_saved_token()
    {
        var lexer = new Lexer("a < b > (c)");
        lexer.Next();
        lexer.Next();
        var saved = lexer.Save();
        lexer.Next();
        lexer.Next();
        lexer.Restore(saved);
        Assert.Equal(LessThan, lexer.Kind);
        lexer.Next();
        Assert.Equal("b", lexer.Text.ToString());
    }

    [Fact]
    public void Rescans_reject_the_wrong_current_token()
    {
        Assert.Throws<InvalidOperationException>(() => { var l = new Lexer("a"); l.Next(); l.ReScanSlashAsRegex(); });
        Assert.Throws<InvalidOperationException>(() => { var l = new Lexer("a"); l.Next(); l.ReScanTemplateContinuation(); });
        Assert.Throws<InvalidOperationException>(() => { var l = new Lexer("a"); l.Next(); l.ReScanGreaterThan(); });
    }
}
