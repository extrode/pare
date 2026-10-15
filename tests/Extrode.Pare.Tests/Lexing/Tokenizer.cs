using Extrode.Pare.Lexing;

namespace Extrode.Pare.Tests.Lexing;

internal readonly record struct Tok(TokenKind Kind, string Text, Keyword Keyword, TokenFlags Flags, int FullStart, int Start, int End);

/// <summary>
/// Drives the lexer without a parser, for tests and the corpus round-trip. It stands in for the
/// parser's goal-symbol knowledge with heuristics: a regex may start after anything that cannot
/// end an expression, and a brace stack tells template substitutions from blocks. The
/// recognizer replaces both with grammar.
/// </summary>
internal static class Tokenizer
{
    public static List<Tok> All(string source, bool rescan = true)
    {
        var lexer = new Lexer(source);
        var tokens = new List<Tok>();
        var braces = new Stack<bool>(); // true: the brace closes a template substitution
        Tok? prev = null;

        while (true)
        {
            int before = lexer.End;
            lexer.Next();

            if (rescan)
            {
                if (lexer.Kind is TokenKind.Slash or TokenKind.SlashEquals && RegexAllowed(prev))
                    lexer.ReScanSlashAsRegex();
                else if (lexer.Kind == TokenKind.GreaterThan)
                    lexer.ReScanGreaterThan();
                else if (lexer.Kind == TokenKind.CloseBrace && braces.TryPop(out bool substitution) && substitution)
                    lexer.ReScanTemplateContinuation();

                if (lexer.Kind == TokenKind.OpenBrace)
                    braces.Push(false);
                else if (lexer.Kind is TokenKind.TemplateHead or TokenKind.TemplateMiddle)
                    braces.Push(true);
            }

            var tok = new Tok(lexer.Kind, lexer.Text.ToString(), lexer.Keyword, lexer.Flags, lexer.FullStart, lexer.Start, lexer.End);
            tokens.Add(tok);
            if (tok.Kind == TokenKind.EndOfFile)
                return tokens;
            if (lexer.End <= before)
                throw new InvalidOperationException($"lexer did not advance at {before}");
            prev = tok;
        }
    }

    private static bool RegexAllowed(Tok? prev)
    {
        if (prev is not { } p)
            return true;
        return p.Kind switch
        {
            TokenKind.Identifier => (Keywords.IsReserved(p.Keyword) &&
                                     p.Keyword is not (Keyword.This or Keyword.Super or Keyword.Null or Keyword.True or Keyword.False)) ||
                                    p.Keyword is Keyword.Yield or Keyword.Await,
            TokenKind.CloseParen or TokenKind.CloseBracket or TokenKind.NumericLiteral or TokenKind.BigIntLiteral or
            TokenKind.StringLiteral or TokenKind.RegularExpression or TokenKind.NoSubstitutionTemplate or
            TokenKind.TemplateTail or TokenKind.PrivateName or TokenKind.PlusPlus or TokenKind.MinusMinus => false,
            _ => true,
        };
    }

    public static TokenKind[] Kinds(string source, bool rescan = true) =>
        All(source, rescan).Where(t => t.Kind != TokenKind.EndOfFile).Select(t => t.Kind).ToArray();

    public static string[] Texts(string source, bool rescan = true) =>
        All(source, rescan).Where(t => t.Kind != TokenKind.EndOfFile).Select(t => t.Text).ToArray();
}
