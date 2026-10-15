// Fetches real-world TypeScript projects into .corpus/realworld/<name> at pinned commits, for the
// benchmarks and the cross-tool comparison (tests/Extrode.Pare.Bench). Shallow, blob-filtered and,
// where a project is large, sparse. Re-running with the same commits is a no-op. Requires git.
//
//   dotnet run scripts/get-bench-corpus.cs

using System.Diagnostics;

// Bump deliberately: benchmark numbers and comparison counts are per commit.
(string Name, string Repo, string Commit, string[] Sparse)[] projects =
[
    ("typescript", "https://github.com/microsoft/TypeScript.git", "c63de15a992d37f0d6cec03ac7631872838602cb", ["/src/compiler/"]), // v5.9.3
    ("vue", "https://github.com/vuejs/core.git", "bf5f0dd877dd3c23ce85c2cbc1dd771567e84c8c", ["/packages/"]),
    ("hono", "https://github.com/honojs/hono.git", "588ec13fa68ea44fed1b5e1b503341db2bf09d70", ["/src/"]),
    ("std", "https://github.com/denoland/std.git", "f834d0223364361169314833e3c7a8f62ce11d58", []),
    ("svelte", "https://github.com/sveltejs/svelte.git", "79e7c3bd346bec0c683ef3bde4334778e5ef94e3", ["/packages/"]),
    ("zod", "https://github.com/colinhacks/zod.git", "680d19f7d1e042e55934209caeb79bc843b94f44", ["/packages/"]),
];

if (args.Length > 0)
{
    Console.Error.WriteLine("usage: dotnet run scripts/get-bench-corpus.cs");
    return 2;
}

string? root = Directory.GetCurrentDirectory();
while (root is not null && !File.Exists(Path.Combine(root, "Pare.slnx")))
    root = Path.GetDirectoryName(root);
if (root is null)
{
    Console.Error.WriteLine("run from inside the Pare repository");
    return 2;
}

foreach (var (name, repo, commit, sparse) in projects)
{
    string dest = Path.Combine(root, ".corpus", "realworld", name);
    Directory.CreateDirectory(dest);
    if (!Directory.Exists(Path.Combine(dest, ".git")))
    {
        Git(dest, "init", "-q");
        Git(dest, "remote", "add", "origin", repo);
    }
    if (TryGit(dest, "rev-parse", "HEAD") == commit)
    {
        Console.WriteLine($"{name} already at {commit}");
        continue;
    }
    if (sparse.Length > 0)
        Git(dest, ["sparse-checkout", "set", "--no-cone", .. sparse]);
    Git(dest, "fetch", "-q", "--depth", "1", "--filter=blob:none", "origin", commit);
    Git(dest, "-c", "advice.detachedHead=false", "checkout", "-q", "FETCH_HEAD");
    Console.WriteLine($"{name} at {commit}");
}
return 0;

static void Git(string dir, params string[] gitArgs)
{
    if (Run(dir, gitArgs, out string output) != 0)
    {
        Console.Error.WriteLine($"git {string.Join(' ', gitArgs)} failed in {dir}:\n{output}");
        Environment.Exit(2);
    }
}

static string? TryGit(string dir, params string[] gitArgs) => Run(dir, gitArgs, out string output) == 0 ? output.Trim() : null;

static int Run(string dir, string[] gitArgs, out string output)
{
    var psi = new ProcessStartInfo("git") { WorkingDirectory = dir, RedirectStandardOutput = true, RedirectStandardError = true };
    foreach (var a in gitArgs)
        psi.ArgumentList.Add(a);
    using var p = Process.Start(psi)!;
    var stderr = p.StandardError.ReadToEndAsync();
    output = p.StandardOutput.ReadToEnd();
    p.WaitForExit();
    output += stderr.Result;
    return p.ExitCode;
}
