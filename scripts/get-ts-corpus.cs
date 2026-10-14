// Fetches the TypeScript test corpus into .corpus/TypeScript at a pinned commit, without npm.
// Sparse, shallow and blob-filtered, so only the files below are downloaded. Since the Go port
// (TypeScript 7) moved into microsoft/TypeScript, the corpus lives under tsc/testdata/:
//   tests/cases/{conformance,compiler}                   the test inputs
//   baselines/reference/{conformance,compiler}/*.errors.txt  which inputs are expected to fail
//
//   dotnet run scripts/get-ts-corpus.cs [--commit <sha>]
//
// Re-running with the same commit is a no-op. Requires git.

using System.Diagnostics;

const string Repo = "https://github.com/microsoft/TypeScript.git";
// microsoft/TypeScript main as of 2026-10-14. Bump deliberately; corpus pass rates are per commit.
string commit = "9eedcb0dd7257f8a1a175adbef28f3b6d3492d7a";

for (int i = 0; i < args.Length; i++)
{
    if (args[i] == "--commit" && i + 1 < args.Length)
        commit = args[++i];
    else
    {
        Console.Error.WriteLine("usage: dotnet run scripts/get-ts-corpus.cs [--commit <sha>]");
        return 2;
    }
}

string? root = Directory.GetCurrentDirectory();
while (root is not null && !File.Exists(Path.Combine(root, "Pare.slnx")))
    root = Path.GetDirectoryName(root);
if (root is null)
{
    Console.Error.WriteLine("run from inside the Pare repository");
    return 2;
}
string dest = Path.Combine(root, ".corpus", "TypeScript");
Directory.CreateDirectory(dest);

if (!Directory.Exists(Path.Combine(dest, ".git")))
{
    Git("init", "-q");
    Git("remote", "add", "origin", Repo);
}

string? head = TryGit("rev-parse", "HEAD");
if (head == commit)
{
    Console.WriteLine($"corpus already at {commit}: {dest}");
    return 0;
}

Git("sparse-checkout", "set", "--no-cone",
    "/tsc/testdata/tests/cases/conformance/",
    "/tsc/testdata/tests/cases/compiler/",
    "/tsc/testdata/baselines/reference/conformance/*.errors.txt",
    "/tsc/testdata/baselines/reference/compiler/*.errors.txt");
Git("fetch", "-q", "--depth", "1", "--filter=blob:none", "origin", commit);
Git("-c", "advice.detachedHead=false", "checkout", "-q", "FETCH_HEAD");
Console.WriteLine($"corpus at {commit}: {dest}");
return 0;

void Git(params string[] gitArgs)
{
    if (Run(gitArgs, out string output) != 0)
    {
        Console.Error.WriteLine($"git {string.Join(' ', gitArgs)} failed:\n{output}");
        Environment.Exit(2);
    }
}

string? TryGit(params string[] gitArgs) => Run(gitArgs, out string output) == 0 ? output.Trim() : null;

int Run(string[] gitArgs, out string output)
{
    var psi = new ProcessStartInfo("git") { WorkingDirectory = dest, RedirectStandardOutput = true, RedirectStandardError = true };
    foreach (var a in gitArgs)
        psi.ArgumentList.Add(a);
    using var p = Process.Start(psi)!;
    var stderr = p.StandardError.ReadToEndAsync();
    output = p.StandardOutput.ReadToEnd();
    p.WaitForExit();
    output += stderr.Result;
    return p.ExitCode;
}
