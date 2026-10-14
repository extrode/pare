using System.Buffers;

namespace Extrode.Pare.Lexing;

/// <summary>
/// Everything the lexer knows. A plain struct, so a speculative parse is
/// <c>var saved = lexer.Save(); ...; lexer.Restore(saved);</c>.
/// </summary>
internal struct LexerState
{
    /// <summary>Where the next scan starts: the end of the current token.</summary>
    public int Pos;
    /// <summary>Start of the trivia before the current token (the previous token's end).</summary>
    public int FullStart;
    public int Start;
    public TokenKind Kind;
    public Keyword Keyword;
    public TokenFlags Flags;
}

/// <summary>
/// Scans TypeScript tokens on demand, the way TypeScript's <c>scanner.ts</c> does: context-free
/// by default, with re-scan methods the parser calls where only it knows the goal symbol
/// (<c>reScanSlashToken</c>, <c>reScanTemplateToken</c>, <c>reScanGreaterToken</c>).
/// </summary>
internal ref struct Lexer
{
    private static readonly SearchValues<char> LineTerminators = SearchValues.Create("\n\r\u2028\u2029");
    private static readonly SearchValues<char> DoubleQuoteStops = SearchValues.Create("\"\\\n\r");
    private static readonly SearchValues<char> SingleQuoteStops = SearchValues.Create("'\\\n\r");
    private static readonly SearchValues<char> TemplateStops = SearchValues.Create("`\\$");

    private readonly ReadOnlySpan<char> _src;
    private LexerState _s;

    public Lexer(ReadOnlySpan<char> source)
    {
        _src = source;
        _s = default;
    }

    public readonly TokenKind Kind => _s.Kind;
    public readonly Keyword Keyword => _s.Keyword;
    public readonly TokenFlags Flags => _s.Flags;
    public readonly int FullStart => _s.FullStart;
    public readonly int Start => _s.Start;
    public readonly int End => _s.Pos;
    public readonly ReadOnlySpan<char> Text => _src[_s.Start.._s.Pos];
    public readonly bool HasPrecedingLineBreak => (_s.Flags & TokenFlags.PrecededByLineBreak) != 0;

    public readonly LexerState Save() => _s;

    public void Restore(in LexerState state) => _s = state;

    /// <summary>Scans the next token, skipping trivia. At end of input it keeps returning EndOfFile.</summary>
    public void Next()
    {
        _s.FullStart = _s.Pos;
        _s.Flags = TokenFlags.None;
        _s.Keyword = Keyword.None;
        SkipTrivia();
        _s.Start = _s.Pos;

        if (_s.Pos >= _src.Length)
        {
            _s.Kind = TokenKind.EndOfFile;
            return;
        }

        char c = _src[_s.Pos];
        switch (c)
        {
            case '"':
            case '\'':
                ScanString(c);
                return;
            case '`':
                _s.Pos++;
                ScanTemplate(TokenKind.NoSubstitutionTemplate, TokenKind.TemplateHead);
                return;
            case >= '0' and <= '9':
                ScanNumber();
                return;
            case '.':
                if (CharClass.IsDigit(Peek(1)))
                    ScanNumber();
                else if (Peek(1) == '.' && Peek(2) == '.')
                    Punct(TokenKind.DotDotDot, 3);
                else
                    Punct(TokenKind.Dot, 1);
                return;
            case '{': Punct(TokenKind.OpenBrace, 1); return;
            case '}': Punct(TokenKind.CloseBrace, 1); return;
            case '(': Punct(TokenKind.OpenParen, 1); return;
            case ')': Punct(TokenKind.CloseParen, 1); return;
            case '[': Punct(TokenKind.OpenBracket, 1); return;
            case ']': Punct(TokenKind.CloseBracket, 1); return;
            case ';': Punct(TokenKind.Semicolon, 1); return;
            case ',': Punct(TokenKind.Comma, 1); return;
            case '~': Punct(TokenKind.Tilde, 1); return;
            case '@': Punct(TokenKind.At, 1); return;
            case ':': Punct(TokenKind.Colon, 1); return;
            case '?':
                if (Peek(1) == '?')
                {
                    if (Peek(2) == '=') Punct(TokenKind.QuestionQuestionEquals, 3);
                    else Punct(TokenKind.QuestionQuestion, 2);
                }
                // `a?.5:b` is a conditional, not optional chaining.
                else if (Peek(1) == '.' && !CharClass.IsDigit(Peek(2)))
                    Punct(TokenKind.QuestionDot, 2);
                else
                    Punct(TokenKind.Question, 1);
                return;
            case '<':
                if (Peek(1) == '<')
                {
                    if (Peek(2) == '=') Punct(TokenKind.LessThanLessThanEquals, 3);
                    else Punct(TokenKind.LessThanLessThan, 2);
                }
                else if (Peek(1) == '=') Punct(TokenKind.LessThanEquals, 2);
                else Punct(TokenKind.LessThan, 1);
                return;
            case '>':
                Punct(TokenKind.GreaterThan, 1);
                return;
            case '=':
                if (Peek(1) == '=')
                {
                    if (Peek(2) == '=') Punct(TokenKind.EqualsEqualsEquals, 3);
                    else Punct(TokenKind.EqualsEquals, 2);
                }
                else if (Peek(1) == '>') Punct(TokenKind.EqualsGreaterThan, 2);
                else Punct(TokenKind.Equals, 1);
                return;
            case '!':
                if (Peek(1) == '=')
                {
                    if (Peek(2) == '=') Punct(TokenKind.ExclamationEqualsEquals, 3);
                    else Punct(TokenKind.ExclamationEquals, 2);
                }
                else Punct(TokenKind.Exclamation, 1);
                return;
            case '+':
                if (Peek(1) == '+') Punct(TokenKind.PlusPlus, 2);
                else if (Peek(1) == '=') Punct(TokenKind.PlusEquals, 2);
                else Punct(TokenKind.Plus, 1);
                return;
            case '-':
                if (Peek(1) == '-') Punct(TokenKind.MinusMinus, 2);
                else if (Peek(1) == '=') Punct(TokenKind.MinusEquals, 2);
                else Punct(TokenKind.Minus, 1);
                return;
            case '*':
                if (Peek(1) == '*')
                {
                    if (Peek(2) == '=') Punct(TokenKind.AsteriskAsteriskEquals, 3);
                    else Punct(TokenKind.AsteriskAsterisk, 2);
                }
                else if (Peek(1) == '=') Punct(TokenKind.AsteriskEquals, 2);
                else Punct(TokenKind.Asterisk, 1);
                return;
            case '/':
                if (Peek(1) == '=') Punct(TokenKind.SlashEquals, 2);
                else Punct(TokenKind.Slash, 1);
                return;
            case '%':
                if (Peek(1) == '=') Punct(TokenKind.PercentEquals, 2);
                else Punct(TokenKind.Percent, 1);
                return;
            case '&':
                if (Peek(1) == '&')
                {
                    if (Peek(2) == '=') Punct(TokenKind.AmpersandAmpersandEquals, 3);
                    else Punct(TokenKind.AmpersandAmpersand, 2);
                }
                else if (Peek(1) == '=') Punct(TokenKind.AmpersandEquals, 2);
                else Punct(TokenKind.Ampersand, 1);
                return;
            case '|':
                if (Peek(1) == '|')
                {
                    if (Peek(2) == '=') Punct(TokenKind.BarBarEquals, 3);
                    else Punct(TokenKind.BarBar, 2);
                }
                else if (Peek(1) == '=') Punct(TokenKind.BarEquals, 2);
                else Punct(TokenKind.Bar, 1);
                return;
            case '^':
                if (Peek(1) == '=') Punct(TokenKind.CaretEquals, 2);
                else Punct(TokenKind.Caret, 1);
                return;
            case '#':
                _s.Pos++;
                if (StartsIdentifier(_s.Pos))
                {
                    ScanIdentifierRest();
                    _s.Kind = TokenKind.PrivateName;
                    _s.Keyword = Keyword.None;
                }
                else
                {
                    _s.Kind = TokenKind.Unknown;
                    _s.Flags |= TokenFlags.Invalid;
                }
                return;
            default:
                if (StartsIdentifier(_s.Pos))
                {
                    ScanIdentifierRest();
                    _s.Kind = TokenKind.Identifier;
                    return;
                }
                _s.Pos += CodePointWidth(_s.Pos);
                _s.Kind = TokenKind.Unknown;
                _s.Flags |= TokenFlags.Invalid;
                return;
        }
    }

    /// <summary>
    /// The current token is <c>/</c> or <c>/=</c> in a place that expects an expression:
    /// re-scan it as a regular expression literal. TypeScript: <c>reScanSlashToken</c>.
    /// </summary>
    public void ReScanSlashAsRegex()
    {
        if (_s.Kind is not (TokenKind.Slash or TokenKind.SlashEquals))
            throw new InvalidOperationException("current token is not '/' or '/='");

        int p = _s.Start + 1;
        bool inClass = false;
        while (true)
        {
            if (p >= _src.Length || CharClass.IsLineTerminator(_src[p]))
            {
                _s.Flags |= TokenFlags.Unterminated;
                break;
            }
            char c = _src[p];
            if (c == '\\')
            {
                p++;
                if (p < _src.Length && !CharClass.IsLineTerminator(_src[p]))
                    p++;
                continue;
            }
            p++;
            if (c == '/' && !inClass)
                break;
            if (c == '[')
                inClass = true;
            else if (c == ']')
                inClass = false;
        }
        // Flags. Validating them is the browser's job.
        while (p < _src.Length)
        {
            int cp = CodePointAt(p, out int width);
            if (!CharClass.IsIdentifierPart(cp))
                break;
            p += width;
        }
        _s.Pos = p;
        _s.Kind = TokenKind.RegularExpression;
    }

    /// <summary>
    /// The current token is the <c>}</c> that closes a template substitution: re-scan from it as
    /// a template middle or tail. TypeScript: <c>reScanTemplateToken</c>.
    /// </summary>
    public void ReScanTemplateContinuation()
    {
        if (_s.Kind != TokenKind.CloseBrace)
            throw new InvalidOperationException("current token is not '}'");
        _s.Pos = _s.Start + 1;
        ScanTemplate(TokenKind.TemplateTail, TokenKind.TemplateMiddle);
    }

    /// <summary>
    /// The current token is <c>&gt;</c> in an expression: extend it to <c>&gt;&gt;</c>,
    /// <c>&gt;=</c>, <c>&gt;&gt;&gt;=</c> and so on. Type argument lists never call this, which is
    /// how <c>A&lt;B&lt;C&gt;&gt;</c> closes twice. TypeScript: <c>reScanGreaterToken</c>.
    /// </summary>
    public void ReScanGreaterThan()
    {
        if (_s.Kind != TokenKind.GreaterThan)
            throw new InvalidOperationException("current token is not '>'");
        int p = _s.Start + 1;
        if (At(p) == '>')
        {
            if (At(p + 1) == '>')
            {
                if (At(p + 2) == '=') { _s.Kind = TokenKind.GreaterThanGreaterThanGreaterThanEquals; _s.Pos = p + 3; }
                else { _s.Kind = TokenKind.GreaterThanGreaterThanGreaterThan; _s.Pos = p + 2; }
            }
            else if (At(p + 1) == '=') { _s.Kind = TokenKind.GreaterThanGreaterThanEquals; _s.Pos = p + 2; }
            else { _s.Kind = TokenKind.GreaterThanGreaterThan; _s.Pos = p + 1; }
        }
        else if (At(p) == '=') { _s.Kind = TokenKind.GreaterThanEquals; _s.Pos = p + 1; }
    }

    /// <summary>
    /// The current token starts with <c>&lt;</c> where a type argument or parameter list opens:
    /// shrink it to a lone <c>&lt;</c>, so <c>f&lt;&lt;T&gt;() =&gt; T&gt;()</c> opens twice.
    /// TypeScript: <c>reScanLessThanToken</c>.
    /// </summary>
    public void ReScanLessThan()
    {
        if (_s.Kind is TokenKind.LessThanLessThan or TokenKind.LessThanLessThanEquals or TokenKind.LessThanEquals)
        {
            _s.Kind = TokenKind.LessThan;
            _s.Pos = _s.Start + 1;
        }
    }

    private void SkipTrivia()
    {
        while (_s.Pos < _src.Length)
        {
            char c = _src[_s.Pos];
            switch (c)
            {
                case '\n':
                case '\r':
                case '\u2028':
                case '\u2029':
                    _s.Flags |= TokenFlags.PrecededByLineBreak;
                    _s.Pos++;
                    continue;
                case ' ':
                case '\t':
                    _s.Pos++;
                    continue;
                case '/':
                    if (Peek(1) == '/')
                    {
                        SkipToLineEnd(_s.Pos + 2);
                        continue;
                    }
                    if (Peek(1) == '*')
                    {
                        SkipBlockComment();
                        continue;
                    }
                    return;
                case '#':
                    // Hashbang, only as the first two characters of the file.
                    if (_s.Pos == 0 && Peek(1) == '!')
                    {
                        SkipToLineEnd(2);
                        continue;
                    }
                    return;
                default:
                    if (c > ' ' && c < '\u0080')
                        return;
                    if (CharClass.IsWhiteSpace(c))
                    {
                        _s.Pos++;
                        continue;
                    }
                    return;
            }
        }
    }

    private void SkipToLineEnd(int from)
    {
        int i = _src[from..].IndexOfAny(LineTerminators);
        _s.Pos = i < 0 ? _src.Length : from + i;
    }

    private void SkipBlockComment()
    {
        int bodyStart = _s.Pos + 2;
        int close = _src[bodyStart..].IndexOf("*/");
        int bodyEnd = close < 0 ? _src.Length : bodyStart + close;
        if (_src[bodyStart..bodyEnd].ContainsAny(LineTerminators))
            _s.Flags |= TokenFlags.PrecededByLineBreak;
        if (close < 0)
        {
            _s.Flags |= TokenFlags.Unterminated;
            _s.Pos = _src.Length;
        }
        else
        {
            _s.Pos = bodyEnd + 2;
        }
    }

    private void ScanString(char quote)
    {
        var stops = quote == '"' ? DoubleQuoteStops : SingleQuoteStops;
        int p = _s.Pos + 1;
        while (true)
        {
            int i = _src[p..].IndexOfAny(stops);
            if (i < 0)
            {
                p = _src.Length;
                _s.Flags |= TokenFlags.Unterminated;
                break;
            }
            p += i;
            char c = _src[p];
            if (c == quote)
            {
                p++;
                break;
            }
            if (c == '\\')
            {
                p = SkipEscape(p);
                continue;
            }
            // Unescaped CR or LF ends the line, and the string with it. LS and PS are allowed.
            _s.Flags |= TokenFlags.Unterminated;
            break;
        }
        _s.Pos = p;
        _s.Kind = TokenKind.StringLiteral;
    }

    /// <summary>Scans template characters after <c>`</c> or <c>}</c> up to <c>`</c> or <c>${</c>.</summary>
    private void ScanTemplate(TokenKind whenClosed, TokenKind whenSubstitution)
    {
        int p = _s.Pos;
        while (true)
        {
            int i = _src[p..].IndexOfAny(TemplateStops);
            if (i < 0)
            {
                p = _src.Length;
                _s.Flags |= TokenFlags.Unterminated;
                _s.Kind = whenClosed;
                break;
            }
            p += i;
            char c = _src[p];
            if (c == '`')
            {
                p++;
                _s.Kind = whenClosed;
                break;
            }
            if (c == '\\')
            {
                p = SkipEscape(p);
                continue;
            }
            // '$'
            if (p + 1 < _src.Length && _src[p + 1] == '{')
            {
                p += 2;
                _s.Kind = whenSubstitution;
                break;
            }
            p++;
        }
        _s.Pos = p;
    }

    /// <summary>Skips a backslash and the character it escapes; CRLF counts as one character.</summary>
    private readonly int SkipEscape(int backslash)
    {
        int p = backslash + 1;
        if (p >= _src.Length)
            return p;
        if (_src[p] == '\r' && p + 1 < _src.Length && _src[p + 1] == '\n')
            return p + 2;
        return p + CodePointWidth(p);
    }

    private void ScanNumber()
    {
        int p = _s.Pos;
        bool bigInt = false;
        char c0 = _src[p];
        char c1 = p + 1 < _src.Length ? _src[p + 1] : '\0';

        if (c0 == '0' && (char)(c1 | 0x20) is 'x' or 'o' or 'b')
        {
            p += 2;
            int digitsStart = p;
            while (p < _src.Length && (CharClass.IsHexDigit(_src[p]) || _src[p] == '_'))
                p++;
            if (p == digitsStart)
                _s.Flags |= TokenFlags.Invalid;
        }
        else
        {
            p = SkipDecimalDigits(p);
            if (p < _src.Length && _src[p] == '.')
                p = SkipDecimalDigits(p + 1);
            if (p < _src.Length && (_src[p] | 0x20) == 'e')
            {
                int q = p + 1;
                if (q < _src.Length && _src[q] is '+' or '-')
                    q++;
                if (q < _src.Length && CharClass.IsDigit(_src[q]))
                    p = SkipDecimalDigits(q);
            }
        }

        if (p < _src.Length && _src[p] == 'n')
        {
            p++;
            bigInt = true;
        }

        // `3in x` is an error in JS: an identifier may not touch a numeric literal.
        if (p < _src.Length)
        {
            int cp = CodePointAt(p, out _);
            if (CharClass.IsIdentifierStart(cp) || CharClass.IsDigit(_src[p]) || _src[p] == '\\')
            {
                _s.Flags |= TokenFlags.Invalid;
                while (p < _src.Length)
                {
                    cp = CodePointAt(p, out int width);
                    if (!CharClass.IsIdentifierPart(cp))
                        break;
                    p += width;
                }
            }
        }

        _s.Pos = p;
        _s.Kind = bigInt ? TokenKind.BigIntLiteral : TokenKind.NumericLiteral;
    }

    private readonly int SkipDecimalDigits(int p)
    {
        while (p < _src.Length && (CharClass.IsDigit(_src[p]) || _src[p] == '_'))
            p++;
        return p;
    }

    private readonly bool StartsIdentifier(int p)
    {
        if (p >= _src.Length)
            return false;
        if (_src[p] == '\\')
            return true;
        return CharClass.IsIdentifierStart(CodePointAt(p, out _));
    }

    /// <summary>Scans an identifier from <see cref="LexerState.Pos"/>, which must start one.</summary>
    private void ScanIdentifierRest()
    {
        int start = _s.Pos;
        int p = start;
        bool first = true;
        while (p < _src.Length)
        {
            int cp;
            int width;
            if (_src[p] == '\\')
            {
                _s.Flags |= TokenFlags.HasEscape;
                if (!TryReadUnicodeEscape(p, out cp, out width))
                {
                    _s.Flags |= TokenFlags.Invalid;
                    // Consume the backslash so the scan always advances.
                    p += width;
                    first = false;
                    continue;
                }
                if (!(first ? CharClass.IsIdentifierStart(cp) : CharClass.IsIdentifierPart(cp)))
                    _s.Flags |= TokenFlags.Invalid;
            }
            else
            {
                cp = CodePointAt(p, out width);
                if (!(first ? CharClass.IsIdentifierStart(cp) : CharClass.IsIdentifierPart(cp)))
                    break;
            }
            p += width;
            first = false;
        }
        _s.Pos = p;
        if ((_s.Flags & TokenFlags.HasEscape) == 0)
            _s.Keyword = Keywords.Lookup(_src[start..p]);
    }

    /// <summary>Reads <c>\uXXXX</c> or <c>\u{X...}</c>. On failure, width covers the backslash only.</summary>
    private readonly bool TryReadUnicodeEscape(int backslash, out int cp, out int width)
    {
        cp = -1;
        width = 1;
        int p = backslash + 1;
        if (p >= _src.Length || _src[p] != 'u')
            return false;
        p++;
        int value = 0;
        if (p < _src.Length && _src[p] == '{')
        {
            p++;
            int digits = 0;
            while (p < _src.Length && CharClass.IsHexDigit(_src[p]))
            {
                value = value * 16 + HexValue(_src[p]);
                if (value > 0x10FFFF)
                    return false;
                p++;
                digits++;
            }
            if (digits == 0 || p >= _src.Length || _src[p] != '}')
                return false;
            p++;
        }
        else
        {
            for (int i = 0; i < 4; i++, p++)
            {
                if (p >= _src.Length || !CharClass.IsHexDigit(_src[p]))
                    return false;
                value = value * 16 + HexValue(_src[p]);
            }
        }
        cp = value;
        width = p - backslash;
        return true;
    }

    private static int HexValue(char c) => c <= '9' ? c - '0' : (c | 0x20) - 'a' + 10;

    private readonly int CodePointAt(int p, out int width)
    {
        char c = _src[p];
        if (char.IsHighSurrogate(c) && p + 1 < _src.Length && char.IsLowSurrogate(_src[p + 1]))
        {
            width = 2;
            return char.ConvertToUtf32(c, _src[p + 1]);
        }
        width = 1;
        return c;
    }

    private readonly int CodePointWidth(int p) =>
        char.IsHighSurrogate(_src[p]) && p + 1 < _src.Length && char.IsLowSurrogate(_src[p + 1]) ? 2 : 1;

    private readonly char Peek(int offset)
    {
        int p = _s.Pos + offset;
        return p < _src.Length ? _src[p] : '\0';
    }

    private readonly char At(int p) => p < _src.Length ? _src[p] : '\0';

    private void Punct(TokenKind kind, int length)
    {
        _s.Kind = kind;
        _s.Pos += length;
    }
}
