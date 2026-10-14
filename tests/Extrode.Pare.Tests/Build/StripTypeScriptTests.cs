using System.Collections;
using Extrode.Pare.Build;
using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;

namespace Extrode.Pare.Tests.Build;

public sealed class StripTypeScriptTests : IDisposable
{
    private readonly DirectoryInfo _dir = Directory.CreateTempSubdirectory("pare-task-");
    private readonly Engine _engine = new();

    public void Dispose() => _dir.Delete(recursive: true);

    private string P(string relative) => Path.Combine(_dir.FullName, relative);

    private TaskItem Source(string relative, string text, string output)
    {
        File.WriteAllText(P(relative), text);
        var item = new TaskItem(P(relative));
        item.SetMetadata("PareOutputPath", P(output));
        return item;
    }

    private bool Run(params ITaskItem[] sources) =>
        new StripTypeScript { BuildEngine = _engine, Sources = sources }.Execute();

    [Fact]
    public void Writes_each_source_to_its_output_path()
    {
        var a = Source("a.ts", "import b from './b.ts';\nlet x: T = b;", "out/sub/a.js");
        var task = new StripTypeScript { BuildEngine = _engine, Sources = [a] };

        Assert.True(task.Execute());

        Assert.Equal("import b from './b.js';\nlet x    = b;", File.ReadAllText(P("out/sub/a.js")));
        Assert.Equal(P("out/sub/a.js"), Assert.Single(task.Written).ItemSpec);
        Assert.Empty(_engine.Errors);
    }

    [Fact]
    public void Logs_each_rejection_with_code_line_and_column_and_keeps_going()
    {
        var bad = Source("bad.ts", "let a = 1;\n  enum E {}\nexport = a;", "bad.js");
        var good = Source("good.ts", "let b: T;", "good.js");

        Assert.False(Run(bad, good));

        Assert.Collection(_engine.Errors,
            e => Assert.Equal(("PARE100", 2, 3), (e.Code, e.LineNumber, e.ColumnNumber)),
            e => Assert.Equal(("PARE104", 3, 1), (e.Code, e.LineNumber, e.ColumnNumber)));
        Assert.All(_engine.Errors, e => Assert.Equal(P("bad.ts"), e.File));
        Assert.Contains("use a 'const' object", _engine.Errors[0].Message);
        Assert.False(File.Exists(P("bad.js")));
        Assert.Equal("let b   ;", File.ReadAllText(P("good.js")));
    }

    [Fact]
    public void Missing_source_or_metadata_is_an_error()
    {
        var missing = new TaskItem(P("missing.ts"));
        missing.SetMetadata("PareOutputPath", P("missing.js"));
        var noMetadata = new TaskItem(P("x.ts"));

        Assert.False(Run(missing, noMetadata));
        Assert.Equal(2, _engine.Errors.Count);
        Assert.Contains("PareOutputPath", _engine.Errors[1].Message);
    }

    private sealed class Engine : IBuildEngine
    {
        public List<BuildErrorEventArgs> Errors { get; } = [];
        public bool ContinueOnError => false;
        public int LineNumberOfTaskNode => 0;
        public int ColumnNumberOfTaskNode => 0;
        public string ProjectFileOfTaskNode => "test.proj";
        public bool BuildProjectFile(string projectFileName, string[] targetNames, IDictionary globalProperties, IDictionary targetOutputs) => true;
        public void LogCustomEvent(CustomBuildEventArgs e) { }
        public void LogErrorEvent(BuildErrorEventArgs e) => Errors.Add(e);
        public void LogMessageEvent(BuildMessageEventArgs e) { }
        public void LogWarningEvent(BuildWarningEventArgs e) { }
    }
}
