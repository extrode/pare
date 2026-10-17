using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace Extrode.Pare.Integration;

/// <summary>Publishes libpare for this machine's RID and calls it from C and through P/Invoke.</summary>
public sealed class NativeLibraryTests(NativeLibraryTests.PublishedLibrary library) : IClassFixture<NativeLibraryTests.PublishedLibrary>
{
    private static readonly string RepoRoot = FindRepoRoot();

    public sealed class PublishedLibrary : IDisposable
    {
        private readonly DirectoryInfo _dir = Directory.CreateTempSubdirectory("pare-native-");
        private nint _handle;

        public string Dir => Path.Combine(_dir.FullName, "lib");

        public string FileName { get; } =
            OperatingSystem.IsWindows() ? "pare.dll" : OperatingSystem.IsMacOS() ? "libpare.dylib" : "libpare.so";

        public PublishedLibrary()
        {
            var (exit, output) = Exec(RepoRoot, "dotnet", "publish", Path.Combine(RepoRoot, "src", "Extrode.Pare.Native"),
                "-c", "Release", "-r", RuntimeInformation.RuntimeIdentifier, "-o", Dir,
                "-nologo", "-nodeReuse:false", "--disable-build-servers", "-clp:NoSummary");
            Assert.True(exit == 0, output);
        }

        public string Temp(string name) => Path.Combine(_dir.FullName, name);

        public nint Handle => _handle != 0 ? _handle : _handle = NativeLibrary.Load(Path.Combine(Dir, FileName));

        public T Export<T>(string name) where T : Delegate =>
            Marshal.GetDelegateForFunctionPointer<T>(NativeLibrary.GetExport(Handle, name));

