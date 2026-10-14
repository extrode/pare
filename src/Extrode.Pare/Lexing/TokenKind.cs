namespace Extrode.Pare.Lexing;

/// <summary>
/// Token kinds. Keywords are <see cref="Identifier"/> tokens classified by <see cref="Keyword"/>,
/// because nearly every TS keyword is also a valid identifier somewhere.
/// </summary>
internal enum TokenKind : byte
{
    EndOfFile,
    /// <summary>A character that cannot start any token. Always has <see cref="TokenFlags.Invalid"/>.</summary>
    Unknown,
    Identifier,
    PrivateName,
    NumericLiteral,
    BigIntLiteral,
    StringLiteral,
    /// <summary>Only produced by <see cref="Lexer.ReScanSlashAsRegex"/>.</summary>
    RegularExpression,
    NoSubstitutionTemplate,
    TemplateHead,
    /// <summary>Only produced by <see cref="Lexer.ReScanTemplateContinuation"/>.</summary>
    TemplateMiddle,
    /// <summary>Only produced by <see cref="Lexer.ReScanTemplateContinuation"/>.</summary>
    TemplateTail,

    OpenBrace,
    CloseBrace,
    OpenParen,
    CloseParen,
    OpenBracket,
    CloseBracket,
    Dot,
    DotDotDot,
    Semicolon,
    Comma,
    LessThan,
    LessThanEquals,
    LessThanLessThan,
    LessThanLessThanEquals,
    /// <summary><c>&gt;</c> is always scanned alone; see <see cref="Lexer.ReScanGreaterThan"/>.</summary>
    GreaterThan,
    GreaterThanEquals,
    GreaterThanGreaterThan,
    GreaterThanGreaterThanEquals,
    GreaterThanGreaterThanGreaterThan,
    GreaterThanGreaterThanGreaterThanEquals,
    EqualsEquals,
    EqualsEqualsEquals,
    ExclamationEquals,
    ExclamationEqualsEquals,
    EqualsGreaterThan,
    Plus,
    Minus,
    Asterisk,
    AsteriskAsterisk,
    Slash,
    Percent,
    PlusPlus,
    MinusMinus,
    Ampersand,
    Bar,
    Caret,
    Exclamation,
    Tilde,
    AmpersandAmpersand,
    BarBar,
    QuestionQuestion,
    Question,
    QuestionDot,
    Colon,
    At,
    Equals,
    PlusEquals,
    MinusEquals,
    AsteriskEquals,
    AsteriskAsteriskEquals,
    SlashEquals,
    PercentEquals,
    AmpersandEquals,
    BarEquals,
    CaretEquals,
    AmpersandAmpersandEquals,
    BarBarEquals,
    QuestionQuestionEquals,
}

[Flags]
internal enum TokenFlags : byte
{
    None = 0,
    /// <summary>A line terminator sits between the previous token and this one (ASI, postfix <c>!</c>).</summary>
    PrecededByLineBreak = 1,
    /// <summary>An identifier spelled with a <c>\u</c> escape. It never counts as a keyword.</summary>
    HasEscape = 2,
    /// <summary>A string, template, regex or block comment ran into a line end or end of file.</summary>
    Unterminated = 4,
    /// <summary>Malformed: bad escape, identifier right after a number, unknown character.</summary>
    Invalid = 8,
}
