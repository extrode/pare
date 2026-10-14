using Extrode.Pare.Lexing;

namespace Extrode.Pare.Parsing;

internal ref partial struct Parser
{
    /// <summary>Expression with commas. TypeScript: <c>parseExpression</c>.</summary>
    private void ParseExpression()
    {
        ParseAssignment();
        while (_ok && T == TokenKind.Comma)
        {
            Next();
            ParseAssignment();
        }
    }

    /// <summary>An expression where <c>in</c> is an operator again (inside brackets and parens).</summary>
    private void ParseExpressionAllowIn()
    {
        bool saved = _disallowIn, savedDecorator = _decorator;
        _disallowIn = false;
        _decorator = false;
        ParseExpression();
        _disallowIn = saved;
        _decorator = savedDecorator;
    }

    private void ParseAssignmentAllowIn()
    {
        bool saved = _disallowIn, savedDecorator = _decorator;
        _disallowIn = false;
        _decorator = false;
        ParseAssignment();
        _disallowIn = saved;
        _decorator = savedDecorator;
    }

    /// <summary>TypeScript: <c>parseAssignmentExpressionOrHigher</c>.</summary>
    private void ParseAssignment(bool allowReturnTypeInArrow = true)
    {
        Guard();
        if (!_ok)
            return;
        if (K == Keyword.Yield && IsYieldExpression())
        {
            ParseYield();
            return;
        }
        if (TryParseArrowFunction(allowReturnTypeInArrow))
            return;

        ParseBinary();

        if (IsAssignmentOperator(T))
        {
            Next();
            ParseAssignment(allowReturnTypeInArrow);
            return;
        }
        if (T == TokenKind.Question)
        {
            // TypeScript: parseConditionalExpressionRest. The true branch may not end in an arrow
            // function with a return type unless a ':' follows, because `a ? (b): c => d` reads
            // either way.
            Next();
            bool saved = _disallowIn;
            _disallowIn = false;
            ParseAssignment(allowReturnTypeInArrow: false);
            _disallowIn = saved;
            Expect(TokenKind.Colon);
            ParseAssignment(allowReturnTypeInArrow);
        }
    }

    private readonly bool IsYieldExpression()
    {
        // TypeScript: isYieldExpression. Outside a generator, `yield` is an identifier unless an
        // operand follows on the same line.
        if (_yield)
            return true;
        var next = Peek();
        return !IsLineBreak(next) && next.Kind is TokenKind.Identifier or TokenKind.NumericLiteral or
            TokenKind.BigIntLiteral or TokenKind.StringLiteral;
    }

    private void ParseYield()
    {
        Next();
        if (LineBreakBefore)
            return;
        if (T == TokenKind.Asterisk)
        {
            Next();
            ParseAssignment();
        }
        else if (IsStartOfExpression())
        {
            ParseAssignment();
        }
    }

    /// <summary>
    /// Unary operands joined by binary operators. Precedence does not change which tokens are type
    /// syntax, so one flat loop recognizes every binary expression. TypeScript:
    /// <c>parseBinaryExpressionRest</c>.
    /// </summary>
    private void ParseBinary()
    {
        ParseUnary();
        while (_ok)
        {
            if (T == TokenKind.GreaterThan)
                _lex.ReScanGreaterThan();
            var k = K;
            if (k is Keyword.As or Keyword.Satisfies)
            {
                // `x\nas T` is two statements: `as` cannot follow a line break.
                if (LineBreakBefore)
                    break;
                int start = Start;
                Next();
                ParseType();
                Erase(start, PrevEnd);
                continue;
            }
            if (!IsBinaryOperator())
                break;
            Next();
            ParseUnary();
        }
    }

    private readonly bool IsBinaryOperator()
    {
        switch (T)
        {
            case TokenKind.QuestionQuestion:
            case TokenKind.BarBar:
            case TokenKind.AmpersandAmpersand:
            case TokenKind.Bar:
            case TokenKind.Caret:
            case TokenKind.Ampersand:
            case TokenKind.EqualsEquals:
            case TokenKind.ExclamationEquals:
            case TokenKind.EqualsEqualsEquals:
            case TokenKind.ExclamationEqualsEquals:
            case TokenKind.LessThan:
            case TokenKind.GreaterThan:
            case TokenKind.LessThanEquals:
            case TokenKind.GreaterThanEquals:
            case TokenKind.LessThanLessThan:
            case TokenKind.GreaterThanGreaterThan:
            case TokenKind.GreaterThanGreaterThanGreaterThan:
            case TokenKind.Plus:
            case TokenKind.Minus:
            case TokenKind.Asterisk:
            case TokenKind.Slash:
            case TokenKind.Percent:
            case TokenKind.AsteriskAsterisk:
                return true;
            case TokenKind.Identifier:
                return K switch
                {
                    Keyword.Instanceof or Keyword.As or Keyword.Satisfies => true,
                    Keyword.In => !_disallowIn,
                    _ => false,
                };
            default:
                return false;
        }
    }

    private static bool IsAssignmentOperator(TokenKind kind) => kind is TokenKind.Equals or
        TokenKind.PlusEquals or TokenKind.MinusEquals or TokenKind.AsteriskEquals or
        TokenKind.AsteriskAsteriskEquals or TokenKind.SlashEquals or TokenKind.PercentEquals or
        TokenKind.LessThanLessThanEquals or TokenKind.GreaterThanGreaterThanEquals or
        TokenKind.GreaterThanGreaterThanGreaterThanEquals or TokenKind.AmpersandEquals or
        TokenKind.BarEquals or TokenKind.CaretEquals or TokenKind.AmpersandAmpersandEquals or
        TokenKind.BarBarEquals or TokenKind.QuestionQuestionEquals;

    /// <summary>TypeScript: <c>isStartOfExpression</c>.</summary>
    private readonly bool IsStartOfExpression()
    {
        switch (T)
        {
            case TokenKind.Plus:
            case TokenKind.Minus:
            case TokenKind.Tilde:
            case TokenKind.Exclamation:
            case TokenKind.PlusPlus:
            case TokenKind.MinusMinus:
            case TokenKind.LessThan:
            case TokenKind.NumericLiteral:
            case TokenKind.BigIntLiteral:
            case TokenKind.StringLiteral:
            case TokenKind.NoSubstitutionTemplate:
            case TokenKind.TemplateHead:
            case TokenKind.OpenParen:
            case TokenKind.OpenBracket:
            case TokenKind.OpenBrace:
            case TokenKind.Slash:
            case TokenKind.SlashEquals:
            case TokenKind.PrivateName:
            case TokenKind.At:
                return true;
            case TokenKind.Identifier:
                return !Keywords.IsReserved(_lex.Keyword) || K is Keyword.This or Keyword.Super or
                    Keyword.Null or Keyword.True or Keyword.False or Keyword.Function or Keyword.Class or
                    Keyword.New or Keyword.Import or Keyword.Delete or Keyword.Typeof or Keyword.Void;
            default:
                return false;
        }
    }

    /// <summary>TypeScript: <c>parseUnaryExpressionOrHigher</c> and <c>parseSimpleUnaryExpression</c>.</summary>
    private void ParseUnary()
    {
        Guard();
        switch (T)
        {
            case TokenKind.Plus:
            case TokenKind.Minus:
            case TokenKind.Tilde:
            case TokenKind.Exclamation:
            case TokenKind.PlusPlus:
            case TokenKind.MinusMinus:
                Next();
                ParseUnary();
                return;
            case TokenKind.LessThan:
                ParseAngleBracketAssertion();
                return;
            case TokenKind.Identifier:
                switch (K)
                {
                    case Keyword.Delete:
                    case Keyword.Void:
                    case Keyword.Typeof:
                        Next();
                        ParseUnary();
                        return;
                    case Keyword.Await when IsAwaitExpression():
                        Next();
                        ParseUnary();
                        return;
                }
                break;
        }

        ParseLeftHandSide();
        if (T is TokenKind.PlusPlus or TokenKind.MinusMinus && !LineBreakBefore)
            Next();
    }

    private readonly bool IsAwaitExpression()
    {
        // TypeScript: isAwaitExpression. In a script `await` is an identifier even at top level;
        // `[await]` and `f(await)` stay identifiers so such scripts still strip.
        if (_await)
        {
            var after = Peek();
            return after.Kind is not (TokenKind.CloseBracket or TokenKind.CloseParen or TokenKind.Comma or
                TokenKind.Semicolon or TokenKind.Colon or TokenKind.CloseBrace or TokenKind.EqualsGreaterThan or
                TokenKind.Equals or TokenKind.EndOfFile);
        }
        var next = Peek();
        return !IsLineBreak(next) && next.Kind is TokenKind.Identifier or TokenKind.NumericLiteral or
            TokenKind.BigIntLiteral or TokenKind.StringLiteral;
    }

    /// <summary><c>&lt;T&gt;expr</c>: rejected, then parsed on for further diagnostics.</summary>
    private void ParseAngleBracketAssertion()
    {
        int start = Start;
        Next();
        ParseType();
        if (T != TokenKind.GreaterThan)
        {
            Fail();
            return;
        }
        Next();
        Reject(DiagnosticCode.AngleBracketAssertion, start, PrevEnd);
        ParseUnary();
    }

    /// <summary>TypeScript: <c>parseLeftHandSideExpressionOrHigher</c>.</summary>
    private void ParseLeftHandSide()
    {
        if (K == Keyword.New)
            ParseNew();
        else
            ParsePrimary();
        ParseMemberRest(allowCall: true);
    }

    /// <summary>TypeScript: <c>parseNewExpressionOrNewDotTarget</c>.</summary>
    private void ParseNew()
    {
        Guard();
        Next();
        if (T == TokenKind.Dot)
        {
            // new.target
            Next();
            ExpectIdentifierName();
            return;
        }
        if (K == Keyword.New)
            ParseNew();
        else
            ParsePrimary();
        ParseMemberRest(allowCall: false);
        if (T == TokenKind.OpenParen)
            ParseArguments();
    }

    /// <summary>
    /// Property access, element access, calls, tagged templates, non-null <c>!</c> and type
    /// arguments. TypeScript: <c>parseMemberExpressionRest</c> and <c>parseCallExpressionRest</c>.
    /// </summary>
    private void ParseMemberRest(bool allowCall)
    {
        while (_ok)
        {
            switch (T)
            {
                case TokenKind.Dot:
                    Next();
                    if (T == TokenKind.PrivateName)
                        Next();
                    else
                        ExpectIdentifierName();
                    continue;
                case TokenKind.QuestionDot:
                    Next();
                    if (T == TokenKind.OpenParen)
                        ParseArguments();
                    else if (T == TokenKind.OpenBracket)
                        ParseElementAccess();
                    else if (T is TokenKind.LessThan or TokenKind.LessThanLessThan)
                    {
                        // a?.<T>()
                        int start = Start;
                        _lex.ReScanLessThan();
                        Next();
                        ParseTypeArgumentsRest();
                        Erase(start, PrevEnd);
                        if (T != TokenKind.OpenParen)
                            Fail();
                        ParseArguments();
                    }
                    else if (T == TokenKind.PrivateName)
                        Next();
                    else
                        ExpectIdentifierName();
                    continue;
                case TokenKind.OpenBracket:
                    if (_decorator)
                        return;
                    ParseElementAccess();
                    continue;
                case TokenKind.NoSubstitutionTemplate:
                case TokenKind.TemplateHead:
                    ParseTemplate();
                    continue;
                case TokenKind.Exclamation:
                    if (LineBreakBefore)
                        return;
                    Erase(Start, End);
                    Next();
                    continue;
                case TokenKind.LessThan:
                case TokenKind.LessThanLessThan:
                    if (TryParseTypeArgumentsInExpression())
                        continue;
                    return;
                case TokenKind.OpenParen:
                    if (!allowCall)
                        return;
                    ParseArguments();
                    continue;
                default:
                    return;
            }
        }
    }

    private void ParseElementAccess()
    {
        Next();
        ParseExpressionAllowIn();
        Expect(TokenKind.CloseBracket);
    }

    private void ParseArguments()
    {
        Expect(TokenKind.OpenParen);
        bool saved = _disallowIn, savedDecorator = _decorator;
        _disallowIn = false;
        _decorator = false;
        while (_ok && T != TokenKind.CloseParen)
        {
            Optional(TokenKind.DotDotDot);
            ParseAssignment();
            if (T != TokenKind.CloseParen)
                Expect(TokenKind.Comma);
        }
        _disallowIn = saved;
        _decorator = savedDecorator;
        Expect(TokenKind.CloseParen);
    }

    /// <summary>
    /// <c>f&lt;T&gt;(x)</c>, <c>new C&lt;T&gt;()</c>, <c>f&lt;T&gt;</c>: speculative, kept only when the
    /// token after <c>&gt;</c> cannot continue an expression. TypeScript:
    /// <c>parseTypeArgumentsInExpression</c> and <c>canFollowTypeArgumentsInExpression</c>.
    /// </summary>
    private bool TryParseTypeArgumentsInExpression()
    {
        var saved = Save();
        int start = Start;
        _lex.ReScanLessThan();
        Next();
        if (_ok && T == TokenKind.GreaterThan)
            Fail(); // `<>` is not a type argument list
        ParseTypeArgumentsRest();
        if (_ok && CanFollowTypeArgumentsInExpression())
        {
            Erase(start, PrevEnd);
            return true;
        }
        Restore(saved);
        return false;
    }

    /// <summary>Types up to and including the closing <c>&gt;</c>, which must stand alone.</summary>
    private void ParseTypeArgumentsRest()
    {
        while (_ok && T != TokenKind.GreaterThan)
        {
            ParseType();
            if (T != TokenKind.GreaterThan)
                Expect(TokenKind.Comma);
        }
        if (T != TokenKind.GreaterThan)
        {
            Fail();
            return;
        }
        // `f<T>= x` and `f<T>> x` are operators, not type arguments.
        _lex.ReScanGreaterThan();
        Expect(TokenKind.GreaterThan);
    }

    private readonly bool CanFollowTypeArgumentsInExpression()
    {
        switch (T)
        {
            case TokenKind.OpenParen:
            case TokenKind.NoSubstitutionTemplate:
            case TokenKind.TemplateHead:
                return true;
            case TokenKind.LessThan:
            case TokenKind.GreaterThan:
            case TokenKind.Plus:
            case TokenKind.Minus:
                return false;
        }
        return LineBreakBefore || IsBinaryOperator() || !IsStartOfExpression();
    }

    /// <summary>TypeScript: <c>parsePrimaryExpression</c>.</summary>
    private void ParsePrimary()
    {
        switch (T)
        {
            case TokenKind.NumericLiteral:
            case TokenKind.BigIntLiteral:
            case TokenKind.StringLiteral:
            case TokenKind.PrivateName:
                Next();
                return;
            case TokenKind.NoSubstitutionTemplate:
            case TokenKind.TemplateHead:
                ParseTemplate();
                return;
            case TokenKind.Slash:
            case TokenKind.SlashEquals:
                _lex.ReScanSlashAsRegex();
                if ((_lex.Flags & TokenFlags.Unterminated) != 0)
                {
                    Fail();
                    return;
                }
                Next();
                return;
            case TokenKind.OpenParen:
                Next();
                ParseExpressionAllowIn();
                Expect(TokenKind.CloseParen);
                return;
            case TokenKind.OpenBracket:
                ParseArrayLiteral();
                return;
            case TokenKind.OpenBrace:
                ParseObjectLiteral();
                return;
            case TokenKind.At:
                ParseDecorators();
                if (K != Keyword.Class)
                {
                    Fail();
                    return;
                }
                ParseClass(declaration: false, start: Start, prevEnd: PrevEnd);
                return;
            case TokenKind.Identifier:
                switch (K)
                {
                    case Keyword.Function:
                        ParseFunction(declaration: false, isAsync: false, start: Start, prevEnd: PrevEnd);
                        return;
                    case Keyword.Class:
                        ParseClass(declaration: false, start: Start, prevEnd: PrevEnd);
                        return;
                    case Keyword.Async:
                    {
                        var next = Peek();
                        if (next.Kind == TokenKind.Identifier && next.Keyword == Keyword.Function && !IsLineBreak(next))
                        {
                            int start = Start;
                            Next();
                            ParseFunction(declaration: false, isAsync: true, start: start, prevEnd: PrevEnd);
                            return;
                        }
                        Next();
                        return;
                    }
                    case Keyword.This:
                    case Keyword.Super:
                    case Keyword.Null:
                    case Keyword.True:
                    case Keyword.False:
                        Next();
                        return;
                    case Keyword.Import:
                        Next();
                        // import("./x.ts"): rewrite only a lone string argument.
                        if (T == TokenKind.OpenParen && Peek().Kind == TokenKind.StringLiteral &&
                            Peek2().Kind is TokenKind.CloseParen or TokenKind.Comma)
                        {
                            var arg = Peek();
                            RewriteSpecifier(arg.Start, arg.Pos);
                        }
                        return;
                }
                if (IsIdentifier())
                {
                    Next();
                    return;
                }
                Fail();
                return;
            default:
                Fail();
                return;
        }
    }

    private void ParseTemplate()
    {
        if (T == TokenKind.NoSubstitutionTemplate)
        {
            Next();
            return;
        }
        // TemplateHead
        Next();
        while (_ok)
        {
            ParseExpressionAllowIn();
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

    private void ParseArrayLiteral()
    {
        Next();
        bool saved = _disallowIn;
        _disallowIn = false;
        while (_ok && T != TokenKind.CloseBracket)
        {
            if (T == TokenKind.Comma)
            {
                Next();
                continue;
            }
            Optional(TokenKind.DotDotDot);
            ParseAssignment();
            if (T != TokenKind.CloseBracket)
                Expect(TokenKind.Comma);
        }
        _disallowIn = saved;
        Expect(TokenKind.CloseBracket);
    }

    /// <summary>TypeScript: <c>parseObjectLiteralElement</c>.</summary>
    private void ParseObjectLiteral()
    {
        Next();
        bool saved = _disallowIn;
        _disallowIn = false;
        while (_ok && T != TokenKind.CloseBrace)
        {
            if (T == TokenKind.DotDotDot)
            {
                Next();
                ParseAssignment();
            }
            else
            {
                ParseObjectMember();
            }
            if (T != TokenKind.CloseBrace)
                Expect(TokenKind.Comma);
        }
        _disallowIn = saved;
        Expect(TokenKind.CloseBrace);
    }

    private void ParseObjectMember()
    {
        bool isAsync = false, isGenerator = false;
        if (K == Keyword.Async)
        {
            var next = Peek();
            if (!IsLineBreak(next) && (IsPropertyNameStart(next.Kind) || next.Kind == TokenKind.Asterisk))
            {
                Next();
                isAsync = true;
            }
        }
        else if (K is Keyword.Get or Keyword.Set && IsPropertyNameStart(Peek().Kind))
        {
            Next();
            ParsePropertyName();
            ParseFunctionRest(isAsync: false, isGenerator: false, bodyRequired: true, isConstructor: false);
            return;
        }
        if (T == TokenKind.Asterisk)
        {
            Next();
            isGenerator = true;
        }

        bool shorthand = IsIdentifier() || K is Keyword.Await or Keyword.Yield;
        ParsePropertyName();
        if (T is TokenKind.OpenParen or TokenKind.LessThan)
        {
            ParseFunctionRest(isAsync, isGenerator, bodyRequired: true, isConstructor: false);
            return;
        }
        if (isAsync || isGenerator)
        {
            Fail();
            return;
        }
        if (T == TokenKind.Colon)
        {
            Next();
            ParseAssignment();
            return;
        }
        if (!shorthand)
        {
            Fail();
            return;
        }
        // `{ a = 1 } = obj`: a cover initializer, legal only in destructuring.
        if (T == TokenKind.Equals)
        {
            Next();
            ParseAssignment();
        }
    }

    private static bool IsPropertyNameStart(TokenKind kind) => kind is TokenKind.Identifier or
        TokenKind.StringLiteral or TokenKind.NumericLiteral or TokenKind.BigIntLiteral or
        TokenKind.OpenBracket or TokenKind.PrivateName;

    /// <summary>TypeScript: <c>parsePropertyName</c>.</summary>
    private void ParsePropertyName()
    {
        switch (T)
        {
            case TokenKind.Identifier:
            case TokenKind.StringLiteral:
            case TokenKind.NumericLiteral:
            case TokenKind.BigIntLiteral:
            case TokenKind.PrivateName:
                Next();
                return;
            case TokenKind.OpenBracket:
                Next();
                ParseAssignmentAllowIn();
                Expect(TokenKind.CloseBracket);
                return;
            default:
                Fail();
                return;
        }
    }

    /// <summary>
    /// <c>(params) =&gt; body</c>, <c>async (params) =&gt;</c>, <c>&lt;T&gt;(params) =&gt;</c>,
    /// <c>x =&gt;</c> and <c>async x =&gt;</c>. A parenthesized head is parsed speculatively and
    /// kept only if <c>=&gt;</c> follows. TypeScript: <c>tryParseParenthesizedArrowFunctionExpression</c>,
    /// <c>tryParseAsyncSimpleArrowFunctionExpression</c>, <c>parseSimpleArrowFunctionExpression</c>.
    /// </summary>
    private bool TryParseArrowFunction(bool allowReturnType)
    {
        switch (T)
        {
            case TokenKind.OpenParen:
            case TokenKind.LessThan:
                return TryParseParenthesizedArrow(isAsync: false, allowReturnType);
            case TokenKind.Identifier:
                break;
            default:
                return false;
        }

        if (K == Keyword.Async && Peek().Kind != TokenKind.EqualsGreaterThan)
        {
            var next = Peek();
            if (IsLineBreak(next))
                return false;
            if (next.Kind is TokenKind.OpenParen or TokenKind.LessThan)
                return TryParseParenthesizedArrow(isAsync: true, allowReturnType);
            if (IsIdentifierToken(next) && Peek2().Kind == TokenKind.EqualsGreaterThan)
            {
                Next();
                Next();
                ParseArrowBody(isAsync: true, allowReturnType);
                return true;
            }
            return false;
        }

        if (IsIdentifier() && Peek().Kind == TokenKind.EqualsGreaterThan)
        {
            Next();
            ParseArrowBody(isAsync: false, allowReturnType);
            return true;
        }
        return false;
    }

    private bool TryParseParenthesizedArrow(bool isAsync, bool allowReturnType)
    {
        if (!_ok || IsNotArrow(Start) || !MayBeParenthesizedArrow())
            return false;
        int arrowStart = Start;
        if (TryParseParenthesizedArrowCore(isAsync, allowReturnType))
            return true;
        MarkNotArrow(arrowStart);
        return false;
    }

    /// <summary>
    /// Cheap lookahead that rules out most parenthesized expressions before speculating.
    /// TypeScript: <c>isParenthesizedArrowFunctionExpressionWorker</c> (False versus True/Unknown).
    /// </summary>
    private readonly bool MayBeParenthesizedArrow()
    {
        var copy = _lex;
        if (copy.Kind == TokenKind.Identifier && copy.Keyword == Keyword.Async)
        {
            copy.Next();
            if (copy.HasPrecedingLineBreak || copy.Kind is not (TokenKind.OpenParen or TokenKind.LessThan))
                return false;
        }
        var first = copy.Kind;
        copy.Next();
        var second = copy.Kind;
        var secondKeyword = second == TokenKind.Identifier ? copy.Keyword : Keyword.None;
        if (first != TokenKind.OpenParen)
        {
            // `<`: a type parameter list needs a name (or `const`) first.
            return second == TokenKind.Identifier && (!Keywords.IsReserved(secondKeyword) || secondKeyword == Keyword.Const);
        }
        switch (second)
        {
            case TokenKind.CloseParen:
                copy.Next();
                return copy.Kind is TokenKind.EqualsGreaterThan or TokenKind.Colon or TokenKind.OpenBrace;
            case TokenKind.OpenBracket:
            case TokenKind.OpenBrace:
            case TokenKind.DotDotDot:
            case TokenKind.At:
                return true;
            case TokenKind.Identifier:
                break;
            default:
                return false;
        }
        if (Keywords.IsReserved(secondKeyword) && secondKeyword != Keyword.This)
            return false;
        copy.Next();
        if (IsParameterModifier(secondKeyword) && copy.Kind == TokenKind.Identifier)
            return true;
        switch (copy.Kind)
        {
            case TokenKind.Colon:
            case TokenKind.Comma:
            case TokenKind.Equals:
            case TokenKind.CloseParen:
                return true;
            case TokenKind.Question:
                copy.Next();
                return copy.Kind is TokenKind.Colon or TokenKind.Comma or TokenKind.Equals or TokenKind.CloseParen;
            default:
                return false;
        }
    }

    private bool TryParseParenthesizedArrowCore(bool isAsync, bool allowReturnType)
    {
        var saved = Save();
        if (isAsync)
            Next();
        _await = isAsync;
        _yield = false;

        int typeParamsStart = -1;
        if (T is TokenKind.LessThan or TokenKind.LessThanLessThan)
        {
            typeParamsStart = Start;
            ParseTypeParameters();
        }
        int typeParamsEnd = PrevEnd;
        int openParen = Start;
        if (T != TokenKind.OpenParen)
            Fail();
        ParseParameters(isConstructor: false);
        int closeParen = PrevEnd - 1;

        bool hasReturnType = false;
        if (_ok && T == TokenKind.Colon)
        {
            hasReturnType = true;
            int colon = Start;
            Next();
            ParseReturnType();
            if (_ok && T == TokenKind.EqualsGreaterThan)
            {
                // `(a)\n: T => x` or a type spanning lines: erasing it would leave a line break
                // before `=>`, which JS forbids, so the `)` moves to the end of the erased range.
                if (HasLineBreak(closeParen + 1, PrevEnd))
                    _edits.Add(new Edit(closeParen, PrevEnd, Last: ')'));
                else
                    Erase(colon, PrevEnd);
            }
        }

        if (!_ok || T != TokenKind.EqualsGreaterThan || LineBreakBefore)
        {
            Restore(saved);
            return false;
        }

        if (typeParamsStart >= 0)
        {
            // `async <T>\n(x) => x` would turn into a call to `async`; move the `(` up.
            if (HasLineBreak(typeParamsStart, openParen))
                _edits.Add(new Edit(typeParamsStart, openParen + 1, First: '('));
            else
                Erase(typeParamsStart, typeParamsEnd);
        }

        // Restore the outer context for the body; ParseArrowBody sets its own.
        _await = saved.Await;
        _yield = saved.Yield;

        if (!allowReturnType && hasReturnType)
        {
            // Inside `a ? ... :`, keep the arrow only if the conditional's ':' follows it.
            ParseArrowBody(isAsync, allowReturnType: false);
            if (!_ok || T != TokenKind.Colon)
            {
                Restore(saved);
                return false;
            }
            return true;
        }
        ParseArrowBody(isAsync, allowReturnType);
        return true;
    }

    private void ParseArrowBody(bool isAsync, bool allowReturnType)
    {
        Expect(TokenKind.EqualsGreaterThan);
        bool savedAwait = _await, savedYield = _yield;
        _await = isAsync;
        _yield = false;
        if (T == TokenKind.OpenBrace)
            ParseFunctionBody();
        else
            ParseAssignment(allowReturnType);
        _await = savedAwait;
        _yield = savedYield;
    }
}
