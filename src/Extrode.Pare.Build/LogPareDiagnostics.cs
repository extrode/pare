using System.Text;
using Microsoft.Build.Framework;

namespace Extrode.Pare.Build;

/// <summary>
/// Logs <see cref="Text"/> once per item in <see cref="Items"/>, as an error or a warning with the
/// item as the file and <see cref="Code"/> as the code. <c>{Name}</c> in the text is the item's
/// metadata <c>Name</c>. MSBuild's own <c>Error</c> task, batched over items, stops the target at
/// the first batch; this task reports every item in one build.
/// </summary>
public sealed class LogPareDiagnostics : Microsoft.Build.Utilities.Task
{
    [Required]
    public ITaskItem[] Items { get; set; } = [];

    [Required]
    public string Code { get; set; } = "";

    [Required]
    public string Text { get; set; } = "";

    public override bool Execute()
    {
        foreach (var item in Items)
        {
            var message = new StringBuilder(Text);
            foreach (string name in item.MetadataNames.Cast<string>().OrderByDescending(n => n.Length))
                message.Replace("{" + name + "}", item.GetMetadata(name));
            Log.LogError(null, Code, null, item.ItemSpec, 0, 0, 0, 0, message.ToString());
        }
        return !Log.HasLoggedErrors;
    }
}
