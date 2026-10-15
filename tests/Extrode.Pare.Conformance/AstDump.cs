using System.Reflection;
using System.Text;
using Acornima;
using Acornima.Ast;

namespace Extrode.Pare.Conformance;

/// <summary>Location-free text form of an Acornima tree, for comparing emitters' output.</summary>
internal static class AstDump
{
    /// <summary>
    /// The tree as text: node types and scalar properties, no locations. Leaves out what is not
    /// behavior: empty statements in a statement list (Pare's ASI guard <c>;</c>, handoff 7.5) and
    /// a bare <c>export {}</c> (tsc keeps or adds it to mark a module).
    /// </summary>
    /// <param name="reprinted">For emitters that print a new text: skip literal source text
    /// (<c>Raw</c>: quotes, numeric forms and escapes change, values do not), read <c>{ a: a }</c>
    /// as <c>{ a }</c> and <c>void 0</c> as <c>undefined</c>.</param>
    /// <param name="loose">Also leave out imports (emitters that elide unused ones) and a numeric
    /// suffix on identifiers (emitters that rename a shadowed binding <c>x</c> to <c>x1</c>).</param>
    public static string Dump(Node root, bool reprinted = false, bool loose = false)
    {
        var sb = new StringBuilder();
        // Iterative: corpus files nest deep enough to overflow a recursive walk.
        var stack = new Stack<object>();
        stack.Push(root);
        while (stack.Count > 0)
        {
            if (stack.Pop() is not Node node)
            {
                sb.Append(')');
                continue;
            }
            // Minifying printers write `undefined` as `void 0`.
            if (reprinted && node is UnaryExpression { Operator: Operator.Void, Argument: NumericLiteral { Value: 0 } })
            {
                sb.Append(" (Identifier Name=undefined TypeText=Identifier)");
                continue;
            }
            sb.Append(" (").Append(node.Type);
            foreach (var p in ScalarProperties(node.GetType()))
            {
                // tsc lowercases hex BigInt literals (0xFFFFn); the BigInt property keeps the value.
                if (p.Name == "Raw" && (reprinted || node is BigIntLiteral))
                    continue;
                if (loose && p.Name == "Name" && node is Identifier id)
                {
                    sb.Append(" Name=").Append(id.Name.AsSpan().TrimEnd("0123456789"));
                    continue;
                }
                if (reprinted && p.Name == "Shorthand" && node is Property)
                    continue;
                sb.Append(' ').Append(p.Name).Append('=').Append(p.GetValue(node));
            }
            stack.Push(")");
            var children = new List<Node>();
            foreach (var child in node.ChildNodes)
                children.Add(child);
            // Acornima gives `{ a }` one child; give `{ a: a }` the same when printers may shorten it.
            if (reprinted && node is Property { Shorthand: false, Computed: false, Key: Identifier key, Value: Identifier value } &&
                (key.Name == value.Name || (loose && key.Name.AsSpan().TrimEnd("0123456789").SequenceEqual(value.Name.AsSpan().TrimEnd("0123456789")))))
                children.RemoveAt(1);
            bool list = node.Type is NodeType.Program or NodeType.BlockStatement or NodeType.StaticBlock or NodeType.SwitchCase;
            for (int i = children.Count - 1; i >= 0; i--)
            {
                if (list && (children[i].Type == NodeType.EmptyStatement || IsEmptyExport(children[i])))
                    continue;
                if (loose && children[i].Type == NodeType.ImportDeclaration)
                    continue;
                stack.Push(children[i]);
            }
        }
        return sb.ToString();
    }

    private static bool IsEmptyExport(Node node) =>
        node is ExportNamedDeclaration { Declaration: null, Source: null, Specifiers.Count: 0 };

    private static readonly Dictionary<Type, PropertyInfo[]> Scalars = [];

    private static PropertyInfo[] ScalarProperties(Type type)
    {
        if (!Scalars.TryGetValue(type, out var props))
        {
            props = [.. type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Where(p => p.GetIndexParameters().Length == 0 && IsScalar(p.PropertyType) &&
                            p.Name is not ("Type" or "Start" or "End" or "Range" or "Location"))
                .OrderBy(p => p.Name, StringComparer.Ordinal)];
            Scalars[type] = props;
        }
        return props;
    }

    private static bool IsScalar(Type t) => t == typeof(string) || t == typeof(bool) || t.IsEnum;
}
