# Using the library

`Extrode.Pare` is the stripper alone: no MSBuild, no package dependencies.

```sh
dotnet add package Extrode.Pare
```

## Strip a string

```csharp
using Extrode.Pare;

string? js = TypeStripper.Strip(source, out IReadOnlyList<Diagnostic> diagnostics);
if (js is null)
{
    foreach (var d in diagnostics)
        Console.Error.WriteLine(DiagnosticFormatter.Format("app.ts", source, d));
}
```

`js` is null when the file was rejected. This overload allocates, which is fine for tests and tools.

## Strip without allocating

```csharp
bool ok = TypeStripper.Strip(source, output, diagnostics);
bool ok2 = TypeStripper.Strip(source, output, diagnostics, StripOptions.RewriteRelativeImportExtensions);
```

| Argument | Type |
|---|---|
| `source` | `ReadOnlySpan<char>` |
| `output` | an `IBufferWriter<char>` |
| `diagnostics` | an `IDiagnosticSink` |

`StripOptions.RewriteRelativeImportExtensions` turns `./x.ts` into `./x.js`. The library leaves it
off by default; the CLI and the MSBuild package turn it on.

These overloads, `Diagnostic`, `DiagnosticCode`, `IDiagnosticSink`, `DiagnosticFormatter` and
`StripOptions` are stable for 0.x. Later 0.x releases add and never change or remove, and
`DiagnosticCode` values are fixed.

## Why UTF-16

JavaScript engines report columns in UTF-16 code units, so erasing in UTF-16 (one space per code
unit) keeps every position the same by construction. The reasoning is recorded in
decision 001 (`docs/decisions/` in the [repository](https://github.com/extrode/pare/tree/dev/docs/decisions)).

## Test TypeScript modules in a .NET JavaScript engine

You can run your TypeScript modules in Jint with no build output and no Node.
[`JintModuleTests.cs`](https://github.com/extrode/pare/blob/dev/tests/Extrode.Pare.Integration/JintModuleTests.cs)
is a 25-line Jint module loader that strips each `.ts` as it is imported (`./x.js` loads `x.ts`)
and reports rejections as `file:line:col: error PARE...`.

## Deep nesting

Very deep nesting fails with a diagnostic instead of a stack overflow. On linux-musl-arm64
NativeAOT, call `Strip` from a thread you created: .NET reads the main thread's stack size wrongly
there and the check fails at once.
