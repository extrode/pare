using System.Reflection;
using System.Text;
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
    /// <param name="ignoreRaw">Skip literal source text (<c>Raw</c>), for emitters that reprint
    /// literals: quotes, numeric forms and escapes change, values do not.</param>
    public static string Dump(Node root, bool ignoreRaw = false)
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
            sb.Append(" (").Append(node.Type);
            foreach (var p in ScalarProperties(node.GetType()))
            {
                // tsc lowercases hex BigInt literals (0xFFFFn); the BigInt property keeps the value.
                if (p.Name == "Raw" && (ignoreRaw || node is BigIntLiteral))
                    continue;
                sb.Append(' ').Append(p.Name).Append('=').Append(p.GetValue(node));
            }
            stack.Push(")");
            var children = new List<Node>();
            foreach (var child in node.ChildNodes)
                children.Add(child);
            bool list = node.Type is NodeType.Program or NodeType.BlockStatement or NodeType.StaticBlock or NodeType.SwitchCase;
            for (int i = children.Count - 1; i >= 0; i--)
            {
                if (list && (children[i].Type == NodeType.EmptyStatement || IsEmptyExport(children[i])))
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
