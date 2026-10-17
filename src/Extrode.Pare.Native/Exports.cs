using System.Runtime.InteropServices;
using System.Text;

namespace Extrode.Pare.Native;

/// <summary><c>pare_result</c> in pare.h.</summary>
[StructLayout(LayoutKind.Sequential)]
internal unsafe struct NativeResult
{
    public byte* Output;
    public nuint OutputLength;
    public NativeDiagnostic* Diagnostics;
    public nuint DiagnosticCount;
}

/// <summary>
/// The C entry points declared in include/pare.h. No exception leaves these: a C caller cannot
/// catch one, so anything unexpected is <c>PARE_INTERNAL</c> with nothing allocated.
/// </summary>
internal static unsafe class Exports
{
    public const uint AbiVersion = 1;

    [UnmanagedCallersOnly(EntryPoint = "pare_strip")]
    public static int Strip(byte* source, nuint length, uint options, NativeResult* result)
    {
        if (result == null)
            return NativeStrip.Invalid;
        *result = default;
        if ((source == null && length != 0) || length > (nuint)Array.MaxLength)
            return NativeStrip.Invalid;

        byte* output = null;
        NativeDiagnostic* diagnostics = null;
        try
        {
            output = (byte*)NativeMemory.Alloc(length + 1);
            var found = new List<NativeDiagnostic>();
            int status = NativeStrip.Strip(new ReadOnlySpan<byte>(source, (int)length), options,
                new Span<byte>(output, (int)length), out int written, found);
            if (status == NativeStrip.Ok)
            {
                output[written] = 0;
                *result = new NativeResult { Output = output, OutputLength = (nuint)written };
                return status;
            }
            NativeMemory.Free(output);
            output = null;
            if (status == NativeStrip.Rejected && found.Count > 0)
            {
                diagnostics = (NativeDiagnostic*)NativeMemory.Alloc((nuint)found.Count, (nuint)sizeof(NativeDiagnostic));
                CollectionsMarshal.AsSpan(found).CopyTo(new Span<NativeDiagnostic>(diagnostics, found.Count));
                *result = new NativeResult { Diagnostics = diagnostics, DiagnosticCount = (nuint)found.Count };
            }
            return status;
        }
        catch
        {
            NativeMemory.Free(output);
            NativeMemory.Free(diagnostics);
            *result = default;
            return NativeStrip.Internal;
        }
    }

    [UnmanagedCallersOnly(EntryPoint = "pare_free")]
    public static void Free(NativeResult* result)
    {
        if (result == null)
            return;
        NativeMemory.Free(result->Output);
        NativeMemory.Free(result->Diagnostics);
        *result = default;
    }

    [UnmanagedCallersOnly(EntryPoint = "pare_message")]
    public static byte* Message(int code) => Strings.Message(code);

    [UnmanagedCallersOnly(EntryPoint = "pare_hint")]
    public static byte* Hint(int code) => Strings.Hint(code);

    [UnmanagedCallersOnly(EntryPoint = "pare_version")]
    public static byte* Version() => Strings.Version;

    [UnmanagedCallersOnly(EntryPoint = "pare_abi_version")]
    public static uint Abi() => AbiVersion;
}

/// <summary>
/// NUL-terminated UTF-8 copies of <see cref="DiagnosticMessages"/>, made once by the static
/// constructor (which the runtime runs once, under a lock) and never freed.
/// </summary>
internal static unsafe class Strings
{
    private static readonly nint[] Messages;
    private static readonly nint[] Hints;
    private static readonly nint UnknownMessage;
    private static readonly nint UnknownHint;
    public static readonly byte* Version;

    static Strings()
    {
        var codes = Enum.GetValues<DiagnosticCode>();
        int size = 0;
        foreach (var c in codes)
            size = Math.Max(size, (int)c + 1);
        Messages = new nint[size];
        Hints = new nint[size];
        foreach (var c in codes)
        {
            Messages[(int)c] = (nint)Copy(DiagnosticMessages.Message(c));
            Hints[(int)c] = (nint)Copy(DiagnosticMessages.Hint(c));
        }
        UnknownMessage = (nint)Copy(DiagnosticMessages.Message(0));
        UnknownHint = (nint)Copy(DiagnosticMessages.Hint(0));
        Version = Copy(typeof(TypeStripper).Assembly.GetName().Version?.ToString(3) ?? "0.0.0");
    }

    public static byte* Message(int code) => (byte*)Lookup(Messages, code, UnknownMessage);

    public static byte* Hint(int code) => (byte*)Lookup(Hints, code, UnknownHint);

    // Checked before any cast: DiagnosticCode is a ushort, so 65537 would wrap to SyntaxError.
    private static nint Lookup(nint[] table, int code, nint unknown) =>
        code >= 0 && code < table.Length && table[code] != 0 ? table[code] : unknown;

    private static byte* Copy(string s)
    {
        int n = Encoding.UTF8.GetByteCount(s);
        byte* p = (byte*)NativeMemory.Alloc((nuint)n + 1);
        Encoding.UTF8.GetBytes(s, new Span<byte>(p, n));
        p[n] = 0;
        return p;
    }
}
