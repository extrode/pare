using System.Buffers;
using System.Text;

namespace Extrode.Pare.Tests;

/// <summary>Handoff section 10.7: no allocation per <see cref="TypeStripper.Strip(ReadOnlySpan{char}, IBufferWriter{char}, IDiagnosticSink)"/> call once pools are warm.</summary>
public class AllocationTests
{
    private const string Typed = """
        import type { A } from "./a.ts";
        import { b, type C } from "./b.ts";
        export interface Shape<T extends object = {}> { kind: "circle" | "square"; size?: number; meta: T }
        type Pair<K, V> = [key: K, value: V];
        declare const enumLike: unique symbol;
        export abstract class Base<T> implements Shape<T> {
          private readonly id: number = 0;
          protected abstract area(this: Base<T>): number;
          declare kind: "circle" | "square";
          meta!: T;
          static create<U>(this: void, value: U): Base<U> | undefined { return undefined as unknown as Base<U>; }
        }
        export function f<T,>(a: T, b?: string, ...rest: number[]): asserts a is NonNullable<T> {
          const g = async <U>(x: U): Promise<U> => x;
          const h = (y: number): y is 1 => y === 1;
          let v = (a as any)!.value satisfies unknown;
          if (b! < (rest.length as number)) g<number>(1);
          for (const [k, w] of new Map<string, number>()) { k; w; }
          return;
        }
        let tpl = `${f<string>("x")}` + /re[/]x/g.source;
        export default (a: number) => a > 1 ? (b: number): number => b : null;
        """;

    private const string Rejected = """
        enum E { A }
        namespace N { export const x = 1; }
        class P { constructor(private x: number) {} }
        let y: = ;
        """;

    public static TheoryData<string, StripOptions, bool> Sources() => new()
    {
        { Typed, StripOptions.None, true },
        { Typed, StripOptions.RewriteRelativeImportExtensions, true },
        { Rejected, StripOptions.None, false },
        { Large(), StripOptions.None, true },
        { "(a = (b = (c = (d = (e)))));\n" + string.Concat(Enumerable.Repeat("(a, b) => a;\n", 500)), StripOptions.None, true },
    };

    private static string Large()
    {
        var text = new StringBuilder();
        for (int i = 0; i < 400; i++)
            text.Append(Typed);
        return text.ToString();
    }

    [Theory]
    [MemberData(nameof(Sources))]
    public void Strip_allocates_nothing_once_pools_are_warm(string source, StripOptions options, bool ok)
    {
        var output = new ArrayBufferWriter<char>(source.Length);
        var sink = new CountingSink();
        for (int i = 0; i < 3; i++)
        {
            output.ResetWrittenCount();
            Assert.Equal(ok, TypeStripper.Strip(source, output, sink, options));
        }

        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 10; i++)
        {
            output.ResetWrittenCount();
            TypeStripper.Strip(source, output, sink, options);
        }
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.Equal(0, allocated);
    }

    private sealed class CountingSink : IDiagnosticSink
    {
        public int Count;
        public void Report(in Diagnostic diagnostic) => Count++;
    }
}
