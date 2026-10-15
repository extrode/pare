// Runs Deno std's own test suite against Pare's output: copies .corpus/realworld/std to
// tmp/std-js, strips it in place with the NativeAOT `pare` (which rewrites `./x.ts` imports to
// `./x.js`), maps the `@std/*` imports to the .js files, and runs `deno test` per package on the
// original *_test.ts and on the stripped *_test.js. Prints both results side by side.
// Requires deno, `dotnet run scripts/get-bench-corpus.cs` and a published CLI in tmp/aot.
//
//   dotnet run scripts/run-std-tests.cs [package...]

using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

// Differences between the two runs that are not Pare errors, checked by hand at the pinned commit.
var known = new Dictionary<string, string>
{
    ["csv"] = "a test compares Function.prototype.toString() with Deno's reprint `(n)=>n`; blanking keeps `(n        ) => n`",
    ["expect"] = "not run: Pare rejects 3 files (parameter properties, `<T>x`), so expect.js is missing",
    ["http"] = "serveDir() refuses the TypeScript copy's dot path (.corpus); the .js copy passes",
    ["testing"] = "snapshot tests look for __snapshots__/<test file>.snap, named after the .ts test file",
};

string? root = Directory.GetCurrentDirectory();
while (root is not null && !File.Exists(Path.Combine(root, "Pare.slnx")))
    root = Path.GetDirectoryName(root);
if (root is null)
{
    Console.Error.WriteLine("run from inside the Pare repository");
    return 2;
}
string std = Path.Combine(root, ".corpus", "realworld", "std");
string pare = Path.Combine(root, "tmp", "aot", OperatingSystem.IsWindows() ? "pare.exe" : "pare");
if (!Directory.Exists(std))
{
    Console.Error.WriteLine("std missing: run `dotnet run scripts/get-bench-corpus.cs`");
    return 2;
}
if (!File.Exists(pare))
{
    Console.Error.WriteLine($"{pare} missing: run `dotnet publish src/Extrode.Pare.Cli -c Release -r <rid> -o tmp/aot`");
    return 2;
}

string js = Path.Combine(root, "tmp", "std-js");
if (Directory.Exists(js))
    Directory.Delete(js, recursive: true);
Copy(new DirectoryInfo(std), js);

var watch = Stopwatch.StartNew();
var (pareExit, pareOutput) = Run(pare, [js]);
var rejected = pareOutput.Split('\n').Where(l => l.Contains(": error PARE", StringComparison.Ordinal))
    .Select(l => Path.GetRelativePath(js, l[..l.IndexOf(':')])).Distinct().ToList();
Console.WriteLine($"pare stripped std in {watch.ElapsedMilliseconds} ms (exit {pareExit}); rejected {rejected.Count} files: {string.Join(", ", rejected)}");

// Workspace members' exports, with .ts targets pointed at the stripped .js.
var options = new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true };
var rootConfig = JsonNode.Parse(File.ReadAllText(Path.Combine(js, "deno.json")), documentOptions: options)!.AsObject();
var imports = rootConfig["imports"]?.DeepClone().AsObject() ?? [];
foreach (string config in Directory.EnumerateFiles(js, "deno.json", SearchOption.AllDirectories).Order())
{
    if (Path.GetDirectoryName(config) == js)
        continue;
    var member = JsonNode.Parse(File.ReadAllText(config), documentOptions: options)!.AsObject();
    if (member["name"]?.GetValue<string>() is not { } name || member["exports"] is not { } exports)
        continue;
    string dir = "./" + Path.GetRelativePath(js, Path.GetDirectoryName(config)!).Replace('\\', '/') + "/";
    var entries = exports is JsonObject map
        ? map.Select(e => (e.Key, e.Value!.GetValue<string>()))
        : [(".", exports.GetValue<string>())];
    foreach (var (key, target) in entries)
    {
        string path = dir + target[2..];
        imports[key == "." ? name : name + key[1..]] = path.EndsWith(".ts", StringComparison.Ordinal) ? path[..^3] + ".js" : path;
    }
}
string jsConfig = Path.Combine(js, "pare-std.json");
File.WriteAllText(jsConfig, new JsonObject { ["imports"] = imports, ["unstable"] = rootConfig["unstable"]?.DeepClone() }.ToJsonString());

