using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;

namespace Extrode.Pare.Build;

/// <summary>
/// One item per file in <see cref="Sources"/>, compared by full path, so <c>wwwroot/js/a.ts</c> and
/// <c>wwwroot\js\a.ts</c> are one file on Windows. The last declaration's metadata wins (the project
/// file comes after a package's .props); the item keeps the place of the first. A task, not
/// <c>RemoveDuplicates</c>: copying <c>%(RecursiveDir)</c> into metadata in a target batches and
/// reorders the items, and the out-of-process task host does not pass <c>RecursiveDir</c>.
/// <see cref="RecursiveDirs"/> is <c>@(Sources-&gt;'|%(RecursiveDir)')</c>, one entry per source,
/// and becomes <c>_PareRecursiveDir</c>.
/// </summary>
public sealed class DistinctSources : Microsoft.Build.Utilities.Task
{
    public ITaskItem[] Sources { get; set; } = [];

    public ITaskItem[] RecursiveDirs { get; set; } = [];

    [Output]
    public ITaskItem[] Distinct { get; private set; } = [];

    public override bool Execute()
    {
        if (RecursiveDirs.Length != Sources.Length)
        {
            Log.LogError($"DistinctSources: {Sources.Length} sources but {RecursiveDirs.Length} recursive dirs");
            return false;
        }
        var comparer = OperatingSystem.IsLinux() ? StringComparer.Ordinal : StringComparer.OrdinalIgnoreCase;
        var slot = new Dictionary<string, int>(comparer);
        var result = new List<ITaskItem>(Sources.Length);
        for (int i = 0; i < Sources.Length; i++)
        {
            // Not new TaskItem(Sources[i]): its CopyMetadataTo adds OriginalItemSpec, and static web
            // asset compression would then read the .ts source instead of the output.
            var item = Sources[i] is ITaskItem2 escaped
                ? new TaskItem(escaped.EvaluatedIncludeEscaped, escaped.CloneCustomMetadataEscaped())
                : new TaskItem(Sources[i].ItemSpec, Sources[i].CloneCustomMetadata());
            item.SetMetadata("_PareRecursiveDir", RecursiveDirs[i].ItemSpec[1..]);
            string key = Sources[i].GetMetadata("FullPath");
            if (slot.TryGetValue(key, out int at))
                result[at] = item;
            else
            {
                slot[key] = result.Count;
                result.Add(item);
            }
        }
        Distinct = [.. result];
        return true;
    }
}
