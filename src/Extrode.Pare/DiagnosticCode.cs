namespace Extrode.Pare;

/// <summary>
/// Why a file was rejected. Every value has a message and a fix hint in
/// <see cref="DiagnosticMessages"/>. Values are stable: they print as <c>PARE{value:000}</c>.
/// </summary>
public enum DiagnosticCode : ushort
{
    /// <summary>The input is not valid TypeScript at this point.</summary>
    SyntaxError = 1,
    Enum = 100,
    NamespaceWithValues = 101,
    ParameterProperty = 102,
    ImportEquals = 103,
    ExportEquals = 104,
    AngleBracketAssertion = 105,
    ParameterDecorator = 106,
    ImportTypeDefaultAndNamed = 107,
}
