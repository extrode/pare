namespace Extrode.Pare;

/// <summary>Static text for each <see cref="DiagnosticCode"/>. No allocation on lookup.</summary>
public static class DiagnosticMessages
{
    public static string Message(DiagnosticCode code) => code switch
    {
        DiagnosticCode.SyntaxError => "syntax error",
        DiagnosticCode.Enum => "'enum' generates runtime code and cannot be erased",
        DiagnosticCode.NamespaceWithValues => "namespace containing values generates runtime code",
        DiagnosticCode.ParameterProperty => "parameter property generates runtime code",
        DiagnosticCode.ImportEquals => "'import x = ...' generates runtime code",
        DiagnosticCode.ExportEquals => "'export =' is CommonJS-only emit",
        DiagnosticCode.AngleBracketAssertion => "angle-bracket type assertion is not supported",
        DiagnosticCode.ParameterDecorator => "parameter decorators need legacy experimentalDecorators",
        DiagnosticCode.ImportTypeDefaultAndNamed => "a type-only import cannot have both a default and named bindings",
        _ => "unknown diagnostic",
    };

    public static string Hint(DiagnosticCode code) => code switch
    {
        DiagnosticCode.SyntaxError => "check the syntax at this position",
        DiagnosticCode.Enum => "use a 'const' object with 'as const' instead of 'enum'",
        DiagnosticCode.NamespaceWithValues => "use an ES module instead of a namespace",
        DiagnosticCode.ParameterProperty => "declare the field in the class body and assign it in the constructor",
        DiagnosticCode.ImportEquals => "use an ES 'import' declaration",
        DiagnosticCode.ExportEquals => "use 'export default' instead",
        DiagnosticCode.AngleBracketAssertion => "use 'expr as T' instead of '<T>expr'",
        DiagnosticCode.ParameterDecorator => "use TC39 decorators on the class or member instead",
        DiagnosticCode.ImportTypeDefaultAndNamed => "split it into two 'import type' declarations",
        _ => "",
    };
}
