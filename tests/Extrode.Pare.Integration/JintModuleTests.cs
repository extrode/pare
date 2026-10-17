using Jint;
using Jint.Runtime.Modules;

namespace Extrode.Pare.Integration;

/// <summary>
/// Sample: TypeScript modules tested in Jint, with no build output and no Node. The loader strips
/// each source as it is imported; an import of `./x.js` loads `x.ts`, as in the browser after a build.
/// </summary>
public sealed class JintModuleTests : IDisposable
{
    // Jint's ModuleLoader base reports any load failure as "Could not load module", so the
    // diagnostics would be lost; this builds the module itself and lets the exception through.
    private sealed class PareModuleLoader(string basePath) : IModuleLoader
    {
        private readonly DefaultModuleLoader _files = new(basePath, restrictToBasePath: true);

        public ResolvedSpecifier Resolve(string? referencingModuleLocation, ModuleRequest moduleRequest) =>
            _files.Resolve(referencingModuleLocation, moduleRequest);

        public Module LoadModule(Engine engine, ResolvedSpecifier resolved)
        {
            string path = resolved.Uri!.LocalPath;
            if (path.EndsWith(".js", StringComparison.Ordinal) && File.Exists(Path.ChangeExtension(path, ".ts")))
                path = Path.ChangeExtension(path, ".ts");
            if (!path.EndsWith(".ts", StringComparison.Ordinal))
                return _files.LoadModule(engine, resolved);
            string source = File.ReadAllText(path);
            string js = TypeStripper.Strip(source, out var diagnostics) ?? throw new InvalidOperationException(
                string.Join('\n', diagnostics.Select(d => DiagnosticFormatter.Format(path, source, d))));
            return ModuleFactory.BuildSourceTextModule(engine, resolved, js);
        }
    }

    private readonly DirectoryInfo _dir = Directory.CreateTempSubdirectory("pare-jint-");

    public void Dispose() => _dir.Delete(recursive: true);

    private void Put(string relative, string text)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.Combine(_dir.FullName, relative))!);
        File.WriteAllText(Path.Combine(_dir.FullName, relative), text);
    }

    [Fact]
    public void A_two_module_typescript_graph_runs_with_its_types_stripped_on_import()
    {
        Put("web/format.ts", "export function plural(n: number, word: string): string {\n  return `${n} ${word}${n === 1 ? '' : 's'}`;\n}\n");
        Put("web/counter.ts", "import { plural } from './format.js';\nexport function label(clicks: number): string { return plural(clicks, 'click'); }\n");
        using var engine = new Engine(o => o.EnableModules(new PareModuleLoader(_dir.FullName)));

        var counter = engine.Modules.Import("./web/counter.js");

        Assert.Equal("1 click", engine.Invoke(counter.Get("label"), 1).AsString());
        Assert.Equal("2 clicks", engine.Invoke(counter.Get("label"), 2).AsString());
    }

    [Fact]
    public void A_rejected_import_fails_with_its_located_diagnostic()
    {
        Put("web/app.ts", "import { E } from './kinds.js';\nexport let e = E.A;\n");
        Put("web/kinds.ts", "\nexport enum E { A }\n");
        using var engine = new Engine(o => o.EnableModules(new PareModuleLoader(_dir.FullName)));

        var error = Assert.Throws<InvalidOperationException>(() => engine.Modules.Import("./web/app.js"));

        Assert.Contains("kinds.ts:2:8: error PARE100: ", error.Message);
    }
}
