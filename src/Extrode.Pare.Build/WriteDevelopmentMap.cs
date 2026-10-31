using System.Text;
using System.Text.Json;
using Microsoft.Build.Framework;

namespace Extrode.Pare.Build;

/// <summary>
/// Writes the development map <c>AddPareDevelopment</c> reads: the source and output roots, then one
/// <c>{ source, output }</c> entry per item in <see cref="Sources"/> and one
/// <c>{ source, output, compiler }</c> per item in <see cref="Compiled"/>. A JSON writer escapes the
/// paths, so a quote or backslash in one cannot break the file. The file is left alone when its
/// text is unchanged, so a watcher that follows it does not see a write on every build.
/// </summary>
public sealed class WriteDevelopmentMap : Microsoft.Build.Utilities.Task
{
    [Required]
    public string File { get; set; } = "";

    [Required]
    public string SourceRoot { get; set; } = "";

    [Required]
    public string OutputRoot { get; set; } = "";

    public ITaskItem[] Sources { get; set; } = [];

    public ITaskItem[] Compiled { get; set; } = [];

    public override bool Execute()
    {
        using var stream = new MemoryStream();
        using (var json = new Utf8JsonWriter(stream))
        {
            json.WriteStartObject();
            json.WriteString("sourceRoot", SourceRoot);
            json.WriteString("outputRoot", OutputRoot);
            WriteList(json, "sources", Sources, withCompiler: false);
            WriteList(json, "compiled", Compiled, withCompiler: true);
            json.WriteEndObject();
        }
        string text = new UTF8Encoding(false).GetString(stream.ToArray()) + Environment.NewLine;
        try
        {
            if (System.IO.File.Exists(File) && System.IO.File.ReadAllText(File) == text)
                return true;
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(File))!);
            System.IO.File.WriteAllText(File, text, new UTF8Encoding(false));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Log.LogError(null, null, null, File, 0, 0, 0, 0, e.Message);
            return false;
        }
        return true;
    }

    private static void WriteList(Utf8JsonWriter json, string name, ITaskItem[] items, bool withCompiler)
    {
        json.WriteStartArray(name);
        foreach (var item in items)
        {
            json.WriteStartObject();
            json.WriteString("source", item.GetMetadata("FullPath"));
            json.WriteString("output", item.GetMetadata("PareOutputPath"));
            if (withCompiler)
                json.WriteString("compiler", item.GetMetadata("PareCompiler"));
            json.WriteEndObject();
        }
        json.WriteEndArray();
    }
}
