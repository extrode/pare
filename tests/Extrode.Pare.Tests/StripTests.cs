using System.Buffers;

namespace Extrode.Pare.Tests;

/// <summary>One case per row of handoff sections 5 (erase), 6 (reject) and 7 (traps).</summary>
public class StripTests
{
    private static string Strip(string source)
    {
        var output = new ArrayBufferWriter<char>();
        var sink = new RecordingSink();
        bool ok = TypeStripper.Strip(source, output, sink);
        Assert.True(ok, $"rejected: {string.Join(", ", sink.Items)}");
        Assert.Empty(sink.Items);
        return output.WrittenSpan.ToString();
    }

    private static List<Diagnostic> Reject(string source)
    {
        var output = new ArrayBufferWriter<char>();
        var sink = new RecordingSink();
        Assert.False(TypeStripper.Strip(source, output, sink));
        Assert.Equal(0, output.WrittenCount);
        return sink.Items;
    }

    // Section 5: syntax to erase.
    [Theory]
    [InlineData("let x: number = 1;", "let x         = 1;")]
    [InlineData("function f(a: T): U {}", "function f(a   )    {}")]
    [InlineData("function f(a?: T) {}", "function f(a    ) {}")]
    [InlineData("class C { x?: T; }", "class C { x    ; }")]
    [InlineData("let x!: T;", "let x    ;")]
    [InlineData("class C { x!: T; }", "class C { x    ; }")]
    [InlineData("obj!.x; fn()!;", "obj .x; fn() ;")]
    [InlineData("y = x as T;", "y = x     ;")]
    [InlineData("y = x satisfies T;", "y = x            ;")]
    [InlineData("function f<T>() {}", "function f   () {}")]
    [InlineData("class C<T> {}", "class C    {}")]
    [InlineData("g = <T>(x) => x;", "g =    (x) => x;")]
    [InlineData("f<T>(x);", "f   (x);")]
    [InlineData("new Map<K, V>();", "new Map      ();")]
    [InlineData("tag<T>`a`;", "tag   `a`;")]
    [InlineData("interface A { x: T }", "                    ")]
    [InlineData("type A = B;", "           ")]
    [InlineData("declare const x: T;", "                   ")]
    [InlineData("declare global {}", "                 ")]
    [InlineData("declare module \"x\" {}", "                     ")]
    [InlineData("namespace N { type X = 1 }", "                          ")]
    [InlineData("namespace N { declare namespace I { type T = 1 } }", "                                                  ")]
    [InlineData("namespace N { export declare interface I {} declare type T = 1 }", "                                                                ")]
    [InlineData("declare namespace N { function f(); export async function g(): void; }", "                                                                      ")]
    [InlineData("import type { A } from \"x\";", "                           ")]
    [InlineData("import { type A, b } from \"x\";", "import {         b } from \"x\";")]
    [InlineData("class C { public a; private b; protected c; readonly d; override e; }",
                "class C {        a;         b;           c;          d;          e; }")]
    [InlineData("abstract class C { abstract m(): void; }", "         class C {                     }")]
    [InlineData("class C implements I, J {}", "class C                 {}")]
    [InlineData("class C extends B<T> {}", "class C extends B    {}")]
    [InlineData("function f(this: Window, a) {}", "function f(              a) {}")]
    [InlineData("function f(a: string): void;\nfunction f(a) {}", "                            \nfunction f(a) {}")]
    [InlineData("class C { [key: string]: any; }", "class C {                     }")]
    [InlineData("class C { m(): void; }", "class C {            }")]
    [InlineData("class C { declare x: T; }", "class C {               }")]
    [InlineData("export interface A {}", "                     ")]
    [InlineData("export type A = B;", "                  ")]
    [InlineData("export { type A, b };", "export {         b };")]
    [InlineData("export type * from \"x\";", "                       ")]
    [InlineData("let u: unique symbol;", "let u               ;")]
    [InlineData("try {} catch (e: unknown) {}", "try {} catch (e         ) {}")]
    [InlineData("export as namespace N;", "                      ")]
    [InlineData("import type x = require(\"y\");", "                             ")]
    [InlineData("function f(x): asserts x is T {}", "function f(x)                 {}")]
    [InlineData("function f(x): x is T {}", "function f(x)         {}")]
    [InlineData("let m: { [K in keyof T]: T[K] extends infer U ? U : never };",
                "let m                                                      ;")]
    public void Erases_section_5_syntax(string source, string expected) => Assert.Equal(expected, Strip(source));