        // The library stays loaded (NativeAOT libraries cannot be unloaded), and Windows keeps a
        // loaded dll's file locked.
        public void Dispose()
        {
            if (_handle == 0 || !OperatingSystem.IsWindows())
                _dir.Delete(recursive: true);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Result
    {
        public nint Output;
        public nuint OutputLength;
        public nint Diagnostics;
        public nuint DiagnosticCount;
    }

    [StructLayout(LayoutKind.Sequential)]
    private readonly record struct Diag(int Code, int Line, int Column, int Start, int Length);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int StripFn(byte[]? source, nuint length, uint options, ref Result result);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int StripNoResultFn(byte[]? source, nuint length, uint options, nint result);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void FreeFn(ref Result result);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate nint TextFn(int code);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate nint VersionFn();

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate uint AbiFn();

    private int Strip(string source, out Result result, uint options = 0) => Strip(Encoding.UTF8.GetBytes(source), out result, options);

    private int Strip(byte[]? source, out Result result, uint options = 0, nuint? length = null)
    {
        result = default;
        return library.Export<StripFn>("pare_strip")(source, length ?? (nuint)(source?.Length ?? 0), options, ref result);
    }

    private void Free(ref Result result) => library.Export<FreeFn>("pare_free")(ref result);

    private static string Text(nint p) => Marshal.PtrToStringUTF8(p)!;

    private static bool Zeroed(Result r) => r.Output == 0 && r.OutputLength == 0 && r.Diagnostics == 0 && r.DiagnosticCount == 0;

    [Fact]
    public void Strips_to_nul_terminated_utf8_and_free_zeroes_the_result()
    {
        Assert.Equal(0, Strip("let \u00E9: T = '\u00E9';\n", out var r));
        Assert.Equal("let \u00E9    = '\u00E9';\n", Text(r.Output));
        Assert.Equal((nuint)Encoding.UTF8.GetByteCount("let \u00E9    = '\u00E9';\n"), r.OutputLength);
        Assert.Equal(0, Marshal.ReadByte(r.Output, (int)r.OutputLength));
        Assert.Equal(0, r.Diagnostics);

        Free(ref r);
        Assert.True(Zeroed(r));
        Free(ref r);
    }

    [Fact]
    public void Rejection_carries_located_diagnostics()
    {
        Assert.Equal(1, Strip("let \u00E9 = 1;\nenum E { A }\nlet y = <T>z;\n", out var r));
        Assert.Equal(0, r.Output);
        Assert.Equal((nuint)2, r.DiagnosticCount);
        var d0 = Marshal.PtrToStructure<Diag>(r.Diagnostics);
        var d1 = Marshal.PtrToStructure<Diag>(r.Diagnostics + Marshal.SizeOf<Diag>());
        Assert.Equal(new Diag(100, 2, 1, 12, d0.Length), d0);
        Assert.Equal(105, d1.Code);
        Assert.Equal((3, 9), (d1.Line, d1.Column));

        Free(ref r);
        Assert.True(Zeroed(r));
    }

    [Fact]
    public void Bad_arguments_are_invalid_and_allocate_nothing()
    {
        Assert.Equal(2, Strip(null, out var r, length: 1));
        Assert.True(Zeroed(r));
        Assert.Equal(2, Strip("x;", out r, options: 2));
        Assert.True(Zeroed(r));
        Assert.Equal(2, Strip([0x78, 0xFF], out r));
        Assert.True(Zeroed(r));
        Assert.Equal(2, library.Export<StripNoResultFn>("pare_strip")("x;"u8.ToArray(), 2, 0, 0));
        Assert.Equal(2, Strip("x;"u8.ToArray(), out r, length: (nuint)Array.MaxLength + 1));
        Assert.True(Zeroed(r));

        Assert.Equal(0, Strip((byte[]?)null, out r));
        Assert.Equal("", Text(r.Output));
        Free(ref r);
    }

    [Fact]
    public void Deep_nesting_is_a_syntax_error()
    {
        string source = "x = " + new string('(', 200_000) + "a" + new string(')', 200_000) + ";";

        Assert.Equal(1, Strip(source, out var r));
        Assert.Equal(1, Marshal.PtrToStructure<Diag>(r.Diagnostics).Code);
        Free(ref r);
    }

    [Fact]
    public void Messages_version_and_abi()
    {
        var message = library.Export<TextFn>("pare_message");
        var hint = library.Export<TextFn>("pare_hint");

        Assert.Equal("'enum' generates runtime code and cannot be erased", Text(message(100)));
        Assert.Equal("use 'expr as T' instead of '<T>expr'", Text(hint(105)));
        Assert.Equal("syntax error", Text(message(1)));
        foreach (int code in new[] { 0, -1, 2, 65537, 65536 + 100 })
        {
            Assert.Equal("unknown diagnostic", Text(message(code)));
            Assert.Equal("", Text(hint(code)));
        }
        Assert.Equal(typeof(TypeStripper).Assembly.GetName().Version!.ToString(3), Text(library.Export<VersionFn>("pare_version")()));
        Assert.Equal(1u, library.Export<AbiFn>("pare_abi_version")());
    }

    [Fact]
    public void A_c_program_links_and_runs_against_the_header()
    {
        if (OperatingSystem.IsWindows())
            Assert.Skip("smoke.c uses pthreads; the P/Invoke tests cover Windows");
        if (FindOnPath("cc") is null)
            Assert.Skip("no C compiler (cc) on PATH");

        string exe = library.Temp("smoke");
        var (exit, output) = Exec(RepoRoot, "cc", "-std=c11", "-Wall", "-Wextra", "-Werror",
            "-I", Path.Combine(RepoRoot, "src", "Extrode.Pare.Native", "include"),
            Path.Combine(RepoRoot, "tests", "Extrode.Pare.Integration", "native", "smoke.c"),
            "-L", library.Dir, "-lpare", "-Wl,-rpath," + library.Dir, "-lpthread", "-o", exe);
        Assert.True(exit == 0, output);

        (exit, output) = Exec(RepoRoot, exe);
        Assert.True(exit == 0, output);
        Assert.Equal("ok", output.Trim());
    }

    private static string? FindOnPath(string name) =>
        (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator)
            .Select(d => Path.Combine(d, name)).FirstOrDefault(File.Exists);

    private static (int Exit, string Output) Exec(string dir, string fileName, params string[] args)
    {
        var start = new ProcessStartInfo(fileName)
        {
            WorkingDirectory = dir,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (var arg in args)
            start.ArgumentList.Add(arg);
        // A test host started by `dotnet test` carries MSBuild variables meant for itself.
        foreach (var key in start.Environment.Keys.Where(k => k.StartsWith("MSBuild", StringComparison.OrdinalIgnoreCase)).ToList())
            start.Environment.Remove(key);
        using var process = Process.Start(start)!;
        var stderr = process.StandardError.ReadToEndAsync(TestContext.Current.CancellationToken);
        string stdout = process.StandardOutput.ReadToEnd();
        process.WaitForExit();
        return (process.ExitCode, stdout + stderr.Result);
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Pare.slnx")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("Pare.slnx not found above the test output");
    }
}