var packages = args.Length > 0 ? args : Directory.EnumerateDirectories(std).Select(Path.GetFileName).Where(p => p![0] != '.').Order().ToArray();
int same = 0, explained = 0, differ = 0;
Console.WriteLine();
Console.WriteLine("| Package | TypeScript (deno) | Pare output (deno) | |");
Console.WriteLine("|---|---|---|---|");
foreach (string package in packages!)
{
    var tsTests = Tests(Path.Combine(std, package), "_test.ts");
    if (tsTests.Length == 0)
        continue;
    string ts = Summary(Path.Combine(std, "deno.json"), tsTests);
    string jsResult = Summary(jsConfig, Tests(Path.Combine(js, package), "_test.js"));
    string note;
    if (ts == jsResult)
    {
        same++;
        note = "same";
    }
    else if (known.TryGetValue(package, out var why))
    {
        explained++;
        note = why;
    }
    else
    {
        differ++;
        note = "**differs**";
    }
    Console.WriteLine($"| {package} | {ts} | {jsResult} | {note} |");
}
Console.WriteLine();
Console.WriteLine($"{same} packages same, {explained} with known differences, {differ} unexplained.");
return differ == 0 ? 0 : 1;

// Symbolic links (std's fs tests use some, one dangling) are copied as links.
static void Copy(DirectoryInfo from, string to)
{
    Directory.CreateDirectory(to);
    foreach (var entry in from.EnumerateFileSystemInfos())
    {
        string dest = Path.Combine(to, entry.Name);
        if (entry.LinkTarget is { } target)
        {
            if (entry is DirectoryInfo)
                Directory.CreateSymbolicLink(dest, target);
            else
                File.CreateSymbolicLink(dest, target);
        }
        else if (entry is DirectoryInfo dir)
        {
            if (dir.Name != ".git")
                Copy(dir, dest);
        }
        else
        {
            File.Copy(entry.FullName, dest);
        }
    }
}

static string[] Tests(string dir, string suffix) =>
    Directory.Exists(dir) ? [.. Directory.EnumerateFiles(dir, "*" + suffix).Order()] : [];

// "N passed | M failed" from deno's last summary line, without timings.
static string Summary(string config, string[] tests)
{
    if (tests.Length == 0)
        return "no tests";
    var (_, output) = Run("deno", ["test", "-A", "--no-check", "--quiet", "--config", config, .. tests]);
    var line = output.Split('\n').LastOrDefault(l => l.StartsWith("ok |", StringComparison.Ordinal) || l.StartsWith("FAILED |", StringComparison.Ordinal));
    if (line is null)
        return (output.Split('\n').FirstOrDefault(l => l.StartsWith("error:", StringComparison.Ordinal)) ?? "no summary").Trim();
    return Regex.Replace(line[(line.IndexOf('|') + 1)..], @"\s*\(\d[^)]*\)\s*$|\s*\((\d+(\.\d+)?(ms|s|m))\)", "").Trim().Replace(" | ", ", ");
}

static (int Exit, string Output) Run(string file, string[] arguments)
{
    var start = new ProcessStartInfo(file, arguments) { RedirectStandardOutput = true, RedirectStandardError = true };
    start.Environment["NO_COLOR"] = "1";
    using var process = Process.Start(start)!;
    var stderr = process.StandardError.ReadToEndAsync();
    string stdout = process.StandardOutput.ReadToEnd();
    process.WaitForExit();
    return (process.ExitCode, stdout + stderr.Result);
}
