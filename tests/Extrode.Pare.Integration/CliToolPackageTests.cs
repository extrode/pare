using System.Diagnostics;
using System.IO.Compression;
using System.Reflection;

namespace Extrode.Pare.Integration;

/// <summary>Packs the framework-dependent `Extrode.Pare.Cli` that release.yml publishes, installs it and runs it.</summary>
[Collection("cli-pack")]
public sealed class CliToolPackageTests : IDisposable
{
    // The repo version, not an override: a different Version restamps the Extrode.Pare build other tests share.
    private static readonly string Version = typeof(CliToolPackageTests).Assembly.GetName().Version!.ToString(3);

    private static readonly string RepoRoot = FindRepoRoot();

    private readonly DirectoryInfo _dir = Directory.CreateTempSubdirectory("pare-tool-");

    public void Dispose() => _dir.Delete(recursive: true);

    private string P(string relative) => Path.Combine(_dir.FullName, relative);

    private string Pack()
    {
        var (exit, output) = Exec(RepoRoot, "dotnet", "pack", Path.Combine(RepoRoot, "src", "Extrode.Pare.Cli"),
            "-c", "Release", "-o", P("feed"), "-p:PareCliFrameworkDependent=true",
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

        using var nuspec = new StreamReader(zip.GetEntry("Extrode.Pare.Cli.nuspec")!.Open());
        string spec = nuspec.ReadToEnd();
        Assert.DoesNotContain("NativeAOT", spec, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(".NET 10 runtime", spec);
    }

    [Fact]
    public void Release_workflow_pushes_to_github_packages_with_write_permission()
    {
        string workflow = File.ReadAllText(Path.Combine(RepoRoot, ".github", "workflows", "release.yml"));

        Assert.Contains("https://nuget.pkg.github.com/${{ github.repository_owner }}/index.json", workflow);
        Assert.Contains("\n      packages: write", workflow);
    }

    [Fact]
    public void Release_waits_for_the_whole_ci_suite()
    {
        string workflows = Path.Combine(RepoRoot, ".github", "workflows");
        string release = File.ReadAllText(Path.Combine(workflows, "release.yml"));

        Assert.Contains("uses: ./.github/workflows/ci.yml", release);
        Assert.Contains("\n    needs: ci\n", release);
        Assert.Contains("workflow_call:", File.ReadAllText(Path.Combine(workflows, "ci.yml")));
    }

    [Fact]
    public void Workflow_actions_are_pinned_to_a_commit()
    {
        foreach (string file in Directory.GetFiles(Path.Combine(RepoRoot, ".github", "workflows"), "*.yml"))
        {
            foreach (string line in File.ReadAllLines(file).Where(l => l.TrimStart().TrimStart('-').TrimStart().StartsWith("uses: ", StringComparison.Ordinal)))
            {
                string target = line[(line.IndexOf("uses: ", StringComparison.Ordinal) + 6)..].Split(' ')[0];
                if (target.StartsWith("./", StringComparison.Ordinal))
                    continue;
                string reference = target[(target.IndexOf('@') + 1)..];
                Assert.True(reference.Length == 40 && reference.All(char.IsAsciiHexDigitLower), $"{Path.GetFileName(file)}: {line.Trim()}");
            }
        }
    }

    [Fact]
    public void Readmes_do_not_say_the_cli_package_is_missing_from_nuget()
    {
        foreach (string file in new[] { "README.md", "docs/01-getting-started/README.md", "docs/01-getting-started/first-static-site.md" })
            Assert.DoesNotContain("not on nuget.org", File.ReadAllText(Path.Combine(RepoRoot, file)));
    }

    [Fact]
    public void Classes_that_pack_the_cli_run_one_at_a_time()
    {
        // Both pack src/Extrode.Pare.Cli in place in Release, with different settings.
        foreach (var type in new[] { typeof(CliToolPackageTests), typeof(BuildIntegrationTests) })
            Assert.Equal("cli-pack", type.GetCustomAttribute<CollectionAttribute>()?.Name);
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
        // Only the local feed: a published Extrode.Pare.Cli of the same version must not win.
        File.WriteAllText(P("nuget.config"),
            $"""<configuration><packageSources><clear /><add key="local" value="{feed}" /></packageSources></configuration>""");
        var (installExit, installOutput) = Exec(_dir.FullName, "dotnet", "tool", "install", "Extrode.Pare.Cli",
            "--tool-path", tools, "--configfile", P("nuget.config"), "--version", Version);
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
