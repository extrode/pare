using System.Buffers;
using System.Text;
using Microsoft.Extensions.Logging;

namespace Extrode.Pare.AspNetCore;

/// <summary>
/// Strips the <c>.ts</c> and <c>.mts</c> sources under a directory (not <c>.d.ts</c>, not
/// <c>node_modules</c>), the same set and output paths as the MSBuild package's defaults, and keeps
/// stripping them as they change. With the build's output root, it also deletes the output of a
/// deleted source: that directory holds only Pare's output.
/// </summary>
internal sealed class TypeScriptWatcher : IDisposable
{
    // Editors save in several steps (truncate, write, rename); one strip after they settle.
    internal static readonly TimeSpan Settle = TimeSpan.FromMilliseconds(50);
    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    private readonly string _contentRoot;
    private readonly string _sourceRoot;
    private readonly string? _outRoot;
    // Output paths keep the source's path from here: the content root for OutDir, the web root for the build's output root.
    private readonly string _outBase;
    private readonly bool _ownsOutput;
    private readonly ILogger _logger;
    private readonly HashSet<string> _pending = new(StringComparer.Ordinal);
    private readonly Lock _lock = new();
    private readonly Timer _timer;
    private FileSystemWatcher? _watcher;

    public TypeScriptWatcher(string contentRoot, string sourceRoot, string? outDir, ILogger logger)
        : this(contentRoot, sourceRoot, string.IsNullOrEmpty(outDir) ? null : Path.GetFullPath(outDir, Path.GetFullPath(contentRoot)), ownsOutput: false, logger)
    {
    }

    private TypeScriptWatcher(string contentRoot, string sourceRoot, string? outRoot, bool ownsOutput, ILogger logger)
    {
        _contentRoot = Path.GetFullPath(contentRoot);
        _sourceRoot = Path.GetFullPath(sourceRoot);
        _outRoot = outRoot;
        _outBase = ownsOutput ? _sourceRoot : _contentRoot;
        _ownsOutput = ownsOutput;
        _logger = logger;
        _timer = new Timer(_ => Flush());
    }

    /// <summary>Writes where the build does in a web project: under <paramref name="outputRoot"/>, keeping each path below <paramref name="sourceRoot"/>.</summary>
    public static TypeScriptWatcher ForBuildOutput(string contentRoot, string sourceRoot, string outputRoot, ILogger logger) =>
        new(contentRoot, sourceRoot, Path.GetFullPath(outputRoot), ownsOutput: true, logger);

    public void Start()
    {
        foreach (string source in Directory.EnumerateFiles(_sourceRoot, "*", SearchOption.AllDirectories))
        {
            if (!IsSource(source))
                continue;
            var target = new FileInfo(OutputPath(source));
            if (!target.Exists || target.LastWriteTimeUtc < File.GetLastWriteTimeUtc(source))
                Strip(source, started: false);
        }

        // No name filter: a new directory's own event is the only sign of the files written into it
        // before Linux starts watching it.
        _watcher = new FileSystemWatcher(_sourceRoot)
        {
            IncludeSubdirectories = true,
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite | NotifyFilters.Size,
        };
        _watcher.Changed += (_, e) => Queue(e.FullPath);
        _watcher.Created += (_, e) => QueueTree(e.FullPath);
        _watcher.Renamed += (_, e) =>
        {
            Queue(e.OldFullPath);
            QueueTree(e.FullPath);
        };
        _watcher.Deleted += (_, e) => Queue(e.FullPath);
        _watcher.EnableRaisingEvents = true;
    }