    [Fact]
    public void Erasing_keeps_line_terminators()
    {
        Assert.Equal("        \r\n  \u2028   \u2029  ", Strip("type A =\r\n B\u2028 | \u2029C;"));
    }

    // Section 6: syntax to reject.
    [Theory]
    [InlineData("enum E { A }", DiagnosticCode.Enum, 0)]
    [InlineData("const enum E { A }", DiagnosticCode.Enum, 0)]
    [InlineData("namespace N { export const x = 1; }", DiagnosticCode.NamespaceWithValues, 0)]
    [InlineData("module M { f(); }", DiagnosticCode.NamespaceWithValues, 0)]
    [InlineData("namespace N { export declare function f(): void }", DiagnosticCode.NamespaceWithValues, 0)]
    [InlineData("namespace N { declare let x: number; }", DiagnosticCode.NamespaceWithValues, 0)]
    [InlineData("namespace N { declare namespace I { const x: number } }", DiagnosticCode.NamespaceWithValues, 0)]
    [InlineData("namespace N { export declare const enum E { A } }", DiagnosticCode.NamespaceWithValues, 0)]
    [InlineData("namespace N { function f(); }", DiagnosticCode.NamespaceWithValues, 0)]
    [InlineData("namespace N { export function f(): void; }", DiagnosticCode.NamespaceWithValues, 0)]
    [InlineData("namespace N { export async function f(); }", DiagnosticCode.NamespaceWithValues, 0)]
    [InlineData("namespace N { async function f(); }", DiagnosticCode.NamespaceWithValues, 0)]
    [InlineData("class C { constructor(private x) {} }", DiagnosticCode.ParameterProperty, 22)]
    [InlineData("import x = require(\"y\");", DiagnosticCode.ImportEquals, 0)]
    [InlineData("import x = N.y;", DiagnosticCode.ImportEquals, 0)]
    [InlineData("export = x;", DiagnosticCode.ExportEquals, 0)]
    [InlineData("y = <T>x;", DiagnosticCode.AngleBracketAssertion, 4)]
    [InlineData("class C { m(@dec x) {} }", DiagnosticCode.ParameterDecorator, 12)]
    [InlineData("import type X, { Y } from \"x\";", DiagnosticCode.ImportTypeDefaultAndNamed, 7)]
    public void Rejects_section_6_syntax(string source, DiagnosticCode code, int start)
    {
        var diagnostic = Assert.Single(Reject(source));
        Assert.Equal(code, diagnostic.Code);
        Assert.Equal(start, diagnostic.Start);
    }

    [Fact]
    public void Reports_every_rejection_in_source_order()
    {
        var items = Reject("enum A {}\nexport = x;\ny = <T>z;");
        Assert.Equal([DiagnosticCode.Enum, DiagnosticCode.ExportEquals, DiagnosticCode.AngleBracketAssertion],
            items.Select(d => d.Code));
    }

    [Fact]
    public void Rejection_message_carries_location_and_hint()
    {
        const string source = "let a = 1;\nenum E { A }";
        var diagnostic = Assert.Single(Reject(source));
        Assert.Equal("a.ts:2:1: error PARE100: 'enum' generates runtime code and cannot be erased " +
                     "(use a 'const' object with 'as const' instead of 'enum')",
            DiagnosticFormatter.Format("a.ts", source, diagnostic));
    }

