using System.Buffers;
using System.Text;
using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;

namespace Extrode.Pare.Build;

/// <summary>
/// Strips each <see cref="Sources"/> item to the path in its <c>PareOutputPath</c> metadata,
/// rewriting relative <c>./x.ts</c> specifiers to <c>./x.js</c>. Rejections are logged as errors
/// with file, line and column; every source is processed even after one fails.
/// </summary>
public sealed class StripTypeScript : Microsoft.Build.Utilities.Task
{
    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    [Required]
    public ITaskItem[] Sources { get; set; } = [];

    /// <summary>The files written.</summary>
    [Output]
    public ITaskItem[] Written { get; private set; } = [];

    public override bool Execute()
    {
        var written = new List<ITaskItem>(Sources.Length);
        var output = new ArrayBufferWriter<char>();
        var sink = new Sink();
        foreach (var item in Sources)
        {
            string target = item.GetMetadata("PareOutputPath");
            if (target.Length == 0)
            {
                Log.LogError($"{item.ItemSpec}: missing PareOutputPath metadata");
                continue;
            }
            string source;
            try
            {
                source = File.ReadAllText(item.GetMetadata("FullPath"));
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                Log.LogError(null, null, null, item.ItemSpec, 0, 0, 0, 0, e.Message);
                continue;
            }

            output.ResetWrittenCount();
            sink.Items.Clear();
            if (!TypeStripper.Strip(source, output, sink, StripOptions.RewriteRelativeImportExtensions))
            {
                foreach (var d in sink.Items)
                {
                    var at = SourceLocation.FromOffset(source, d.Start);
                    Log.LogError(null, $"PARE{(int)d.Code:000}", null, item.ItemSpec, at.Line, at.Column, 0, 0,
                        $"{DiagnosticMessages.Message(d.Code)} ({DiagnosticMessages.Hint(d.Code)})");
                }
                continue;
            }

            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(target))!);
                using var stream = new FileStream(target, FileMode.Create, FileAccess.Write);
                using var writer = new StreamWriter(stream, Utf8NoBom);
                writer.Write(output.WrittenSpan);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                Log.LogError(null, null, null, target, 0, 0, 0, 0, e.Message);
                continue;
            }
            Log.LogMessage(MessageImportance.Low, $"pare: {item.ItemSpec} -> {target}");
            written.Add(new TaskItem(target));
        }
        Written = [.. written];
        return !Log.HasLoggedErrors;
    }

    private sealed class Sink : IDiagnosticSink
    {
        public readonly List<Diagnostic> Items = [];
        public void Report(in Diagnostic diagnostic) => Items.Add(diagnostic);
    }
}
