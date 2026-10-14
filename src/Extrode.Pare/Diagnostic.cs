namespace Extrode.Pare;

/// <summary>
/// One rejected construct. <see cref="Start"/> and <see cref="Length"/> are UTF-16 code unit
/// offsets into the source passed to <see cref="TypeStripper.Strip"/>.
/// </summary>
public readonly record struct Diagnostic(DiagnosticCode Code, int Start, int Length);
