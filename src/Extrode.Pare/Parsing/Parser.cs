using System.Runtime.CompilerServices;
using Extrode.Pare.Lexing;

namespace Extrode.Pare.Parsing;

/// <summary>
/// Recognizer for TypeScript modules. It builds no tree: it walks the grammar of TypeScript's
/// <c>parser.ts</c> and records an <see cref="Edit"/> for every piece of type syntax, plus a
/// diagnostic for every construct that cannot be erased.
/// </summary>
/// <remarks>
/// Failure model: a syntax error sets <see cref="_ok"/> to false and moves the lexer to end of
/// file, so every loop (they all stop at EndOfFile) unwinds without exceptions. A speculative parse
/// takes a <see cref="Snapshot"/> and rolls back lexer, edits and diagnostics when it fails.
/// </remarks>
internal ref partial struct Parser
{
    private readonly ReadOnlySpan<char> _src;
    private Lexer _lex;
    private PooledList<Edit> _edits;
    private PooledList<Diagnostic> _diags;
    private bool _ok;
    // Set by Guard and kept by Restore: a speculative parse that ran out of stack must not be undone and retried deeper.
    private bool _tooDeep;
    private int _errorStart;
    private int _errorLength;

    // Grammar context: [Await], [Yield], [In] from the spec, and declare-d (ambient) code.
    private bool _await;
    private bool _yield;
    private bool _disallowIn;
    private bool _ambient;
    // Whether the namespace parsed last holds values; read for a `declare namespace` nested in
    // a non-ambient one.
    private bool _namespaceHasValues;
    // Inside `@...`: `[` starts the decorated member's computed name, not an element access.
    private bool _decorator;
    // TypeScript's DisallowConditionalTypes context: in `A extends B ? ...` the B part, and in
    // `infer X extends C`.
    private bool _noConditional;
    // Positions where a parenthesized arrow function was tried and failed, one bit per char.
    // TypeScript: notParenthesizedArrow. Keeps nested `(a = (b = (c)))` linear.
    private ulong[]? _notArrow;
    // Rewrite relative `./x.ts` module specifiers to `./x.js`.
    private readonly bool _rewriteImports;
    private int _guardTick;

    public Parser(ReadOnlySpan<char> source, bool rewriteImports = false)
    {
        _src = source;
        _rewriteImports = rewriteImports;
        _lex = new Lexer(source);
        _ok = true;
        // Modules: top-level await.
        _await = true;
    }

    public readonly bool Ok => _ok;
    public readonly int ErrorStart => _errorStart;
    public readonly int ErrorLength => _errorLength;
    public readonly ReadOnlySpan<Edit> Edits => _edits.Items;
    public readonly Span<Edit> MutableEdits => _edits.Mutable;
    public readonly ReadOnlySpan<Diagnostic> Diagnostics => _diags.Items;

    public void Dispose()
    {
        _edits.Dispose();
        _diags.Dispose();
        if (_notArrow is not null)
            System.Buffers.ArrayPool<ulong>.Shared.Return(_notArrow);
        _notArrow = null;
    }

    private readonly bool IsNotArrow(int pos) =>
        _notArrow is not null && (_notArrow[pos >> 6] & (1UL << (pos & 63))) != 0;

    private void MarkNotArrow(int pos)
    {
        if (_notArrow is null)
        {
            _notArrow = System.Buffers.ArrayPool<ulong>.Shared.Rent((_src.Length >> 6) + 1);
            Array.Clear(_notArrow);
        }
        _notArrow[pos >> 6] |= 1UL << (pos & 63);
    }

    /// <summary>Parses the whole source as a module.</summary>
    public void ParseModule()
    {
        Next();
        while (_ok && T != TokenKind.EndOfFile)
            ParseStatement();
    }

    // ---- Tokens -------------------------------------------------------------------------------

    private readonly TokenKind T => _lex.Kind;

    /// <summary>Keyword of the current token, or None for non-identifiers and escaped identifiers.</summary>
    private readonly Keyword K => _lex.Kind == TokenKind.Identifier ? _lex.Keyword : Keyword.None;

    private readonly int Start => _lex.Start;

    private readonly int End => _lex.End;

    /// <summary>End of the previous token.</summary>
    private readonly int PrevEnd => _lex.FullStart;

    private readonly bool LineBreakBefore => _lex.HasPrecedingLineBreak;

    private void Next()
    {
        _lex.Next();
        if ((_lex.Flags & (TokenFlags.Invalid | TokenFlags.Unterminated)) != 0 || _lex.Kind == TokenKind.Unknown)
            Fail();
    }

    private readonly bool Is(Keyword k) => K == k;

    /// <summary>An identifier that is not a reserved word.</summary>
    private readonly bool IsIdentifier()
    {
        if (_lex.Kind != TokenKind.Identifier)
            return false;
        // await and yield are identifiers outside their contexts; the unary parser checks
        // for the expressions first.
        return !Keywords.IsReserved(_lex.Keyword);
    }

    /// <summary>Any identifier including reserved words: a property name after <c>.</c>.</summary>
    private readonly bool IsIdentifierName() => _lex.Kind == TokenKind.Identifier;

    private void Expect(TokenKind kind)
    {
        if (T == kind)
            Next();
        else
            Fail();
    }

    private bool Optional(TokenKind kind)
    {
        if (T != kind)
            return false;
        Next();
        return true;
    }

    private void ExpectKeyword(Keyword k)
    {
        if (K == k)
            Next();
        else
            Fail();
    }

    private void ExpectIdentifier()
    {
        if (IsIdentifier())
            Next();
        else
            Fail();
    }

    private void ExpectIdentifierName()
    {
        if (IsIdentifierName())
            Next();
        else
            Fail();
    }

    /// <summary>A <c>;</c>, or a place where ASI inserts one. TypeScript: <c>parseSemicolon</c>.</summary>
    private void ParseSemicolon()
    {
        if (T == TokenKind.Semicolon)
            Next();
        else if (!CanInsertSemicolon())
            Fail();
    }

    private readonly bool CanInsertSemicolon() =>
        T is TokenKind.Semicolon or TokenKind.CloseBrace or TokenKind.EndOfFile || LineBreakBefore;

    /// <summary>The next token, without moving.</summary>
    private readonly LexerState Peek()
    {
        var copy = _lex;
        copy.Next();
        return copy.Save();
    }

    /// <summary>The token after the next one, without moving.</summary>
    private readonly LexerState Peek2()
    {
        var copy = _lex;
        copy.Next();
        copy.Next();
        return copy.Save();
    }

    private static bool IsIdentifierToken(in LexerState s) =>
        s.Kind == TokenKind.Identifier && !Keywords.IsReserved(s.Keyword);

    private static bool IsLineBreak(in LexerState s) => (s.Flags & TokenFlags.PrecededByLineBreak) != 0;

    // ---- Failure and speculation --------------------------------------------------------------

    private void Fail()
    {
        if (_ok)
        {
            _ok = false;
            _errorStart = _lex.Start;
            _errorLength = Math.Max(0, _lex.End - _lex.Start);
            _lex.Restore(new LexerState
            {
                Pos = _src.Length,
                FullStart = _src.Length,
                Start = _src.Length,
                Kind = TokenKind.EndOfFile,
            });
        }
    }

    /// <summary>
    /// Fails on input nested deep enough to exhaust the stack. Checks every 16th call: the check
    /// reads a thread static, and 16 nesting levels use far less than the 64 KB or more it leaves free.
    /// </summary>
    private void Guard()
    {
        if ((++_guardTick & 15) == 0 && !RuntimeHelpers.TryEnsureSufficientExecutionStack())
        {
            _tooDeep = true;
            Fail();
        }
    }

    private struct Snapshot
    {
        public LexerState Lex;
        public int Edits;
        public int Diags;
        public bool Ok, Await, Yield, DisallowIn, Ambient;
    }

    private readonly Snapshot Save() => new()
    {
        Lex = _lex.Save(),
        Edits = _edits.Count,
        Diags = _diags.Count,
        Ok = _ok,
        Await = _await,
        Yield = _yield,
        DisallowIn = _disallowIn,
        Ambient = _ambient,
    };

    /// <summary>Rolls back to <paramref name="s"/>, including a failure that happened since.</summary>
    private void Restore(in Snapshot s)
    {
        _lex.Restore(s.Lex);
        _edits.Truncate(s.Edits);
        _diags.Truncate(s.Diags);
        _await = s.Await;
        _yield = s.Yield;
        _disallowIn = s.DisallowIn;
        _ambient = s.Ambient;
        _ok = s.Ok && !_tooDeep;
    }

    // ---- Recording ----------------------------------------------------------------------------

    private void Erase(int start, int end)
    {
        if (end > start)
            _edits.Add(new Edit(start, end));
    }

    /// <summary>
    /// At a string literal used as a module specifier: records the <c>.ts</c>, <c>.mts</c> or
    /// <c>.cts</c> to <c>.js</c>, <c>.mjs</c> or <c>.cjs</c> rewrite when it is relative and not a
    /// declaration file. Same length, so positions hold. TypeScript: rewriteRelativeImportExtensions.
    /// </summary>
    private void RewriteSpecifier(int start, int end)
    {
        if (!_rewriteImports || end - start < 2)
            return;
        var text = _src[(start + 1)..(end - 1)];
        if (!(text.StartsWith("./") || text.StartsWith("../")) || text.Contains('\\'))
            return;
        if (!(text.EndsWith(".ts") || text.EndsWith(".mts") || text.EndsWith(".cts")))
            return;
        if (text.EndsWith(".d.ts") || text.EndsWith(".d.mts") || text.EndsWith(".d.cts"))
            return;
        // `t` of `ts` sits before the closing quote and `s`.
        _edits.Add(new Edit(end - 3, end - 2, Replace: 'j'));
    }

    /// <summary>
    /// Erases a whole statement or class member that starts at the current token's position
    /// <paramref name="start"/>, whose previous token ended at <paramref name="prevEnd"/>. Writes
    /// <c>;</c> first unless the previous token was <c>;</c> or <c>{</c>, so the lines around it
    /// cannot merge (handoff section 7.5).
    /// </summary>
    private void EraseStatement(int start, int end, int prevEnd)
    {
        if (end <= start)
            return;
        bool safe = prevEnd == 0 || _src[prevEnd - 1] is ';' or '{';
        _edits.Add(new Edit(start, end, safe ? '\0' : ';'));
    }

    /// <summary>Drops edits and diagnostics recorded since a snapshot: the range is erased whole.</summary>
    private void DropSince(in Snapshot s)
    {
        _edits.Truncate(s.Edits);
        _diags.Truncate(s.Diags);
    }

    /// <summary>Inside <c>declare</c> the diagnostic is recorded too; <see cref="ParseDeclare"/> drops it.</summary>
    private void Reject(DiagnosticCode code, int start, int end) =>
        _diags.Add(new Diagnostic(code, start, Math.Max(0, end - start)));

    private readonly bool HasLineBreak(int start, int end)
    {
        if (end > _src.Length)
            end = _src.Length;
        for (int i = start; i < end; i++)
        {
            if (CharClass.IsLineTerminator(_src[i]))
                return true;
        }
        return false;
    }
}
