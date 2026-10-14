using Extrode.Pare.Lexing;

namespace Extrode.Pare.Parsing;

/// <summary>
/// Type grammar. Types are always erased whole by the caller, so nothing here records edits of
/// its own (parameter lists inside function types may, harmlessly, inside the erased range).
/// </summary>
internal ref partial struct Parser
{
    /// <summary>TypeScript: <c>parseType</c>.</summary>
    private void ParseType()
    {
        Guard();
        if (!_ok)
            return;
        if (IsStartOfFunctionType())
        {
            ParseFunctionType();
            return;
        }
        ParseUnionType();
        if (!_noConditional && !LineBreakBefore && K == Keyword.Extends)
        {
            Next();
            _noConditional = true;
            ParseType();
            _noConditional = false;
            Expect(TokenKind.Question);
            ParseType();
            Expect(TokenKind.Colon);
            ParseType();
        }
    }

    /// <summary>A type in brackets, parens or a list: conditional types are allowed again.</summary>
    private void ParseNestedType()
    {
        bool saved = _noConditional;
        _noConditional = false;
        ParseType();
        _noConditional = saved;
    }

    /// <summary>TypeScript: <c>isStartOfFunctionTypeOrConstructorType</c>.</summary>
    private bool IsStartOfFunctionType()
    {
        switch (T)
        {
            case TokenKind.LessThan:
                return true;
            case TokenKind.OpenParen:
                return IsUnambiguouslyStartOfFunctionType();
            case TokenKind.Identifier:
                if (K == Keyword.New)
                    return true;
                if (K == Keyword.Abstract)
                {
                    var next = Peek();
                    return next.Kind == TokenKind.Identifier && next.Keyword == Keyword.New;
                }
                return false;
            default:
                return false;
        }
    }

    /// <summary>TypeScript: <c>isUnambiguouslyStartOfFunctionType</c>, from <c>(</c>.</summary>
    private bool IsUnambiguouslyStartOfFunctionType()
    {
        var saved = Save();
        bool result = false;
        Next();
        if (T is TokenKind.CloseParen or TokenKind.DotDotDot)
        {
            result = true;
        }
        else
        {
            // skipParameterStart: modifiers, then an identifier, `this` or a binding pattern.
            while (_ok && IsParameterModifier(K) && NextCanFollowModifier(sameLine: true))
                Next();
            bool skipped = false;
            if (IsIdentifier() || K == Keyword.This)
            {
                Next();
                skipped = true;
            }
            else if (T is TokenKind.OpenBracket or TokenKind.OpenBrace)
            {
                ParseBindingTarget();
                skipped = _ok;
            }
            if (skipped && _ok)
            {
                if (T is TokenKind.Colon or TokenKind.Comma or TokenKind.Question or TokenKind.Equals)
                {
                    result = true;
                }
                else if (T == TokenKind.CloseParen)
                {
                    Next();
                    result = _ok && T == TokenKind.EqualsGreaterThan;
                }
            }
        }
        Restore(saved);
        return result;
    }

    /// <summary><c>&lt;T&gt;(a: A) =&gt; R</c>, <c>new (...) =&gt; R</c>, <c>abstract new ...</c>.</summary>
    private void ParseFunctionType()
    {
        if (K == Keyword.Abstract)
            Next();
        if (K == Keyword.New)
            Next();
        if (T is TokenKind.LessThan or TokenKind.LessThanLessThan)
            ParseTypeParameters();
        ParseParameters(isConstructor: false);
        Expect(TokenKind.EqualsGreaterThan);
        bool saved = _noConditional;
        _noConditional = false;
        ParseReturnType();
        _noConditional = saved;
    }

    private void ParseUnionType()
    {
        Optional(TokenKind.Bar);
        ParseIntersectionType();
        while (_ok && T == TokenKind.Bar)
        {
            Next();
            ParseIntersectionType();
        }
    }

    private void ParseIntersectionType()
    {
        Optional(TokenKind.Ampersand);
        ParseTypeOperator();
        while (_ok && T == TokenKind.Ampersand)
        {
            Next();
            ParseTypeOperator();
        }
    }

    /// <summary>TypeScript: <c>parseTypeOperatorOrHigher</c>.</summary>
    private void ParseTypeOperator()
    {
        Guard();
        switch (K)
        {
            case Keyword.Keyof:
            case Keyword.Unique:
            case Keyword.Readonly:
                Next();
                ParseTypeOperator();
                return;
            case Keyword.Infer:
                Next();
                ExpectIdentifier();
                if (K == Keyword.Extends)
                {
                    // TypeScript: tryParseConstraintOfInferType.
                    var saved = Save();
                    Next();
                    bool outer = _noConditional;
                    _noConditional = true;
                    ParseType();
                    _noConditional = outer;
                    if (!_ok || (!outer && T == TokenKind.Question))
                        Restore(saved);
                }
                return;
        }
        if (IsStartOfFunctionType())
        {
            // `A | () => B` is an error in TypeScript but still parses.
            ParseFunctionType();
            return;
        }
        ParseNonArrayType();
        // TypeScript: parsePostfixTypeOrHigher, including JSDoc `T!` and `T?`, which parse in
        // .ts files with a checker error.
        while (_ok && !LineBreakBefore)
        {
            if (T == TokenKind.OpenBracket)
            {
                Next();
                if (T != TokenKind.CloseBracket)
                    ParseNestedType();
                Expect(TokenKind.CloseBracket);
            }
            else if (T == TokenKind.Exclamation)
            {
                Next();
            }
            else if (T == TokenKind.Question && !NextIsStartOfType())
            {
                Next();
            }
            else
            {
                return;
            }
        }
    }

    /// <summary>TypeScript: <c>nextTokenIsStartOfType</c>, which tells <c>T?</c> from <c>A extends B ? C : D</c>.</summary>
    private readonly bool NextIsStartOfType()
    {
        var next = Peek();
        switch (next.Kind)
        {
            case TokenKind.Identifier:
            case TokenKind.StringLiteral:
            case TokenKind.NumericLiteral:
            case TokenKind.BigIntLiteral:
            case TokenKind.NoSubstitutionTemplate:
            case TokenKind.TemplateHead:
            case TokenKind.OpenBrace:
            case TokenKind.OpenBracket:
            case TokenKind.OpenParen:
            case TokenKind.LessThan:
            case TokenKind.Bar:
            case TokenKind.Ampersand:
            case TokenKind.Minus:
            case TokenKind.Question:
            case TokenKind.Exclamation:
            case TokenKind.Asterisk:
            case TokenKind.DotDotDot:
                return true;
            default:
                return false;
        }
    }

    /// <summary>TypeScript: <c>parseNonArrayType</c>.</summary>
    private void ParseNonArrayType()
    {
        switch (T)
        {
            case TokenKind.StringLiteral:
            case TokenKind.NumericLiteral:
            case TokenKind.BigIntLiteral:
                Next();
                return;
            case TokenKind.Minus:
                Next();
                if (T is TokenKind.NumericLiteral or TokenKind.BigIntLiteral)
                    Next();
                else
                    Fail();
                return;
            case TokenKind.Question:
            case TokenKind.Exclamation:
                // JSDoc `?T`, `!T` and a lone `?`.
                Next();
                if (T is not (TokenKind.Comma or TokenKind.CloseParen or TokenKind.CloseBracket or
                    TokenKind.GreaterThan or TokenKind.Equals or TokenKind.Semicolon or TokenKind.CloseBrace))
                    ParseTypeOperator();
                return;
            case TokenKind.Asterisk:
                // JSDoc `*`.
                Next();
                return;
            case TokenKind.NoSubstitutionTemplate:
            case TokenKind.TemplateHead:
                ParseTemplateLiteralType();
                return;
            case TokenKind.OpenBrace:
                if (IsStartOfMappedType())
                    ParseMappedType();
                else
                    ParseTypeLiteral();
                return;
            case TokenKind.OpenBracket:
                ParseTupleType();
                return;
            case TokenKind.OpenParen:
                Next();
                ParseNestedType();
                Expect(TokenKind.CloseParen);
                return;
            case TokenKind.Identifier:
                break;
            default:
                Fail();
                return;
        }

        switch (K)
        {
            case Keyword.Void:
            case Keyword.Null:
            case Keyword.True:
            case Keyword.False:
            case Keyword.This:
            case Keyword.Const:
                Next();
                return;
            case Keyword.Typeof:
                Next();
                if (K == Keyword.Import)
                {
                    ParseImportType();
                    return;
                }
                ParseEntityName(allowPrivate: true);
                ParseTypeArgumentsIfSameLine();
                return;
            case Keyword.Import:
                ParseImportType();
                return;
        }
        if (Keywords.IsReserved(K))
        {
            Fail();
            return;
        }
        ParseEntityName(allowPrivate: false);
        ParseTypeArgumentsIfSameLine();
    }

    private void ParseEntityName(bool allowPrivate)
    {
        if (K == Keyword.This)
            Next();
        else
            ExpectIdentifierName();
        while (_ok && T == TokenKind.Dot)
        {
            Next();
            if (allowPrivate && T == TokenKind.PrivateName)
                Next();
            else
                ExpectIdentifierName();
        }
    }

    private void ParseTypeArgumentsIfSameLine()
    {
        if (T is TokenKind.LessThan or TokenKind.LessThanLessThan && !LineBreakBefore)
            ParseTypeArguments();
    }

    /// <summary><c>import("m", { with: { ... } }).A.B&lt;T&gt;</c>. TypeScript: <c>parseImportType</c>.</summary>
    private void ParseImportType()
    {
        Next();
        Expect(TokenKind.OpenParen);
        ParseNestedType();
        if (T == TokenKind.Comma)
        {
            Next();
            if (T == TokenKind.OpenBrace)
                ParseObjectLiteral();
            Optional(TokenKind.Comma);
        }
        Expect(TokenKind.CloseParen);
        while (_ok && T == TokenKind.Dot)
        {
            Next();
            ExpectIdentifierName();
        }
        ParseTypeArgumentsIfSameLine();
    }

    /// <summary><c>&lt;A, B&gt;</c> in a type position: the closing <c>&gt;</c> is never re-scanned.</summary>
    private void ParseTypeArguments()
    {
        _lex.ReScanLessThan();
        Expect(TokenKind.LessThan);
        bool saved = _noConditional;
        _noConditional = false;
        while (_ok && T != TokenKind.GreaterThan)
        {
            ParseType();
            if (T != TokenKind.GreaterThan)
                Expect(TokenKind.Comma);
        }
        _noConditional = saved;
        Expect(TokenKind.GreaterThan);
    }

    /// <summary>
    /// <c>&lt;const T extends C = D, in out U&gt;</c>. TypeScript: <c>parseTypeParameters</c>.
    /// </summary>
    private void ParseTypeParameters()
    {
        _lex.ReScanLessThan();
        Expect(TokenKind.LessThan);
        bool saved = _noConditional;
        _noConditional = false;
        while (_ok && T != TokenKind.GreaterThan)
        {
            while (_ok && K is Keyword.Const or Keyword.In or Keyword.Out && Peek().Kind == TokenKind.Identifier)
                Next();
            ExpectIdentifier();
            if (K == Keyword.Extends)
            {
                Next();
                ParseType();
            }
            if (T == TokenKind.Equals)
            {
                Next();
                ParseType();
            }
            if (T != TokenKind.GreaterThan)
                Expect(TokenKind.Comma);
        }
        _noConditional = saved;
        Expect(TokenKind.GreaterThan);
    }

    /// <summary>
    /// A return type: <c>T</c>, <c>x is T</c>, <c>asserts x</c>, <c>asserts this is T</c>.
    /// TypeScript: <c>parseTypeOrTypePredicate</c> and <c>parseAssertsTypePredicate</c>.
    /// </summary>
    private void ParseReturnType()
    {
        if (K == Keyword.Asserts)
        {
            var next = Peek();
            if (!IsLineBreak(next) && next.Kind == TokenKind.Identifier &&
                (!Keywords.IsReserved(next.Keyword) || next.Keyword == Keyword.This))
            {
                Next();
                Next();
                if (K == Keyword.Is && !LineBreakBefore)
                {
                    Next();
                    ParseType();
                }
                return;
            }
        }
        if (IsIdentifier() || K == Keyword.This)
        {
            var next = Peek();
            if (next.Kind == TokenKind.Identifier && next.Keyword == Keyword.Is && !IsLineBreak(next))
            {
                Next();
                Next();
                ParseType();
                return;
            }
        }
        ParseType();
    }

    private void ParseTemplateLiteralType()
    {
        if (T == TokenKind.NoSubstitutionTemplate)
        {
            Next();
            return;
        }
        Next();
        while (_ok)
        {
            ParseNestedType();
            if (T != TokenKind.CloseBrace)
            {
                Fail();
                return;
            }
            _lex.ReScanTemplateContinuation();
            if ((_lex.Flags & TokenFlags.Unterminated) != 0)
            {
                Fail();
                return;
            }
            bool tail = T == TokenKind.TemplateTail;
            Next();
            if (tail)
                return;
        }
    }

    /// <summary>TypeScript: <c>isStartOfMappedType</c>, from <c>{</c>.</summary>
    private readonly bool IsStartOfMappedType()
    {
        var copy = _lex;
        copy.Next();
        if (copy.Kind is TokenKind.Plus or TokenKind.Minus)
        {
            copy.Next();
            return copy.Kind == TokenKind.Identifier && copy.Keyword == Keyword.Readonly;
        }
        if (copy.Kind == TokenKind.Identifier && copy.Keyword == Keyword.Readonly)
            copy.Next();
        if (copy.Kind != TokenKind.OpenBracket)
            return false;
        copy.Next();
        if (copy.Kind != TokenKind.Identifier)
            return false;
        copy.Next();
        return copy.Kind == TokenKind.Identifier && copy.Keyword == Keyword.In;
    }

    /// <summary><c>{ readonly [K in T as N]?: X }</c>. TypeScript: <c>parseMappedType</c>.</summary>
    private void ParseMappedType()
    {
        Next();
        if (T is TokenKind.Plus or TokenKind.Minus)
            Next();
        if (K == Keyword.Readonly)
            Next();
        Expect(TokenKind.OpenBracket);
        ExpectIdentifierName();
        ExpectKeyword(Keyword.In);
        ParseNestedType();
        if (K == Keyword.As)
        {
            Next();
            ParseNestedType();
        }
        Expect(TokenKind.CloseBracket);
        if (T is TokenKind.Plus or TokenKind.Minus)
            Next();
        Optional(TokenKind.Question);
        if (T == TokenKind.Colon)
        {
            Next();
            ParseNestedType();
        }
        if (T is TokenKind.Semicolon or TokenKind.Comma)
            Next();
        Expect(TokenKind.CloseBrace);
    }

    private void ParseTupleType()
    {
        Next();
        bool saved = _noConditional;
        _noConditional = false;
        while (_ok && T != TokenKind.CloseBracket)
        {
            Optional(TokenKind.DotDotDot);
            // Named member: `name: T`, `name?: T`.
            if (IsIdentifierName())
            {
                var next = Peek();
                if (next.Kind == TokenKind.Colon || (next.Kind == TokenKind.Question && Peek2().Kind == TokenKind.Colon))
                {
                    Next();
                    Optional(TokenKind.Question);
                    Expect(TokenKind.Colon);
                    Optional(TokenKind.DotDotDot);
                }
            }
            ParseType();
            Optional(TokenKind.Question);
            if (T != TokenKind.CloseBracket)
                Expect(TokenKind.Comma);
        }
        _noConditional = saved;
        Expect(TokenKind.CloseBracket);
    }

    /// <summary><c>{ ... }</c> in a type, or an interface body. TypeScript: <c>parseObjectTypeMembers</c>.</summary>
    private void ParseTypeLiteral()
    {
        Expect(TokenKind.OpenBrace);
        bool saved = _noConditional;
        _noConditional = false;
        while (_ok && T != TokenKind.CloseBrace)
        {
            ParseTypeMember();
            ParseTypeMemberSeparator();
        }
        _noConditional = saved;
        Expect(TokenKind.CloseBrace);
    }

    private void ParseTypeMemberSeparator()
    {
        if (T is TokenKind.Semicolon or TokenKind.Comma)
            Next();
        else if (!CanInsertSemicolon())
            Fail();
    }

    /// <summary>TypeScript: <c>parseTypeMember</c>.</summary>
    private void ParseTypeMember()
    {
        Guard();
        if (T is TokenKind.OpenParen or TokenKind.LessThan or TokenKind.LessThanLessThan)
        {
            ParseSignatureRest();
            return;
        }
        if (K == Keyword.New && Peek().Kind is TokenKind.OpenParen or TokenKind.LessThan)
        {
            Next();
            ParseSignatureRest();
            return;
        }
        while (_ok && K is Keyword.Readonly or Keyword.Public or Keyword.Private or Keyword.Protected or
            Keyword.Static or Keyword.Declare or Keyword.Abstract or Keyword.Override or Keyword.Export &&
            NextCanFollowModifier(sameLine: true))
            Next();
        if (T == TokenKind.OpenBracket && IsIndexSignature())
        {
            ParseIndexSignature();
            return;
        }
        if (K is Keyword.Get or Keyword.Set && IsPropertyNameStart(Peek().Kind))
            Next();
        ParsePropertyName();
        Optional(TokenKind.Question);
        if (T is TokenKind.OpenParen or TokenKind.LessThan or TokenKind.LessThanLessThan)
        {
            ParseSignatureRest();
            return;
        }
        if (T == TokenKind.Colon)
        {
            Next();
            ParseType();
        }
    }

    /// <summary>Type parameters, parameters and an optional <c>: R</c>, in a type member.</summary>
    private void ParseSignatureRest()
    {
        if (T is TokenKind.LessThan or TokenKind.LessThanLessThan)
            ParseTypeParameters();
        ParseParameters(isConstructor: false);
        if (T == TokenKind.Colon)
        {
            Next();
            ParseReturnType();
        }
    }

    /// <summary><c>[key: string]: T</c>, from <c>[</c>. TypeScript: <c>parseIndexSignatureDeclaration</c>.</summary>
    private void ParseIndexSignature()
    {
        Next();
        while (_ok && T != TokenKind.CloseBracket)
        {
            while (_ok && IsClassModifier(K) && Peek().Kind == TokenKind.Identifier)
                Next();
            Optional(TokenKind.DotDotDot);
            ParseBindingTarget();
            Optional(TokenKind.Question);
            if (T == TokenKind.Colon)
            {
                Next();
                ParseNestedType();
            }
            if (T != TokenKind.CloseBracket)
                Expect(TokenKind.Comma);
        }
        Expect(TokenKind.CloseBracket);
        if (T == TokenKind.Colon)
        {
            Next();
            ParseType();
        }
    }
}
