// Fetches the TypeScript 7 native compiler into .tools/typescript, without npm. TypeScript 7 ships
// the Go port (formerly tsgo, `@typescript/native-preview`) as `tsc` in one npm package per
// platform, `@typescript/typescript-<os>-<cpu>`, holding lib/tsc and the lib/*.d.ts files it
// needs beside it. This downloads that tarball from the npm registry, checks its sha512 against
// the registry's integrity field, and extracts lib/.
//
//   dotnet run scripts/get-tsgo.cs [--version <x.y.z>]
//
// Then: .tools/typescript/tsc --noEmit -p tsconfig.json, or PareTsgoPath=.tools/typescript/tsc.
// Re-running with the same version is a no-op.

using System.Formats.Tar;
using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;

// Latest stable as of 2026-10-15. Bump deliberately; the differential test compares against it.
string version = "7.0.2";

for (int i = 0; i < args.Length; i++)
{
    if (args[i] == "--version" && i + 1 < args.Length)
        version = args[++i];
    else
    {
        Console.Error.WriteLine("usage: dotnet run scripts/get-tsgo.cs [--version <x.y.z>]");
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

string os = OperatingSystem.IsWindows() ? "win32" : OperatingSystem.IsMacOS() ? "darwin" : OperatingSystem.IsLinux() ? "linux" : "";
string cpu = RuntimeInformation.OSArchitecture switch
{
    Architecture.X64 => "x64",
    Architecture.Arm64 => "arm64",
    _ => "",
};
if (os == "" || cpu == "")
{
    Console.Error.WriteLine($"no TypeScript package for {RuntimeInformation.OSDescription} {RuntimeInformation.OSArchitecture}");
    return 2;
}

string dest = Path.Combine(root, ".tools", "typescript");
string exe = Path.Combine(dest, OperatingSystem.IsWindows() ? "tsc.exe" : "tsc");
string stamp = Path.Combine(dest, "version.txt");
if (File.Exists(exe) && File.Exists(stamp) && File.ReadAllText(stamp).Trim() == version)
{
    Console.WriteLine($"tsc {version} already at {exe}");
    return 0;
}

string package = $"@typescript/typescript-{os}-{cpu}";
using var http = new HttpClient();
string metaUrl = $"https://registry.npmjs.org/{package.Replace("/", "%2F")}/{version}";
using var metaResponse = await http.GetAsync(metaUrl);
if (!metaResponse.IsSuccessStatusCode)
{
    Console.Error.WriteLine($"{package}@{version}: {(int)metaResponse.StatusCode} from {metaUrl}");
    return 2;
}
using var meta = JsonDocument.Parse(await metaResponse.Content.ReadAsStringAsync());
var dist = meta.RootElement.GetProperty("dist");
string tarball = dist.GetProperty("tarball").GetString()!;
string integrity = dist.GetProperty("integrity").GetString()!;
if (!integrity.StartsWith("sha512-", StringComparison.Ordinal))
{
    Console.Error.WriteLine($"{package}@{version}: unexpected integrity {integrity}");
    return 2;
}

byte[] bytes = await http.GetByteArrayAsync(tarball);
if (Convert.ToBase64String(SHA512.HashData(bytes)) != integrity["sha512-".Length..])
{
    Console.Error.WriteLine($"{tarball}: sha512 does not match the registry's integrity");
    return 2;
}

if (Directory.Exists(dest))
    Directory.Delete(dest, recursive: true);
Directory.CreateDirectory(dest);
using (var gzip = new GZipStream(new MemoryStream(bytes), CompressionMode.Decompress))
using (var tar = new TarReader(gzip))
{
    while (tar.GetNextEntry() is { } entry)
    {
        const string Prefix = "package/lib/";
        if (entry.EntryType is not (TarEntryType.RegularFile or TarEntryType.V7RegularFile) ||
            !entry.Name.StartsWith(Prefix, StringComparison.Ordinal))
            continue;
        string relative = entry.Name[Prefix.Length..];
        string target = Path.GetFullPath(Path.Combine(dest, relative));
        if (!target.StartsWith(dest + Path.DirectorySeparatorChar, StringComparison.Ordinal))
        {
            Console.Error.WriteLine($"{tarball}: entry outside lib/: {entry.Name}");
            return 2;
        }
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        entry.ExtractToFile(target, overwrite: true);
    }
}
if (!File.Exists(exe))
{
    Console.Error.WriteLine($"{tarball}: no lib/{Path.GetFileName(exe)} in the package");
    return 2;
}
if (!OperatingSystem.IsWindows())
    File.SetUnixFileMode(exe, File.GetUnixFileMode(exe) | UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute);
File.WriteAllText(stamp, version + "\n");
Console.WriteLine($"tsc {version} at {exe}");
return 0;