    [Fact]
    public void Tc39_decorators_pass_through()
    {
        const string source = "@dec class C { @dec m() {} @dec accessor x = 1; }";
        Assert.Equal(source, Strip(source));
    }

    [Fact]
    public void Declarations_inside_declare_are_not_rejected()
    {
        const string source = "declare namespace N { const x = 1; enum E {} }";
        Assert.Equal(new string(' ', source.Length), Strip(source));
    }

    // Section 7: known traps.
    [Theory]
    [InlineData("f<T>(x);", "f   (x);")]
    // TypeScript parses this as a generic call, spaces or not.
    [InlineData("a < b > (c);", "a       (c);")]
    [InlineData("f<T>`t`;", "f   `t`;")]
    [InlineData("y = f<T>;", "y = f   ;")]
    [InlineData("y = a < b > c;", "y = a < b > c;")]
    public void Trap_1_less_than_ambiguity(string source, string expected) => Assert.Equal(expected, Strip(source));

    [Theory]
    [InlineData("g = (a: T) => x;", "g = (a   ) => x;")]
    [InlineData("g = (a);", "g = (a);")]
    [InlineData("g = async (x): T => x;", "g = async (x)    => x;")]
    [InlineData("g = (a): T => x;", "g = (a)    => x;")]
    [InlineData("g = c ? (a): T => a : b;", "g = c ? (a)    => a : b;")]
    [InlineData("g = c ? (a) : b;", "g = c ? (a) : b;")]
    // `(a) : b => a` is not an arrow: the conditional's `:` must follow the whole arrow.
    [InlineData("g = c ? (a) : b => a;", "g = c ? (a) : b => a;")]
    public void Trap_2_arrow_or_parenthesized(string source, string expected) => Assert.Equal(expected, Strip(source));

    [Fact]
    public void Trap_3_regex_or_division()
    {
        const string source = "a = b / c / d; r = /x/g; if (x) /re/.test(y); f(x) / 2;";
        Assert.Equal(source, Strip(source));
        Assert.Equal("a = b      / c /g;", Strip("a = b as T / c /g;"));
        Assert.Equal("a = b  / c /g;", Strip("a = b! / c /g;"));
    }

    [Fact]
    public void Trap_4_nested_templates()
    {
        Assert.Equal("t = `a${ `b${ {c: 1}.c }` }d${ x      }`;", Strip("t = `a${ `b${ {c: 1}.c }` }d${ x as T }`;"));
    }

    [Theory]
    // Return type across lines: `)` moves to the end of the erased range.
    [InlineData("g = (a)\n: T => x;", "g = (a \n  ) => x;")]
    [InlineData("g = (a): {\n} => x;", "g = (a    \n) => x;")]
    // Type parameters with a line break before `(`: `(` moves to the start of the erased range.
    [InlineData("g = async <T>\n(a) => a;", "g = async (  \n a) => a;")]
    // Erased statement between statements: `;` keeps them apart.
    [InlineData("let a = b\ntype T = 1\n(c)", "let a = b\n;         \n(c)")]
    [InlineData("let a = b\ninterface I {}\n[c]", "let a = b\n;             \n[c]")]
    [InlineData("let a = b\ndeclare let x\n`t`", "let a = b\n;            \n`t`")]
    [InlineData("let a = b;\ntype T = 1\n(c)", "let a = b;\n          \n(c)")]
    [InlineData("{}\ntype T = 1\n(c)", "{}\n;         \n(c)")]
    // Erased class member between members.
    [InlineData("class C { a = 1\n declare b\n [c] = 2 }", "class C { a = 1\n ;        \n [c] = 2 }")]
    [InlineData("class C { a = 1\n public [c] = 2 }", "class C { a = 1\n ;      [c] = 2 }")]
    // An erased `as` or `satisfies` type ends at a line break, so TypeScript ends the statement
    // before a following `(`, `[` or template; JS would read a call, index or tagged template.
    [InlineData("const x = y as T\n(z)", "const x = y    ;\n(z)")]
    [InlineData("x = [1] as const\n[0]", "x = [1]        ;\n[0]")]
    [InlineData("y satisfies T\n`t`", "y           ;\n`t`")]
    [InlineData("y as T\n`a${b}`", "y    ;\n`a${b}`")]
    [InlineData("class C { x = y as T\n[k]() {} }", "class C { x = y    ;\n[k]() {} }")]
    [InlineData("function f() { return x as T\n(y) }", "function f() { return x    ;\n(y) }")]
    [InlineData("x = y as T\n- 1", "x = y     \n- 1")]
    public void Trap_5_asi_hazards(string source, string expected) => Assert.Equal(expected, Strip(source));

