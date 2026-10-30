using System.Diagnostics;
using System.IO.Compression;

namespace Extrode.Pare.Integration;

/// <summary>Packs the framework-dependent `Extrode.Pare.Cli` that release.yml publishes, installs it and runs it.</summary>
public sealed class CliToolPackageTests : IDisposable
{
    private const string Version = "0.0.1";

    private static readonly string RepoRoot = FindRepoRoot();

    private readonly DirectoryInfo _dir = Directory.CreateTempSubdirectory("pare-tool-");

    public void Dispose() => _dir.Delete(recursive: true);

    private string P(string relative) => Path.Combine(_dir.FullName, relative);

    private string Pack()
    {
        var (exit, output) = Exec(RepoRoot, "dotnet", "pack", Path.Combine(RepoRoot, "src", "Extrode.Pare.Cli"),
            "-c", "Release", "-o", P("feed"), "-p:PareCliFrameworkDependent=true", $"-p:Version={Version}",
            "-nologo", "-nodeReuse:false", "--disable-build-servers", "-clp:NoSummary");
        Assert.True(exit == 0, output);
        return P("feed");
    }

    [Fact]
    public void Release_pack_makes_one_framework_dependent_package()
    {
        string feed = Pack();

        Assert.Equal([$"Extrode.Pare.Cli.{Version}.nupkg"], Directory.GetFiles(feed).Select(Path.GetFileName));

        using var zip = ZipFile.OpenRead(Path.Combine(feed, $"Extrode.Pare.Cli.{Version}.nupkg"));
        var entries = zip.Entries.Select(e => e.FullName).ToList();
        Assert.Contains("tools/net10.0/any/pare.dll", entries);
        Assert.DoesNotContain(entries, e => e.EndsWith("/pare.exe") || e.EndsWith("/pare") && !e.EndsWith("/any/pare"));

        using var settings = new StreamReader(zip.GetEntry("tools/net10.0/any/DotnetToolSettings.xml")!.Open());
        string xml = settings.ReadToEnd();
        Assert.Contains("""<Command Name="pare" EntryPoint="pare.dll" Runner="dotnet" />""", xml);
        Assert.DoesNotContain("RuntimeIdentifierPackage", xml);
    }

    [Fact]
    public void Release_workflow_packs_the_cli_with_the_flag()
    {
        string workflow = File.ReadAllText(Path.Combine(RepoRoot, ".github", "workflows", "release.yml"));

        Assert.Contains("dotnet pack src/Extrode.Pare.Cli -c Release -o dist/packages -p:PareCliFrameworkDependent=true", workflow);
    }

    [Fact]
    public void Installed_tool_strips_a_file()
    {
        string feed = Pack();
        string tools = P("tools");
        var (installExit, installOutput) = Exec(_dir.FullName, "dotnet", "tool", "install", "Extrode.Pare.Cli",
            "--tool-path", tools, "--add-source", feed, "--version", Version, "--ignore-failed-sources");
        Assert.True(installExit == 0, installOutput);

        string pare = Path.Combine(tools, OperatingSystem.IsWindows() ? "pare.exe" : "pare");
        Directory.CreateDirectory(P("src"));
        File.WriteAllText(P("src/a.ts"), "const n: number = 1 as const;\n");

        var (versionExit, versionOutput) = Exec(_dir.FullName, pare, "--version");
        Assert.True(versionExit == 0, versionOutput);
        Assert.Equal(Version, versionOutput.Trim());

        var (exit, output) = Exec(_dir.FullName, pare, "src", "--outdir", "js");
        Assert.True(exit == 0, output);
        Assert.Equal("const n         = 1         ;\n", File.ReadAllText(P("js/a.js")));
    }

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
