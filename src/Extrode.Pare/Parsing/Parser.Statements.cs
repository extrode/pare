using Extrode.Pare.Lexing;

namespace Extrode.Pare.Parsing;

internal ref partial struct Parser
{
    /// <summary>TypeScript: <c>parseStatement</c> and <c>parseDeclaration</c>.</summary>
    private void ParseStatement() => ParseStatementAt(Start, PrevEnd);

    /// <summary>
    /// A statement whose erasable form starts at <paramref name="start"/>: the current token, or
    /// an <c>export</c> / <c>declare</c> / decorators already consumed, which are erased with it.
    /// </summary>
    private void ParseStatementAt(int start, int prevEnd)
    {
        Guard();
        if (!_ok)
            return;
        switch (T)
        {
            case TokenKind.OpenBrace:
                ParseBlock();
                return;
            case TokenKind.Semicolon:
                Next();
                return;
            case TokenKind.At:
                ParseDecorators();
                if (K == Keyword.Export)
                    ParseExport(start, prevEnd);
                else if (K == Keyword.Abstract && IsKeywordOnSameLine(Peek(), Keyword.Class))
                    ParseAbstractClass();
                else if (K == Keyword.Class)
                    ParseClass(declaration: true, start, prevEnd);
                else
                    Fail();
                return;
            case TokenKind.Identifier:
                if (TryParseKeywordStatement(start, prevEnd))
                    return;
                break;
        }

        // Labeled or expression statement.
        if (IsIdentifier() && Peek().Kind == TokenKind.Colon)
        {
            Next();
            Next();
            ParseStatement();
            return;
        }
        ParseExpressionAllowIn();
        ParseSemicolon();
    }

    private static bool IsKeywordOnSameLine(in LexerState s, Keyword k) =>
        s.Kind == TokenKind.Identifier && s.Keyword == k && !IsLineBreak(s);

    private static bool IsIdentifierOnSameLine(in LexerState s) => IsIdentifierToken(s) && !IsLineBreak(s);

    private bool TryParseKeywordStatement(int start, int prevEnd)
    {
        switch (K)
        {
            case Keyword.Var:
                ParseVariableStatement(1);
                return true;
            case Keyword.Let:
            {
                var next = Peek();
                if (IsIdentifierToken(next) || next.Kind is TokenKind.OpenBracket or TokenKind.OpenBrace)
                {
                    ParseVariableStatement(1);
                    return true;
                }
                return false;
            }
            case Keyword.Const:
                if (IsKeywordOnSameLine(Peek(), Keyword.Enum))
                    ParseEnum(start, prevEnd);
                else
                    ParseVariableStatement(1);
                return true;
            case Keyword.Using:
                if (!IsIdentifierOnSameLine(Peek()))
                    return false;
                ParseVariableStatement(1);
                return true;
            case Keyword.Await:
                if (!IsKeywordOnSameLine(Peek(), Keyword.Using) || !IsIdentifierOnSameLine(Peek2()))
                    return false;
                ParseVariableStatement(2);
                return true;
            case Keyword.Function:
                ParseFunctionDeclaration(isAsync: false, start, prevEnd);
                return true;
            case Keyword.Async:
                if (!IsKeywordOnSameLine(Peek(), Keyword.Function))
                    return false;
                Next();
                ParseFunctionDeclaration(isAsync: true, start, prevEnd);
                return true;
            case Keyword.Class:
                ParseClass(declaration: true, start, prevEnd);
                return true;
            case Keyword.If:
                Next();
                ParseParenthesizedCondition();
                ParseStatement();
                if (K == Keyword.Else)
                {
                    Next();
                    ParseStatement();
                }
                return true;
            case Keyword.Do:
                Next();
                ParseStatement();
                ExpectKeyword(Keyword.While);
                ParseParenthesizedCondition();
                // ES2015 inserts a semicolon after do-while even without a line break.
                Optional(TokenKind.Semicolon);
                return true;
            case Keyword.While:
                Next();
                ParseParenthesizedCondition();
                ParseStatement();
                return true;
            case Keyword.With:
                Next();
                ParseParenthesizedCondition();
                ParseStatement();
                return true;
            case Keyword.For:
                ParseFor();
                return true;
            case Keyword.Continue:
            case Keyword.Break:
                Next();
                if (!CanInsertSemicolon() && IsIdentifier())
                    Next();
                ParseSemicolon();
                return true;
            case Keyword.Return:
                Next();
                if (!CanInsertSemicolon())
                    ParseExpressionAllowIn();
                ParseSemicolon();
                return true;
            case Keyword.Throw:
                Next();
                ParseExpressionAllowIn();
                ParseSemicolon();
                return true;
            case Keyword.Switch:
                ParseSwitch();
                return true;
            case Keyword.Try:
                ParseTry();
                return true;
            case Keyword.Debugger:
                Next();
                ParseSemicolon();
                return true;
            case Keyword.Import:
                if (Peek().Kind is TokenKind.OpenParen or TokenKind.Dot)
                    return false;
                ParseImport(start, prevEnd);
                return true;
            case Keyword.Export:
                ParseExport(start, prevEnd);
                return true;
            case Keyword.Interface:
                if (!IsIdentifierOnSameLine(Peek()))
                    return false;
                ParseInterface(start, prevEnd);
                return true;
            case Keyword.Type:
                if (!IsIdentifierOnSameLine(Peek()))
                    return false;
                ParseTypeAlias(start, prevEnd);
                return true;
            case Keyword.Enum:
                if (!IsIdentifierToken(Peek()))
                    return false;
                ParseEnum(start, prevEnd);
                return true;
            case Keyword.Declare:
                if (!IsDeclareStart(Peek()))
                    return false;
                ParseDeclare(start, prevEnd);
                return true;
            case Keyword.Namespace:
            case Keyword.Module:
            {
                var next = Peek();
                if (IsLineBreak(next) || !(IsIdentifierToken(next) || next.Kind == TokenKind.StringLiteral))
                    return false;
                ParseNamespace(start, prevEnd);
                return true;
            }
            case Keyword.Global:
                if (Peek().Kind != TokenKind.OpenBrace || IsLineBreak(Peek()))
                    return false;
                ParseNamespace(start, prevEnd);
                return true;
            case Keyword.Abstract:
                if (!IsKeywordOnSameLine(Peek(), Keyword.Class))
                    return false;
                ParseAbstractClass();
                return true;
            default:
                return false;
        }
    }

    private static bool IsDeclareStart(in LexerState s)
    {
        if (IsLineBreak(s) || s.Kind != TokenKind.Identifier)
            return false;
        return s.Keyword is Keyword.Var or Keyword.Let or Keyword.Const or Keyword.Function or
            Keyword.Class or Keyword.Enum or Keyword.Interface or Keyword.Type or Keyword.Namespace or
            Keyword.Module or Keyword.Global or Keyword.Abstract or Keyword.Async or Keyword.Using;
    }

    private void ParseBlock()
    {
        Expect(TokenKind.OpenBrace);
        while (_ok && T != TokenKind.CloseBrace)
            ParseStatement();
        Expect(TokenKind.CloseBrace);
    }

    private void ParseParenthesizedCondition()
    {
        Expect(TokenKind.OpenParen);
        ParseExpressionAllowIn();
        Expect(TokenKind.CloseParen);
    }

    /// <summary><c>var</c>, <c>let</c>, <c>const</c>, <c>using</c> (one keyword) or <c>await using</c> (two).</summary>
    private void ParseVariableStatement(int keywords)
    {
        for (int i = 0; i < keywords; i++)
            Next();
        ParseVariableDeclarations();
        ParseSemicolon();
    }

    /// <summary>TypeScript: <c>parseVariableDeclarationList</c>. Erases <c>!</c> and <c>: T</c>.</summary>
    private void ParseVariableDeclarations()
    {
        while (_ok)
        {
            ParseBindingTarget();
            if (T == TokenKind.Exclamation && !LineBreakBefore)
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
                ParseAssignment();
            }
            if (T != TokenKind.Comma)
                return;
            Next();
        }
    }

    private void ParseFunctionDeclaration(bool isAsync, int start, int prevEnd)
    {
        var snapshot = Save();
        if (!ParseFunction(declaration: true, isAsync, start, prevEnd) && _ok)
        {
            // Overload signature: `function f(a: string): void;`.
            DropSince(snapshot);
            EraseStatement(start, PrevEnd, prevEnd);
        }
    }

    private void ParseAbstractClass()
    {
        Erase(Start, End);
        Next();
        ParseClass(declaration: true, Start, PrevEnd);
    }

    /// <summary>TypeScript: <c>parseForOrForInOrForOfStatement</c>.</summary>
    private void ParseFor()
    {
        Next();
        if (K == Keyword.Await)
            Next();
        Expect(TokenKind.OpenParen);
        if (T != TokenKind.Semicolon)
        {
            bool savedIn = _disallowIn;
            _disallowIn = true;
            int keywords = 0;
            switch (K)
            {
                case Keyword.Var:
                case Keyword.Const:
                    keywords = 1;
                    break;
                case Keyword.Let:
                {
                    var next = Peek();
                    if (IsIdentifierToken(next) || next.Kind is TokenKind.OpenBracket or TokenKind.OpenBrace)
                        keywords = 1;
                    break;
                }
                case Keyword.Using:
                {
                    var next = Peek();
                    if (IsIdentifierOnSameLine(next) && next.Keyword != Keyword.Of)
                        keywords = 1;
                    break;
                }
                case Keyword.Await:
                    if (IsKeywordOnSameLine(Peek(), Keyword.Using) && IsIdentifierOnSameLine(Peek2()))
                        keywords = 2;
                    break;
            }
            if (keywords > 0)
            {
                for (int i = 0; i < keywords; i++)
                    Next();
                ParseVariableDeclarations();
            }
            else
            {
                ParseExpression();
            }
            _disallowIn = savedIn;
        }

        if (K is Keyword.Of or Keyword.In)
        {
            bool of = K == Keyword.Of;
            Next();
            if (of)
                ParseAssignmentAllowIn();
            else
                ParseExpressionAllowIn();
            Expect(TokenKind.CloseParen);
        }
        else
        {
            Expect(TokenKind.Semicolon);
            if (T != TokenKind.Semicolon)
                ParseExpressionAllowIn();
            Expect(TokenKind.Semicolon);
            if (T != TokenKind.CloseParen)
                ParseExpressionAllowIn();
            Expect(TokenKind.CloseParen);
        }
        ParseStatement();
    }

    private void ParseSwitch()
    {
        Next();
        ParseParenthesizedCondition();
        Expect(TokenKind.OpenBrace);
        while (_ok && T != TokenKind.CloseBrace)
        {
            if (K == Keyword.Case)
            {
                Next();
                ParseExpressionAllowIn();
            }
            else
            {
                ExpectKeyword(Keyword.Default);
            }
            Expect(TokenKind.Colon);
            while (_ok && T != TokenKind.CloseBrace && K is not (Keyword.Case or Keyword.Default))
                ParseStatement();
        }
        Expect(TokenKind.CloseBrace);
    }

    private void ParseTry()
    {
        Next();
        ParseBlock();
        bool handled = false;
        if (K == Keyword.Catch)
        {
            handled = true;
            Next();
            if (T == TokenKind.OpenParen)
            {
                Next();
                ParseBindingTarget();
                if (T == TokenKind.Colon)
                {
                    int colon = Start;
                    Next();
                    ParseType();
                    Erase(colon, PrevEnd);
                }
                Expect(TokenKind.CloseParen);
            }
            ParseBlock();
        }
        if (K == Keyword.Finally)
        {
            Next();
            ParseBlock();
        }
        else if (!handled)
        {
            Fail();
        }
    }

    // ---- TypeScript declarations ---------------------------------------------------------------

    /// <summary><c>interface I&lt;T&gt; extends A, B { ... }</c>: erased whole.</summary>
    private void ParseInterface(int start, int prevEnd)
    {
        var snapshot = Save();
        Next();
        ExpectIdentifier();
        if (T is TokenKind.LessThan or TokenKind.LessThanLessThan)
            ParseTypeParameters();
        if (K == Keyword.Extends)
        {
            Next();
            ParseHeritageList();
        }
        ParseTypeLiteral();
        DropSince(snapshot);
        EraseStatement(start, PrevEnd, prevEnd);
    }

    /// <summary><c>type A&lt;T&gt; = B;</c>: erased whole.</summary>
    private void ParseTypeAlias(int start, int prevEnd)
    {
        var snapshot = Save();
        Next();
        ExpectIdentifier();
        if (T is TokenKind.LessThan or TokenKind.LessThanLessThan)
            ParseTypeParameters();
        Expect(TokenKind.Equals);
        ParseType();
        ParseSemicolon();
        DropSince(snapshot);
        EraseStatement(start, PrevEnd, prevEnd);
    }

    /// <summary><c>enum</c> and <c>const enum</c>: rejected, unless ambient (then the caller erases it).</summary>
    private void ParseEnum(int start, int prevEnd)
    {
        int keyword = Start;
        if (K == Keyword.Const)
            Next();
        Next();
        ExpectIdentifier();
        Reject(DiagnosticCode.Enum, keyword, PrevEnd);
        Expect(TokenKind.OpenBrace);
        while (_ok && T != TokenKind.CloseBrace)
        {
            ParsePropertyName();
            if (T == TokenKind.Equals)
            {
                Next();
                ParseAssignmentAllowIn();
            }
            if (T != TokenKind.CloseBrace)
                Expect(TokenKind.Comma);
        }
        Expect(TokenKind.CloseBrace);
    }

    /// <summary><c>declare ...</c>: parsed as ambient code and erased whole.</summary>
    private void ParseDeclare(int start, int prevEnd)
    {
        var snapshot = Save();
        Next();
        _ambient = true;
        ParseStatementAt(start, prevEnd);
        _ambient = snapshot.Ambient;
        if (!_ok)
            return;
        DropSince(snapshot);
        EraseStatement(start, PrevEnd, prevEnd);
    }

    /// <summary>
    /// <c>namespace A.B { }</c>, <c>module "m" { }</c>, <c>global { }</c>. Erased whole when every
    /// statement inside is erased whole (TypeScript's non-instantiated module), otherwise rejected.
    /// </summary>
    private void ParseNamespace(int start, int prevEnd)
    {
        var snapshot = Save();
        int keyword = Start;
        bool global = K == Keyword.Global;
        Next();
        if (!global)
        {
            if (T == TokenKind.StringLiteral)
            {
                Next();
                ParseImportAttributes();
            }
            else
            {
                ExpectIdentifier();
                while (_ok && T == TokenKind.Dot)
                {
                    Next();
                    ExpectIdentifier();
                }
            }
        }
        int nameEnd = PrevEnd;

        bool hasValues = false;
        if (T == TokenKind.OpenBrace)
        {
            Next();
            while (_ok && T != TokenKind.CloseBrace)
            {
                int mark = _edits.Count;
                int stmtStart = Start;
                bool empty = T == TokenKind.Semicolon;
                var declared = DeclaredKeyword();
                ParseStatement();
                if (!empty && (!ErasedWhole(mark, stmtStart) || DeclaresValue(declared)))
                    hasValues = true;
            }
            Expect(TokenKind.CloseBrace);
        }
        else
        {
            // `declare module "m";`
            ParseSemicolon();
        }
        _namespaceHasValues = hasValues;
        if (!_ok)
            return;

        if (hasValues && !_ambient)
        {
            Reject(DiagnosticCode.NamespaceWithValues, keyword, nameEnd);
            return;
        }
        DropSince(snapshot);
        EraseStatement(start, PrevEnd, prevEnd);
    }

    /// <summary>The statement parsed since <paramref name="mark"/> was erased as a single range.</summary>
    /// <summary>
    /// The keyword after <c>declare</c> or <c>export declare</c> at the current token, else
    /// <see cref="Keyword.None"/>.
    /// </summary>
    private readonly Keyword DeclaredKeyword()
    {
        if (K == Keyword.Declare)
        {
            var next = Peek();
            return IsDeclareStart(next) ? next.Keyword : Keyword.None;
        }
        if (K == Keyword.Export && Peek().Keyword == Keyword.Declare)
        {
            var next = Peek2();
            return IsDeclareStart(next) ? next.Keyword : Keyword.None;
        }
        return Keyword.None;
    }

    /// <summary>
    /// Whether a <c>declare</c>-d statement still instantiates the namespace around it: TypeScript's
    /// <c>getModuleInstanceStateWorker</c> ignores <c>declare</c>, so only interfaces, type aliases
    /// and namespaces without values stay type-only.
    /// </summary>
    private readonly bool DeclaresValue(Keyword declared) => declared switch
    {
        Keyword.None or Keyword.Interface or Keyword.Type => false,
        Keyword.Namespace or Keyword.Module => _namespaceHasValues,
        _ => true,
    };

    private readonly bool ErasedWhole(int mark, int start)
    {
        if (_edits.Count != mark + 1)
            return false;
        var e = _edits.Items[mark];
        return e.Start == start && e.End == PrevEnd;
    }

    // ---- Modules -------------------------------------------------------------------------------

    /// <summary>TypeScript: <c>parseImportDeclarationOrImportEqualsDeclaration</c>.</summary>
    private void ParseImport(int start, int prevEnd)
    {
        var snapshot = Save();
        int importKeyword = Start;
        Next();
        if (T == TokenKind.StringLiteral)
        {
            // import "m";
            RewriteSpecifier(Start, End);
            Next();
            ParseImportAttributes();
            ParseSemicolon();
            return;
        }

        // import source x from "m";  import defer * as ns from "m";
        if (T == TokenKind.Identifier && K == Keyword.None && _src[Start..End] is "source" or "defer")
        {
            var next = Peek();
            bool phase = next.Kind is TokenKind.Asterisk or TokenKind.OpenBrace ||
                (IsIdentifierToken(next) && (next.Keyword != Keyword.From ||
                    (Peek2().Kind == TokenKind.Identifier && Peek2().Keyword == Keyword.From)));
            if (phase)
                Next();
        }

        bool typeOnly = false;
        int typeKeyword = -1;
        if (K == Keyword.Type)
        {
            var next = Peek();
            if (next.Kind is TokenKind.OpenBrace or TokenKind.Asterisk)
            {
                typeOnly = true;
            }
            else if (IsIdentifierToken(next))
            {
                if (next.Keyword != Keyword.From)
                {
                    typeOnly = true;
                }
                else
                {
                    // `import type from "m"` imports a default named `type`;
                    // `import type from from "m"` is type-only.
                    var after = Peek2();
                    typeOnly = (after.Kind == TokenKind.Identifier && after.Keyword == Keyword.From) ||
                        after.Kind == TokenKind.Equals;
                }
            }
            if (typeOnly)
            {
                typeKeyword = Start;
                Next();
            }
        }

        bool hasDefault = false, hasBindings = false;
        if (IsIdentifier())
        {
            Next();
            hasDefault = true;
            if (T == TokenKind.Equals)
            {
                ParseImportEquals(start, prevEnd, importKeyword, typeOnly, snapshot);
                return;
            }
            if (T == TokenKind.Comma)
                Next();
        }
        if (T == TokenKind.Asterisk)
        {
            Next();
            ExpectKeyword(Keyword.As);
            ExpectIdentifier();
            hasBindings = true;
        }
        else if (T == TokenKind.OpenBrace)
        {
            ParseNamedBindings(typeOnly);
            hasBindings = true;
        }
        if (!hasDefault && !hasBindings)
            Fail();
        if (typeOnly && hasDefault && hasBindings)
            Reject(DiagnosticCode.ImportTypeDefaultAndNamed, typeKeyword, typeKeyword + 4);
        ExpectKeyword(Keyword.From);
        ExpectSpecifier();
        ParseImportAttributes();
        ParseSemicolon();
        if (typeOnly && _ok)
        {
            // Keep the default-and-named diagnostic: the import is wrong whatever we erase.
            _edits.Truncate(snapshot.Edits);
            EraseStatement(start, PrevEnd, prevEnd);
        }
    }

    /// <summary><c>import x = require("m")</c> or <c>import x = A.B</c>, from <c>=</c>.</summary>
    private void ParseImportEquals(int start, int prevEnd, int importKeyword, bool typeOnly, in Snapshot snapshot)
    {
        int equalsEnd = End;
        Next();
        if (K == Keyword.Require && Peek().Kind == TokenKind.OpenParen)
        {
            Next();
            Next();
            Expect(TokenKind.StringLiteral);
            Expect(TokenKind.CloseParen);
        }
        else
        {
            ParseEntityName(allowPrivate: false);
        }
        ParseSemicolon();
        if (!_ok)
            return;
        if (typeOnly)
        {
            DropSince(snapshot);
            EraseStatement(start, PrevEnd, prevEnd);
        }
        else
        {
            Reject(DiagnosticCode.ImportEquals, importKeyword, equalsEnd);
        }
    }

    /// <summary><c>with { type: "json" }</c> or the older <c>assert { ... }</c>.</summary>
    private void ParseImportAttributes()
    {
        if (K is Keyword.With || (_lex.Kind == TokenKind.Identifier && _src[Start..End] is "assert"))
        {
            if (LineBreakBefore && K != Keyword.With)
                return;
            Next();
            ParseObjectLiteral();
        }
    }

    /// <summary>
    /// <c>{ a, type b, c as d }</c> in an import or export. Inline <c>type</c> specifiers are
    /// erased with their trailing comma, unless the whole declaration is type-only.
    /// </summary>
    private void ParseNamedBindings(bool typeOnly)
    {
        Expect(TokenKind.OpenBrace);
        while (_ok && T != TokenKind.CloseBrace)
        {
            int specStart = Start;
            bool isType = ParseSpecifier();
            int specEnd = PrevEnd;
            if (T != TokenKind.CloseBrace)
            {
                Expect(TokenKind.Comma);
                specEnd = PrevEnd;
            }
            if (isType && !typeOnly)
                Erase(specStart, specEnd);
        }
        Expect(TokenKind.CloseBrace);
    }

    /// <summary>
    /// One import or export specifier. Returns whether it is type-only. TypeScript:
    /// <c>parseImportOrExportSpecifier</c>, including its <c>type as as x</c> cases.
    /// </summary>
    private bool ParseSpecifier()
    {
        if (K == Keyword.Type)
        {
            Next();
            if (K == Keyword.As)
            {
                Next();
                if (K == Keyword.As)
                {
                    Next();
                    if (IsIdentifierName())
                    {
                        // { type as as x }
                        Next();
                        return true;
                    }
                    // { type as as }
                    return false;
                }
                if (IsIdentifierName() || T == TokenKind.StringLiteral)
                {
                    // { type as x }
                    Next();
                    return false;
                }
                // { type as }
                return true;
            }
            if (IsIdentifierName() || T == TokenKind.StringLiteral)
            {
                // { type x }, { type x as y }
                Next();
                if (K == Keyword.As)
                {
                    Next();
                    ParseModuleExportName();
                }
                return true;
            }
            // { type }
            return false;
        }

        ParseModuleExportName();
        if (K == Keyword.As)
        {
            Next();
            ParseModuleExportName();
        }
        return false;
    }

    private void ExpectSpecifier()
    {
        if (T == TokenKind.StringLiteral)
            RewriteSpecifier(Start, End);
        Expect(TokenKind.StringLiteral);
    }

    private void ParseModuleExportName()
    {
        if (T == TokenKind.StringLiteral)
            Next();
        else
            ExpectIdentifierName();
    }

    /// <summary>TypeScript: <c>parseExportDeclaration</c>, <c>parseExportAssignment</c> and exported declarations.</summary>
    private void ParseExport(int start, int prevEnd)
    {
        var snapshot = Save();
        int exportKeyword = Start;
        Next();

        if (T == TokenKind.Equals)
        {
            // export = x;
            int equalsEnd = End;
            Next();
            ParseAssignmentAllowIn();
            ParseSemicolon();
            Reject(DiagnosticCode.ExportEquals, exportKeyword, equalsEnd);
            return;
        }
        if (K == Keyword.As && IsKeywordOnSameLine(Peek(), Keyword.Namespace))
        {
            // export as namespace N;  (UMD global, declaration-only)
            Next();
            Next();
            ExpectIdentifier();
            ParseSemicolon();
            DropSince(snapshot);
            EraseStatement(start, PrevEnd, prevEnd);
            return;
        }
        if (K == Keyword.Default)
        {
            Next();
            ParseExportDefault(start, prevEnd);
            return;
        }
        if (K == Keyword.Type && Peek().Kind is TokenKind.OpenBrace or TokenKind.Asterisk)
        {
            // export type { A } [from "m"];  export type * [as ns] from "m";
            Next();
            ParseExportClause(typeOnly: true);
            if (_ok)
            {
                DropSince(snapshot);
                EraseStatement(start, PrevEnd, prevEnd);
            }
            return;
        }
        if (T is TokenKind.Asterisk or TokenKind.OpenBrace)
        {
            ParseExportClause(typeOnly: false);
            return;
        }
        if (K == Keyword.Import)
        {
            ParseImport(start, prevEnd);
            return;
        }
        if (T == TokenKind.At || (T == TokenKind.Identifier && IsExportableDeclaration()))
        {
            ParseStatementAt(start, prevEnd);
            return;
        }
        Fail();
    }

    private bool IsExportableDeclaration()
    {
        switch (K)
        {
            case Keyword.Var:
            case Keyword.Let:
            case Keyword.Const:
            case Keyword.Function:
            case Keyword.Class:
            case Keyword.Enum:
                return true;
            case Keyword.Async:
                return IsKeywordOnSameLine(Peek(), Keyword.Function);
            case Keyword.Using:
                return IsIdentifierOnSameLine(Peek());
            case Keyword.Interface:
            case Keyword.Type:
                return IsIdentifierOnSameLine(Peek());
            case Keyword.Declare:
                return IsDeclareStart(Peek());
            case Keyword.Namespace:
            case Keyword.Module:
            {
                var next = Peek();
                return !IsLineBreak(next) && (IsIdentifierToken(next) || next.Kind == TokenKind.StringLiteral);
            }
            case Keyword.Abstract:
                return IsKeywordOnSameLine(Peek(), Keyword.Class);
            default:
                return false;
        }
    }

    /// <summary><c>* [as ns] from "m"</c> or <c>{ ... } [from "m"]</c>, then <c>;</c>.</summary>
    private void ParseExportClause(bool typeOnly)
    {
        if (T == TokenKind.Asterisk)
        {
            Next();
            if (K == Keyword.As)
            {
                Next();
                ParseModuleExportName();
            }
            ExpectKeyword(Keyword.From);
            ExpectSpecifier();
            ParseImportAttributes();
        }
        else
        {
            ParseNamedBindings(typeOnly);
            if (K == Keyword.From)
            {
                Next();
                ExpectSpecifier();
                ParseImportAttributes();
            }
        }
        ParseSemicolon();
    }

    private void ParseExportDefault(int start, int prevEnd)
    {
        switch (K)
        {
            case Keyword.Function:
                ParseFunctionDeclaration(isAsync: false, start, prevEnd);
                return;
            case Keyword.Async when IsKeywordOnSameLine(Peek(), Keyword.Function):
                Next();
                ParseFunctionDeclaration(isAsync: true, start, prevEnd);
                return;
            case Keyword.Class:
                ParseClass(declaration: true, start, prevEnd);
                return;
            case Keyword.Abstract when IsKeywordOnSameLine(Peek(), Keyword.Class):
                ParseAbstractClass();
                return;
            case Keyword.Interface when IsIdentifierOnSameLine(Peek()):
                ParseInterface(start, prevEnd);
                return;
        }
        if (T == TokenKind.At)
        {
            ParseDecorators();
            if (K == Keyword.Abstract)
                ParseAbstractClass();
            else if (K == Keyword.Class)
                ParseClass(declaration: true, start, prevEnd);
            else
                Fail();
            return;
        }
        ParseAssignmentAllowIn();
        ParseSemicolon();
    }
}