    [Theory]
    [InlineData("function f() { return x as\n T; }", "function f() { return x   \n  ; }")]
    [InlineData("function f() { return (\n x as T); }", "function f() { return (\n x     ); }")]
    [InlineData("function f() { return f<\nT>(x); }", "function f() { return f \n  (x); }")]
    [InlineData("function f() { return <T>\n(x) => x; }", "function f() { return (  \n x) => x; }")]
    [InlineData("function* f() { yield x satisfies\n T; }", "function* f() { yield x          \n  ; }")]
    [InlineData("function f() { throw x!\n; }", "function f() { throw x \n; }")]
    public void Trap_5_return_throw_yield_keep_their_operand(string source, string expected) =>
        Assert.Equal(expected, Strip(source));

    [Theory]
    [InlineData("type = 1;")]
    [InlineData("as = satisfies = declare = abstract = readonly = 1;")]
    [InlineData("namespace = module = keyof = infer = is = asserts = override = accessor = out = 1;")]
    [InlineData("type\n(x);")]
    [InlineData("declare\nlet x;")]
    [InlineData("abstract\nclass C {}")]
    [InlineData("let x = a\nas(b);")]
    public void Trap_6_contextual_keywords_as_identifiers(string source) => Assert.Equal(source, Strip(source));

    // TypeScript's isDeclaration: after `declare`, the keyword must start a declaration itself.
    [Theory]
    [InlineData("declare namespace = 1;")]
    [InlineData("declare namespace;")]
    [InlineData("declare module = 1;")]
    [InlineData("declare global = 1;")]
    [InlineData("declare type = 1;")]
    [InlineData("declare interface = 1;")]
    [InlineData("declare abstract = 1;")]
    [InlineData("declare async = 1;")]
    [InlineData("export declare namespace;")]
    public void Trap_6_declare_needs_a_declaration_after_it(string source) =>
        Assert.Equal(DiagnosticCode.SyntaxError, Assert.Single(Reject(source)).Code);

    [Fact]
    public void Trap_6_contextual_keywords_in_type_parameters()
    {
        Assert.Equal("function f             (in_, out) {}", Strip("function f<in T, out U>(in_, out) {}"));
    }

    [Fact]
    public void Trap_7_accessor_is_kept()
    {
        Assert.Equal("class C { accessor x    = 1; static accessor y; }", Strip("class C { accessor x: T = 1; static accessor y; }"));
    }

    [Theory]
    [InlineData("y = x! != z;", "y = x  != z;")]
    [InlineData("y = x != z;", "y = x != z;")]
    [InlineData("y = x!\n!z;", "y = x \n!z;")]
    [InlineData("y = x\n!z;", "y = x\n!z;")]
    public void Trap_9_non_null_or_not_equal(string source, string expected) => Assert.Equal(expected, Strip(source));

    [Fact]
    public void Trap_10_optional_chaining_with_type_arguments()
    {
        Assert.Equal("a?.b   (); a?.   ();", Strip("a?.b<T>(); a?.<T>();"));
    }

    [Theory]
    [InlineData("class C { get; set; static; async; }")]
    [InlineData("class C { get get() {} set set(v) {} static static() {} async async() {} }")]
    [InlineData("class C { static async *gen() {} get\n x() {} }")]
    public void Trap_11_class_member_keywords_as_names(string source) => Assert.Equal(source, Strip(source));

