using Extrode.Pare.Lexing;

namespace Extrode.Pare.Parsing;

internal ref partial struct Parser
{
    /// <summary>
    /// <c>function* name&lt;T&gt;(params): R { body }</c> from the <c>function</c> keyword. Returns
    /// false for a signature without a body (an overload), which the caller erases. TypeScript:
    /// <c>parseFunctionDeclaration</c>, <c>parseFunctionExpression</c>.
    /// </summary>
    private bool ParseFunction(bool declaration, bool isAsync, int start, int prevEnd)
    {
        Next();
        bool isGenerator = Optional(TokenKind.Asterisk);
        if (IsIdentifier())
            Next();
        bool hasBody = ParseFunctionRest(isAsync, isGenerator, bodyRequired: !declaration, isConstructor: false);
        return hasBody;
    }

    /// <summary>
    /// Type parameters, parameters, return type and body (or <c>;</c> when the body is optional).
    /// Returns whether a body was present. TypeScript: <c>fillSignature</c> and
    /// <c>parseFunctionBlockOrSemicolon</c>.
    /// </summary>
    private bool ParseFunctionRest(bool isAsync, bool isGenerator, bool bodyRequired, bool isConstructor)
    {
        if (T is TokenKind.LessThan or TokenKind.LessThanLessThan)
        {
            int start = Start;
            ParseTypeParameters();
            Erase(start, PrevEnd);
        }

        bool savedAwait = _await, savedYield = _yield;
        _await = isAsync;
        _yield = isGenerator;
        ParseParameters(isConstructor);
        if (T == TokenKind.Colon)
        {
            int colon = Start;
            Next();
            ParseReturnType();
            Erase(colon, PrevEnd);
        }

        bool hasBody = false;
        if (T == TokenKind.OpenBrace)
        {
            ParseFunctionBody();
            hasBody = true;
        }
        else if (bodyRequired)
        {
            Fail();
        }
        else
        {
            ParseSemicolon();
        }
        _await = savedAwait;
        _yield = savedYield;
        return hasBody;
    }

    private void ParseFunctionBody()
    {
        bool savedIn = _disallowIn, savedDecorator = _decorator;
        _disallowIn = false;
        _decorator = false;
        Expect(TokenKind.OpenBrace);
        while (_ok && T != TokenKind.CloseBrace)
            ParseStatement();
        Expect(TokenKind.CloseBrace);
        _disallowIn = savedIn;
        _decorator = savedDecorator;
    }

    /// <summary>TypeScript: <c>parseParameters</c>. A leading <c>this</c> parameter is erased with its comma.</summary>
    private void ParseParameters(bool isConstructor)
    {
        Expect(TokenKind.OpenParen);
        bool savedIn = _disallowIn;
        _disallowIn = false;
        bool first = true;
        while (_ok && T != TokenKind.CloseParen)
        {
            if (first && K == Keyword.This)
            {
                int start = Start;
                Next();
                if (T == TokenKind.Colon)
                {
                    Next();
                    ParseType();
                }
                if (T == TokenKind.Comma)
                    Next();
                else if (T != TokenKind.CloseParen)
                    Fail();
                Erase(start, PrevEnd);
                first = false;
                continue;
            }
            first = false;
            ParseParameter(isConstructor);
            if (T != TokenKind.CloseParen)
                Expect(TokenKind.Comma);
        }
        _disallowIn = savedIn;
        Expect(TokenKind.CloseParen);
    }

    /// <summary>TypeScript: <c>parseParameterWorker</c>.</summary>
    private void ParseParameter(bool isConstructor)
    {
        if (T == TokenKind.At)
        {
            int start = Start;
            ParseDecorators();
            Reject(DiagnosticCode.ParameterDecorator, start, PrevEnd);
        }
        while (_ok && IsParameterModifier(K) && NextCanFollowModifier(sameLine: true))
        {
            Reject(DiagnosticCode.ParameterProperty, Start, End);
            Next();
        }
        Optional(TokenKind.DotDotDot);
        ParseBindingTarget();
        if (T == TokenKind.Question)
        {
            Erase(Start, End);
            Next();
        }
        if (T == TokenKind.Colon)
        {
            int colon = Start;
            Next();
            ParseType();
            Erase(colon, PrevEnd);
        }
        if (T == TokenKind.Equals)
        {
            Next();
            ParseAssignmentAllowIn();
        }
    }

    private static bool IsParameterModifier(Keyword k) =>
        k is Keyword.Public or Keyword.Private or Keyword.Protected or Keyword.Readonly or Keyword.Override;

    /// <summary>
    /// The token after the current one can follow a modifier. TypeScript:
    /// <c>nextTokenCanFollowModifier</c> and <c>canFollowModifier</c>.
    /// </summary>
    private readonly bool NextCanFollowModifier(bool sameLine)
    {
        var next = Peek();
        if (sameLine && IsLineBreak(next))
            return false;
        return next.Kind is TokenKind.OpenBracket or TokenKind.OpenBrace or TokenKind.Asterisk or
            TokenKind.DotDotDot or TokenKind.Identifier or TokenKind.StringLiteral or
            TokenKind.NumericLiteral or TokenKind.BigIntLiteral or TokenKind.PrivateName;
    }

    /// <summary>An identifier, or an array or object destructuring pattern.</summary>
    private void ParseBindingTarget()
    {
        Guard();
        switch (T)
        {
            case TokenKind.OpenBracket:
                Next();
                while (_ok && T != TokenKind.CloseBracket)
                {
                    if (T == TokenKind.Comma)
                    {
                        Next();
                        continue;
                    }
                    Optional(TokenKind.DotDotDot);
                    ParseBindingElement();
                    if (T != TokenKind.CloseBracket)
                        Expect(TokenKind.Comma);
                }
                Expect(TokenKind.CloseBracket);
                return;
            case TokenKind.OpenBrace:
                Next();
                while (_ok && T != TokenKind.CloseBrace)
                {
                    if (T == TokenKind.DotDotDot)
                    {
                        Next();
                        ParsePropertyName();
                        if (T == TokenKind.Colon)
                        {
                            // `{ ...a: b }`: a TypeScript checker error, still parsed.
                            Next();
                            ParseBindingTarget();
                        }
                    }
                    else
                    {
                        bool shorthand = IsIdentifier();
                        ParsePropertyName();
                        if (T == TokenKind.Colon)
                        {
                            Next();
                            ParseBindingElement();
                        }
                        else if (!shorthand)
                        {
                            Fail();
                        }
                        else if (T == TokenKind.Equals)
                        {
                            Next();
                            ParseAssignmentAllowIn();
                        }
                    }
                    if (T != TokenKind.CloseBrace)
                        Expect(TokenKind.Comma);
                }
                Expect(TokenKind.CloseBrace);
                return;
            case TokenKind.PrivateName:
                // `const #x = 1`: a TypeScript checker error, still parsed.
                Next();
                return;
            default:
                ExpectIdentifier();
                return;
        }
    }

    private void ParseBindingElement()
    {
        ParseBindingTarget();
        if (T == TokenKind.Equals)
        {
            Next();
            ParseAssignmentAllowIn();
        }
    }

    /// <summary><c>@a.b(c)</c> or <c>@(expr)</c>, repeated. TypeScript: <c>parseDecoratorExpression</c>.</summary>
    private void ParseDecorators()
    {
        bool saved = _decorator;
        _decorator = true;
        while (_ok && T == TokenKind.At)
        {
            Next();
            ParsePrimary();
            ParseMemberRest(allowCall: true);
        }
        _decorator = saved;
    }

    /// <summary>
    /// From the <c>class</c> keyword: name, type parameters, heritage, body. TypeScript:
    /// <c>parseClassDeclarationOrExpression</c>.
    /// </summary>
    private void ParseClass(bool declaration, int start, int prevEnd)
    {
        Next();
        if (IsIdentifier() && K is not (Keyword.Implements or Keyword.Extends))
            Next();
        if (T is TokenKind.LessThan or TokenKind.LessThanLessThan)
        {
            int tp = Start;
            ParseTypeParameters();
            Erase(tp, PrevEnd);
        }
        if (K == Keyword.Extends)
        {
            Next();
            ParseHeritage(erase: true);
        }
        if (K == Keyword.Implements)
        {
            int impl = Start;
            Next();
            ParseHeritageList();
            Erase(impl, PrevEnd);
        }

        bool savedDecorator = _decorator, savedIn = _disallowIn;
        _decorator = false;
        _disallowIn = false;
        Expect(TokenKind.OpenBrace);
        while (_ok && T != TokenKind.CloseBrace)
            ParseClassMember();
        Expect(TokenKind.CloseBrace);
        _decorator = savedDecorator;
        _disallowIn = savedIn;
    }

    /// <summary>
    /// <c>Base&lt;T&gt;</c> in a heritage clause: a left-hand-side expression plus type arguments.
    /// TypeScript: <c>parseExpressionWithTypeArguments</c>.
    /// </summary>
    private void ParseHeritage(bool erase)
    {
        ParseLeftHandSide();
        if (T is TokenKind.LessThan or TokenKind.LessThanLessThan)
        {
            int start = Start;
            ParseTypeArguments();
            if (erase)
                Erase(start, PrevEnd);
        }
    }

    /// <summary><c>A, B&lt;T&gt;</c> after <c>implements</c> or an interface's <c>extends</c>; erased by the caller.</summary>
    private void ParseHeritageList()
    {
        ParseHeritage(erase: false);
        while (_ok && T == TokenKind.Comma)
        {
            Next();
            ParseHeritage(erase: false);
        }
    }

    private static bool IsClassModifier(Keyword k) => k is Keyword.Public or Keyword.Private or
        Keyword.Protected or Keyword.Readonly or Keyword.Override or Keyword.Abstract or
        Keyword.Declare or Keyword.Static or Keyword.Accessor or Keyword.Async;

    private static bool IsTypeScriptModifier(Keyword k) => k is Keyword.Public or Keyword.Private or
        Keyword.Protected or Keyword.Readonly or Keyword.Override;

    /// <summary>TypeScript: <c>parseClassElement</c>.</summary>
    private void ParseClassMember()
    {
        Guard();
        int start = Start;
        int prevEnd = PrevEnd;
        if (T == TokenKind.Semicolon)
        {
            Next();
            return;
        }
        var snapshot = Save();
        bool hasDecorators = false;
        if (T == TokenKind.At)
        {
            ParseDecorators();
            hasDecorators = true;
        }

        bool erasedWhole = false, isAsync = false, isGenerator = false;
        int modifierEdits = _edits.Count;
        while (_ok && T == TokenKind.Identifier && IsClassModifier(K))
        {
            var k = K;
            if (k == Keyword.Static)
            {
                var next = Peek();
                if (next.Kind == TokenKind.OpenBrace)
                {
                    // static { ... }
                    Next();
                    bool savedAwait = _await, savedYield = _yield;
                    _await = false;
                    _yield = false;
                    ParseFunctionBody();
                    _await = savedAwait;
                    _yield = savedYield;
                    return;
                }
                if (!NextCanFollowModifier(sameLine: false))
                    break;
            }
            else if (!NextCanFollowModifier(sameLine: true))
            {
                break;
            }
            if (k is Keyword.Declare or Keyword.Abstract)
                erasedWhole = true;
            else if (IsTypeScriptModifier(k))
                Erase(Start, End);
            else if (k == Keyword.Async)
                isAsync = true;
            Next();
        }

        bool hasBody = true;
        if (K is Keyword.Get or Keyword.Set && IsPropertyNameStart(Peek().Kind))
        {
            Next();
            ParsePropertyName();
            hasBody = ParseFunctionRest(isAsync: false, isGenerator: false, bodyRequired: false, isConstructor: false);
        }
        else if (T == TokenKind.OpenBracket && IsIndexSignature())
        {
            ParseIndexSignature();
            ParseTypeMemberSeparator();
            erasedWhole = true;
        }
        else
        {
            if (T == TokenKind.Asterisk)
            {
                Next();
                isGenerator = true;
            }
            bool isConstructor = K == Keyword.Constructor ||
                (T == TokenKind.StringLiteral && _src[(Start + 1)..(End - 1)] is "constructor");
            ParsePropertyName();
            if (T is TokenKind.Question or TokenKind.Exclamation)
            {
                Erase(Start, End);
                Next();
            }
            if (T is TokenKind.OpenParen or TokenKind.LessThan or TokenKind.LessThanLessThan)
            {
                hasBody = ParseFunctionRest(isAsync, isGenerator, bodyRequired: false, isConstructor);
            }
            else
            {
                if (T == TokenKind.Colon)
                {
                    int colon = Start;
                    Next();
                    ParseType();
                    Erase(colon, PrevEnd);
                }
                if (T == TokenKind.Equals)
                {
                    Next();
                    bool savedAwait = _await, savedYield = _yield;
                    _await = false;
                    _yield = false;
                    ParseAssignmentAllowIn();
                    _await = savedAwait;
                    _yield = savedYield;
                }
                ParseSemicolon();
            }
        }

        if (!_ok)
            return;
        if (erasedWhole || !hasBody)
        {
            // declare/abstract members, index signatures, overloads: erase the whole member.
            DropSince(snapshot);
            EraseStatement(start, PrevEnd, prevEnd);
        }
        else if (!hasDecorators && _edits.Count > modifierEdits && _edits.Items[modifierEdits].Start == start)
        {
            // `x = 1\n private [k] = 2` would merge into `x = 1[k] = 2` once `private` is gone.
            var e = _edits.Items[modifierEdits];
            bool safe = prevEnd == 0 || _src[prevEnd - 1] is ';' or '{';
            if (!safe)
                _edits.Mutable[modifierEdits] = e with { First = ';' };
        }
    }

    /// <summary>TypeScript: <c>isUnambiguouslyIndexSignature</c>, from <c>[</c>.</summary>
    private readonly bool IsIndexSignature()
    {
        var copy = _lex;
        copy.Next();
        if (copy.Kind is TokenKind.DotDotDot or TokenKind.CloseBracket)
            return true;
        if (copy.Kind != TokenKind.Identifier)
            return false;
        if (IsClassModifier(copy.Keyword) || copy.Keyword == Keyword.Readonly)
        {
            copy.Next();
            if (copy.Kind == TokenKind.Identifier)
                return true;
        }
        else if (Keywords.IsReserved(copy.Keyword))
        {
            return false;
        }
        else
        {
            copy.Next();
        }
        if (copy.Kind is TokenKind.Colon or TokenKind.Comma)
            return true;
        if (copy.Kind != TokenKind.Question)
            return false;
        copy.Next();
        return copy.Kind is TokenKind.Colon or TokenKind.Comma or TokenKind.CloseBracket;
    }
}
