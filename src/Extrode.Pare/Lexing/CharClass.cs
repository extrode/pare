using System.Globalization;
using System.Runtime.CompilerServices;

namespace Extrode.Pare.Lexing;

/// <summary>ECMAScript character classes. ASCII is a table lookup; the rest asks Unicode categories.</summary>
internal static class CharClass
{
    private const byte IdStart = 1;
    private const byte IdPart = 2;
    private const byte Digit = 4;
    private const byte HexDigit = 8;

    private static ReadOnlySpan<byte> Ascii =>
    [
        0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, // 0x00
        0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, // 0x10
        0, 0, 0, 0, 3, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, // 0x20  $
        14, 14, 14, 14, 14, 14, 14, 14, 14, 14, 0, 0, 0, 0, 0, 0, // 0x30  0-9
        0, 11, 11, 11, 11, 11, 11, 3, 3, 3, 3, 3, 3, 3, 3, 3, // 0x40  A-O
        3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 0, 0, 0, 0, 3, // 0x50  P-Z _
        0, 11, 11, 11, 11, 11, 11, 3, 3, 3, 3, 3, 3, 3, 3, 3, // 0x60  a-o
        3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 3, 0, 0, 0, 0, 0, // 0x70  p-z
    ];

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool IsDigit(char c) => (uint)(c - '0') <= 9;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool IsHexDigit(char c) => c < 0x80 && (Ascii[c] & HexDigit) != 0;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool IsLineTerminator(char c) => c is '\n' or '\r' or '\u2028' or '\u2029';

    /// <summary>WhiteSpace from the spec, excluding line terminators.</summary>
    public static bool IsWhiteSpace(char c) => c switch
    {
        ' ' or '\t' or '\v' or '\f' or '\u00A0' or '\uFEFF' => true,
        < '\u0080' => false,
        _ => CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.SpaceSeparator,
    };

    public static bool IsIdentifierStart(int cp)
    {
        if (cp < 0x80)
            return cp >= 0 && (Ascii[cp] & IdStart) != 0;
        return CharUnicodeInfo.GetUnicodeCategory(cp) switch
        {
            UnicodeCategory.UppercaseLetter or UnicodeCategory.LowercaseLetter or
            UnicodeCategory.TitlecaseLetter or UnicodeCategory.ModifierLetter or
            UnicodeCategory.OtherLetter or UnicodeCategory.LetterNumber => true,
            _ => IsOtherIdStart(cp),
        };
    }

    public static bool IsIdentifierPart(int cp)
    {
        if (cp < 0x80)
            return cp >= 0 && (Ascii[cp] & IdPart) != 0;
        return CharUnicodeInfo.GetUnicodeCategory(cp) switch
        {
            UnicodeCategory.UppercaseLetter or UnicodeCategory.LowercaseLetter or
            UnicodeCategory.TitlecaseLetter or UnicodeCategory.ModifierLetter or
            UnicodeCategory.OtherLetter or UnicodeCategory.LetterNumber or
            UnicodeCategory.NonSpacingMark or UnicodeCategory.SpacingCombiningMark or
            UnicodeCategory.DecimalDigitNumber or UnicodeCategory.ConnectorPunctuation => true,
            _ => cp is 0x200C or 0x200D || IsOtherIdStart(cp) || IsOtherIdContinue(cp),
        };
    }

    // Unicode Other_ID_Start and Other_ID_Continue: kept for backward compatibility by UAX #31.
    private static bool IsOtherIdStart(int cp) => cp is 0x1885 or 0x1886 or 0x2118 or 0x212E or 0x309B or 0x309C;

    private static bool IsOtherIdContinue(int cp) => cp is 0x00B7 or 0x0387 or (>= 0x1369 and <= 0x1371) or 0x19DA;
}
