using System.Buffers;
using System.Runtime.CompilerServices;
using System.Text;
using Microsoft.Extensions.Logging;

namespace Extrode.Pare.AspNetCore;

/// <summary>
/// Strips the <c>.ts</c> and <c>.mts</c> sources under a directory (not <c>.d.ts</c>, not
/// <c>node_modules</c>), the same set and output paths as the MSBuild package's defaults, and keeps
/// stripping them as they change. With the build's output root, it also deletes the output of a
/// deleted source: that directory holds only Pare's output. The build's map may also name sources
/// and their outputs one by one (<c>PareServeAt</c>, anywhere in the project); those are watched
/// in their own directories and written where the map says. Sources of another compiler
/// (<c>PareCompileSource</c>) are watched the same way and handed to their <see cref="IPareCompiler"/>.
/// </summary>
internal sealed class TypeScriptWatcher : IDisposable
{
    // Editors save in several steps (truncate, write, rename); one strip after they settle.
    internal static readonly TimeSpan Settle = TimeSpan.FromMilliseconds(50);
    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);
    private static readonly StringComparer PathComparer =
        OperatingSystem.IsWindows() || OperatingSystem.IsMacOS() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
    // One compile at a time per compiler: every project's watcher shares the app's compilers.
    private static readonly ConditionalWeakTable<IPareCompiler, Lock> CompilerGates = [];

    private readonly string _contentRoot;
    private readonly string _sourceRoot;
    private readonly string? _outRoot;
    // Output paths keep the source's path from here: the content root for OutDir, the web root for the build's output root.
    private readonly string _outBase;
    private readonly bool _ownsOutput;
    private readonly Dictionary<string, string> _mapped;
    private readonly Dictionary<string, CompiledSource> _compiled;
    private readonly ILogger _logger;
    private readonly HashSet<string> _pending = new(StringComparer.Ordinal);
    private readonly Lock _lock = new();
    // Timer callbacks overlap: one flush at a time, so a later save is compiled after an earlier one.
    private readonly Lock _flushLock = new();
    private bool _disposed;
    // Not disposed: a compile still running when the app stops may hold its token.
    private readonly CancellationTokenSource _stopping = new();
    private readonly Timer _timer;
    private readonly List<FileSystemWatcher> _watchers = [];

    public TypeScriptWatcher(string contentRoot, string sourceRoot, string? outDir, ILogger logger)
        : this(contentRoot, sourceRoot, string.IsNullOrEmpty(outDir) ? null : Path.GetFullPath(outDir, Path.GetFullPath(contentRoot)), ownsOutput: false, null, null, logger)
    {
    }

    private TypeScriptWatcher(string contentRoot, string sourceRoot, string? outRoot, bool ownsOutput, IReadOnlyDictionary<string, string>? mapped,
        IReadOnlyDictionary<string, CompiledSource>? compiled, ILogger logger)
    {
        _contentRoot = Path.GetFullPath(contentRoot);
        _sourceRoot = Path.GetFullPath(sourceRoot);
        _outRoot = outRoot;
        _outBase = ownsOutput ? _sourceRoot : _contentRoot;
        _ownsOutput = ownsOutput;
        _mapped = new Dictionary<string, string>(PathComparer);
        foreach (var (source, output) in mapped ?? new Dictionary<string, string>())
            _mapped[Path.GetFullPath(source)] = Path.GetFullPath(output);
        _compiled = new Dictionary<string, CompiledSource>(PathComparer);
        foreach (var (source, entry) in compiled ?? new Dictionary<string, CompiledSource>())
            _compiled[Path.GetFullPath(source)] = entry with { Outputs = [.. entry.Outputs.Select(o => Path.GetFullPath(o))] };
        _logger = logger;
        _timer = new Timer(_ => Flush());
    }

    /// <summary>
    /// Writes where the build does in a web project: each of <paramref name="sources"/> (full source
    /// path to full output path) where it says, any other source under <paramref name="sourceRoot"/>
    /// under <paramref name="outputRoot"/>, keeping its path below <paramref name="sourceRoot"/>, and
    /// each of <paramref name="compiled"/> through its compiler to its outputs.
    /// </summary>
    public static TypeScriptWatcher ForBuildOutput(string contentRoot, string sourceRoot, string outputRoot, ILogger logger,
        IReadOnlyDictionary<string, string>? sources = null, IReadOnlyDictionary<string, CompiledSource>? compiled = null) =>
        new(contentRoot, sourceRoot, Path.GetFullPath(outputRoot), ownsOutput: true, sources, compiled, logger);

    public void Start()
    {
        var initial = new HashSet<string>(_mapped.Keys, PathComparer);
        initial.UnionWith(_compiled.Keys);
        bool hasRoot = Directory.Exists(_sourceRoot);
        if (hasRoot)
            initial.UnionWith(Directory.EnumerateFiles(_sourceRoot, "*", SearchOption.AllDirectories).Where(IsSource));
        // Watching starts first, so a save during the first pass is queued; its flush waits for the pass.
        lock (_flushLock)
        {
            // No name filter: a new directory's own event is the only sign of the files written into it
            // before Linux starts watching it.
            if (hasRoot)
                Watch(_sourceRoot, recursive: true);
            foreach (string directory in _mapped.Keys.Concat(_compiled.Keys).Select(s => Path.GetDirectoryName(s)!).Distinct(PathComparer))
            {
                if (Directory.Exists(directory) && !(hasRoot && IsUnder(_sourceRoot, directory)))
                    Watch(directory, recursive: false);
            }
            foreach (string source in initial)
            {
                bool exists = File.Exists(source);
                var written = exists ? File.GetLastWriteTimeUtc(source) : default;
                if (Outputs(source).Select(o => new FileInfo(o)).Any(target => !exists ? target.Exists : !target.Exists || target.LastWriteTimeUtc < written))
                    Strip(source, started: false);
            }
        }
    }

    private void Watch(string directory, bool recursive)
    {
        var watcher = new FileSystemWatcher(directory)
        {
            IncludeSubdirectories = recursive,
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite | NotifyFilters.Size,
        };
        watcher.Changed += (_, e) => Queue(e.FullPath);
        watcher.Created += (_, e) => QueueTree(e.FullPath);
        watcher.Renamed += (_, e) =>
        {
            QueueGone(e.OldFullPath);
            QueueTree(e.FullPath);
        };
        watcher.Deleted += (_, e) => QueueGone(e.FullPath);
        watcher.EnableRaisingEvents = true;
        _watchers.Add(watcher);
    }

    private static bool IsUnder(string root, string path)
    {
        string relative = Path.GetRelativePath(root, path);
        return relative != ".." && !relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal) && !Path.IsPathRooted(relative);
    }

    internal bool IsSource(string path)
    {
        if (_mapped.ContainsKey(path) || _compiled.ContainsKey(path))
            return true;
        if (!path.EndsWith(".ts", StringComparison.Ordinal) && !path.EndsWith(".mts", StringComparison.Ordinal))
            return false;
        if (path.EndsWith(".d.ts", StringComparison.Ordinal) || path.EndsWith(".d.mts", StringComparison.Ordinal))
            return false;
        if (!IsUnder(_sourceRoot, path))
            return false;
        string relative = Path.GetRelativePath(_sourceRoot, path);
        return !relative.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).Contains("node_modules");
    }

    internal string OutputPath(string source)
    {
        if (_mapped.TryGetValue(source, out string? mapped))
            return mapped;
        string js = Path.ChangeExtension(source, source.EndsWith(".mts", StringComparison.Ordinal) ? ".mjs" : ".js");
        return _outRoot is null ? js : Path.Combine(_outRoot, Path.GetRelativePath(_outBase, js));
    }

    private string[] Outputs(string source) =>
        _compiled.TryGetValue(source, out var compiled) ? compiled.Outputs : [OutputPath(source)];

    private void Queue(string path)
    {
        if (!IsSource(path))
            return;
        lock (_lock)
        {
            if (_disposed)
                return;
            _pending.Add(path);
            _timer.Change(Settle, Timeout.InfiniteTimeSpan);
        }
    }

    // A deleted or moved-away path. A directory is no source, yet the outputs of the sources in it are stale.
    private void QueueGone(string path)
    {
        if (IsSource(path))
        {
            Queue(path);
            return;
        }
        if (!_ownsOutput || _outRoot is null || !IsUnder(_sourceRoot, path))
            return;
        foreach (string source in _mapped.Keys.Concat(_compiled.Keys))
        {
            if (IsUnder(path, source))
                Queue(source);
        }
        string outputs = Path.Combine(_outRoot, Path.GetRelativePath(_outBase, path));
        try
        {
            if (!Directory.Exists(outputs))
                return;
            var declared = new HashSet<string>(_mapped.Values.Concat(_compiled.Values.SelectMany(c => c.Outputs)), PathComparer);
            foreach (string output in Directory.EnumerateFiles(outputs, "*", SearchOption.AllDirectories))
            {
                bool module = output.EndsWith(".mjs", StringComparison.Ordinal);
                if (declared.Contains(output) || !module && !output.EndsWith(".js", StringComparison.Ordinal))
                    continue;
                Queue(Path.Combine(path, Path.ChangeExtension(Path.GetRelativePath(outputs, output), module ? ".mts" : ".ts")));
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // Gone or unreadable again.
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
        lock (_flushLock)
        {
            string[] batch;
            lock (_lock)
            {
                batch = [.. _pending];
                _pending.Clear();
            }
            foreach (string source in batch)
            {
                if (_stopping.IsCancellationRequested)
                    return;
                Strip(source, started: true);
            }
        }
    }

    /// <summary>
    /// Strips one source, or compiles it with its compiler. Logs rejections and I/O errors; leaves
    /// the old output in place then. A missing source removes its outputs when the watcher owns the
    /// output directory.
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
                {
                    foreach (string stale in Outputs(sourcePath))
                        Remove(stale);
                }
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

        if (_compiled.TryGetValue(sourcePath, out var compiled))
        {
            Compile(name, sourcePath, source, compiled, started);
            return;
        }

        var output = new ArrayBufferWriter<char>(source.Length);
        var sink = new Sink();
        if (!TypeStripper.Strip(source, output, sink, StripOptions.RewriteRelativeImportExtensions))
        {
            foreach (var d in sink.Items)
                _logger.LogError("{Diagnostic}", DiagnosticFormatter.Format(name, source, d));
            return;
        }
        Write(name, OutputPath(sourcePath), output.WrittenSpan, started, "Stripped");
    }

    private void Compile(string name, string sourcePath, string source, CompiledSource compiled, bool started)
    {
        // Keyed as the outputs are, whatever path form or comparer the compiler used.
        var texts = new Dictionary<string, string>(PathComparer);
        try
        {
            lock (CompilerGates.GetValue(compiled.Compiler, _ => new Lock()))
            {
                var outputs = compiled.Compiler.CompileAsync(sourcePath, source, compiled.Outputs, _logger, _stopping.Token).GetAwaiter().GetResult();
                if (outputs is null)
                    return;
                foreach (var (path, text) in outputs)
                    texts[Path.GetFullPath(path)] = text;
            }
        }
        catch (OperationCanceledException) when (_stopping.IsCancellationRequested)
        {
            return;
        }
        catch (Exception e)
        {
            _logger.LogError(e, "{File}: {Compiler} failed: {Message}", name, compiled.Compiler.Name, e.Message);
            return;
        }
        foreach (string target in compiled.Outputs)
        {
            if (texts.TryGetValue(target, out string? text))
                Write(name, target, text, started, "Compiled");
            else
                _logger.LogError("{File}: {Compiler} returned no text for {Target}", name, compiled.Compiler.Name, Path.GetRelativePath(_contentRoot, target));
        }
    }

    private void Write(string name, string target, ReadOnlySpan<char> text, bool started, string verb)
    {
        byte[] bytes = new byte[Utf8NoBom.GetByteCount(text)];
        Utf8NoBom.GetBytes(text, bytes);
        bool created = !File.Exists(target);
        try
        {
            if (!created && File.ReadAllBytes(target).AsSpan().SequenceEqual(bytes))
            {
                // Newer than its source again, so the next start and build skip it.
                File.SetLastWriteTimeUtc(target, DateTime.UtcNow);
                return;
            }
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
            _logger.LogInformation("{Verb} {Source} to {Target}; a new file is served after the next build (Ctrl+R in dotnet watch)",
                verb, name, Path.GetRelativePath(_contentRoot, target));
        else
            _logger.LogInformation("{Verb} {Source} to {Target}", verb, name, Path.GetRelativePath(_contentRoot, target));
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

    /// <summary>How long <see cref="WaitStopped"/> lets a running strip finish.</summary>
    internal static readonly TimeSpan StopWait = TimeSpan.FromSeconds(5);

    public void Dispose()
    {
        BeginStop();
        WaitStopped(DateTime.UtcNow + StopWait);
    }

    /// <summary>Stops watching and cancels a running compile; does not wait for it.</summary>
    internal void BeginStop()
    {
        lock (_lock)
        {
            if (_disposed)
                return;
            _disposed = true;
        }
        _stopping.Cancel();
        foreach (var watcher in _watchers)
            watcher.Dispose();
        _timer.Dispose();
    }

    /// <summary>Waits until <paramref name="deadline"/> for a running strip to end; logs when it does not.</summary>
    internal void WaitStopped(DateTime deadline)
    {
        var left = deadline - DateTime.UtcNow;
        if (left < TimeSpan.Zero)
            left = TimeSpan.Zero;
        if (_flushLock.TryEnter(left))
            _flushLock.Exit();
        else
            _logger.LogWarning("A strip was still running when the app stopped; its output may be incomplete");
    }

    private sealed class Sink : IDiagnosticSink
    {
        public readonly List<Diagnostic> Items = [];
        public void Report(in Diagnostic diagnostic) => Items.Add(diagnostic);
    }
}

/// <summary>A source of another compiler: its compiler and every output the build gave it.</summary>
internal sealed record CompiledSource(IPareCompiler Compiler, string[] Outputs);
