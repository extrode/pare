namespace Extrode.Pare;

/// <summary>Receives diagnostics as they are found, in source order.</summary>
public interface IDiagnosticSink
{
    void Report(in Diagnostic diagnostic);
}