    [Theory]
    // A field named get, set or static whose erased type was all that stood before the line
    // break would become a modifier of the next member: `get\n post` is a getter in JS.
    [InlineData("class C {\n get!: T\n post!: T\n}", "class C {\n get   ;\n post    \n}")]
    [InlineData("class C {\n set?\n x() {}\n}", "class C {\n set;\n x() {}\n}")]
    [InlineData("class C {\n static: T\n x = 1\n}", "class C {\n static  ;\n x = 1\n}")]
    [InlineData("class C {\n static get: T\n [k]() {}\n}", "class C {\n static get  ;\n [k]() {}\n}")]
    [InlineData("class C {\n get: T;\n x() {}\n}", "class C {\n get   ;\n x() {}\n}")]
    [InlineData("class C {\n get: T\n}", "class C {\n get   \n}")]
    [InlineData("class C {\n async: T\n x() {}\n}", "class C {\n async   \n x() {}\n}")]
    [InlineData("class C {\n got: T\n x() {}\n}", "class C {\n got   \n x() {}\n}")]
    public void Trap_11_keyword_named_field_keeps_apart_from_the_next_member(string source, string expected) =>
        Assert.Equal(expected, Strip(source));

    [Fact]
    public void Trap_11_modifier_names_are_not_erased()
    {
        Assert.Equal("class C { public; readonly = 1; declare() {} abstract   ; }",
            Strip("class C { public; readonly = 1; declare() {} abstract: T; }"));
    }

    [Theory]
    [InlineData("import a from \"./a.ts\";", "import a from \"./a.js\";")]
    [InlineData("import \"../b.mts\";", "import \"../b.mjs\";")]
    [InlineData("import { x } from './c.cts';", "import { x } from './c.cjs';")]
    [InlineData("export * from \"./a.ts\";", "export * from \"./a.js\";")]
    [InlineData("export { x } from \"./a.ts\" with { type: \"js\" };", "export { x } from \"./a.js\" with { type: \"js\" };")]
    [InlineData("const m = import(\"./a.ts\");", "const m = import(\"./a.js\");")]
    [InlineData("const m = import(\"./a.ts\", { with: {} });", "const m = import(\"./a.js\", { with: {} });")]
    [InlineData("import type { T } from \"./t.ts\";", "                                ")]
    [InlineData("export type { T } from \"./t.ts\";", "                                ")]
    [InlineData("declare module \"m\" { export * from \"./a.ts\"; }", "                                              ")]
    [InlineData("import { type T, x } from \"./t.ts\";", "import {         x } from \"./t.js\";")]
    public void Rewrites_relative_ts_specifiers(string source, string expected) =>
        Assert.Equal(expected, StripWith(source, StripOptions.RewriteRelativeImportExtensions));

    [Theory]
    [InlineData("import a from \"a.ts\";")]
    [InlineData("import a from \"/abs/a.ts\";")]
    [InlineData("import a from \"./a.d.ts\";")]
    [InlineData("import a from \"./a.tsx\";")]
    [InlineData("import a from \"./a.js\";")]
    [InlineData("import a from \"./a\\u002ets\";")]
    [InlineData("import a from \"./\\u0061.ts\";")]
    [InlineData("const m = import(\"./a.ts\" + x);")]
    [InlineData("const s = \"./a.ts\";")]
    public void Leaves_other_specifiers_alone(string source) =>
        Assert.Equal(source, StripWith(source, StripOptions.RewriteRelativeImportExtensions));

    [Fact]
    public void Specifiers_are_kept_without_the_option()
    {
        Assert.Equal("import a from \"./a.ts\";", Strip("import a from \"./a.ts\";"));
    }

    private static string StripWith(string source, StripOptions options)
    {
        var output = new ArrayBufferWriter<char>();
        var sink = new RecordingSink();
        Assert.True(TypeStripper.Strip(source, output, sink, options));
        return output.WrittenSpan.ToString();
    }
}