    internal bool IsSource(string path)
    {
        if (!path.EndsWith(".ts", StringComparison.Ordinal) && !path.EndsWith(".mts", StringComparison.Ordinal))
            return false;
        if (path.EndsWith(".d.ts", StringComparison.Ordinal) || path.EndsWith(".d.mts", StringComparison.Ordinal))
            return false;
        string relative = Path.GetRelativePath(_sourceRoot, path);
        return !relative.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).Contains("node_modules");
    }

    internal string OutputPath(string source)
    {
        string js = Path.ChangeExtension(source, source.EndsWith(".mts", StringComparison.Ordinal) ? ".mjs" : ".js");
        return _outRoot is null ? js : Path.Combine(_outRoot, Path.GetRelativePath(_outBase, js));
    }

    private void Queue(string path)
    {
        if (!IsSource(path))
            return;
        lock (_lock)
        {
            _pending.Add(path);
            _timer.Change(Settle, Timeout.InfiniteTimeSpan);
        }
    }

    // A created or moved-in directory: queue the sources already in it. A strip that finds nothing changed writes nothing.
    private void QueueTree(string path)
    {
        if (!Directory.Exists(path))
        {
            Queue(path);
            return;
        }
        try
        {
            foreach (string file in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
                Queue(file);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // Gone or unreadable again; its own events, if any, follow.
        }
    }

    private void Flush()
    {
        string[] batch;
        lock (_lock)
        {
            batch = [.. _pending];
            _pending.Clear();
        }
        foreach (string source in batch)
            Strip(source, started: true);
    }

    /// <summary>
    /// Strips one source. Logs rejections and I/O errors; leaves the old output in place then. A
    /// missing source removes its output when the watcher owns the output directory.
    /// </summary>
    internal void Strip(string sourcePath, bool started)
    {
        string name = Path.GetRelativePath(_contentRoot, sourcePath);
        string? source = null;
        for (int attempt = 0; source is null; attempt++)
        {
            try
            {
                source = File.ReadAllText(sourcePath);
            }
            catch (Exception e) when (e is FileNotFoundException or DirectoryNotFoundException)
            {
                if (_ownsOutput)
                    Remove(OutputPath(sourcePath));
                return;
            }
            catch (IOException) when (attempt < 3)
            {
                // The editor still holds the file.
                Thread.Sleep(Settle);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                _logger.LogError("{File}: {Message}", name, e.Message);
                return;
            }
        }

        var output = new ArrayBufferWriter<char>(source.Length);
        var sink = new Sink();
        if (!TypeStripper.Strip(source, output, sink, StripOptions.RewriteRelativeImportExtensions))
        {
            foreach (var d in sink.Items)
                _logger.LogError("{Diagnostic}", DiagnosticFormatter.Format(name, source, d));
            return;
        }

        string target = OutputPath(sourcePath);
        byte[] bytes = new byte[Utf8NoBom.GetByteCount(output.WrittenSpan)];
        Utf8NoBom.GetBytes(output.WrittenSpan, bytes);
        bool created = !File.Exists(target);
        try
        {
            if (!created && File.ReadAllBytes(target).AsSpan().SequenceEqual(bytes))
                return;
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.WriteAllBytes(target, bytes);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            _logger.LogError("{File}: {Message}", Path.GetRelativePath(_contentRoot, target), e.Message);
            return;
        }
        // The build registers each output as a static web asset; dotnet watch serves no new one until it builds again.
        if (created && started && _ownsOutput)
            _logger.LogInformation("Stripped {Source} to {Target}; a new file is served after the next build (Ctrl+R in dotnet watch)",
                name, Path.GetRelativePath(_contentRoot, target));
        else
            _logger.LogInformation("Stripped {Source} to {Target}", name, Path.GetRelativePath(_contentRoot, target));
    }

    private void Remove(string target)
    {
        try
        {
            if (!File.Exists(target))
                return;
            File.Delete(target);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            _logger.LogError("{File}: {Message}", Path.GetRelativePath(_contentRoot, target), e.Message);
            return;
        }
        _logger.LogInformation("Removed {Target}", Path.GetRelativePath(_contentRoot, target));
    }

    public void Dispose()
    {
        _watcher?.Dispose();
        _timer.Dispose();
    }

    private sealed class Sink : IDiagnosticSink
    {
        public readonly List<Diagnostic> Items = [];
        public void Report(in Diagnostic diagnostic) => Items.Add(diagnostic);
    }
}
