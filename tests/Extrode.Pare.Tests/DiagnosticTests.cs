namespace Extrode.Pare.Tests;

public class DiagnosticTests
{
    public static TheoryData<DiagnosticCode> AllCodes() => new(Enum.GetValues<DiagnosticCode>());

    [Theory]
    [MemberData(nameof(AllCodes))]
    public void Every_code_has_a_message_and_a_hint(DiagnosticCode code)
    {
        Assert.NotEqual("unknown diagnostic", DiagnosticMessages.Message(code));
        Assert.NotEqual("", DiagnosticMessages.Hint(code));
    }

    [Fact]
    public void Format_prints_file_line_column_code_message_and_hint()
    {
        var d = new Diagnostic(DiagnosticCode.Enum, 4, 4);
        Assert.Equal(
            "a.ts:2:2: error PARE100: 'enum' generates runtime code and cannot be erased " +
            "(use a 'const' object with 'as const' instead of 'enum')",
            DiagnosticFormatter.Format("a.ts", "x;\n enum E {}", d));
    }
}
