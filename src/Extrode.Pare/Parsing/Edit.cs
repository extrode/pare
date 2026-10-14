namespace Extrode.Pare.Parsing;

/// <summary>
/// One erase range <c>[Start, End)</c>. Every character becomes a space except line terminators.
/// <see cref="First"/> and <see cref="Last"/>, when not <c>'\0'</c>, overwrite the first or last
/// character of the range: the ASI fixes from handoff section 7.5 (<c>;</c> before an erased
/// statement, <c>)</c> or <c>(</c> moved across an erased type).
/// <see cref="Replace"/>, when not <c>'\0'</c>, makes the edit a one-character substitution at
/// <see cref="Start"/> instead (the <c>.ts</c> to <c>.js</c> specifier rewrite); it is skipped
/// when an erase range already covers that character.
/// </summary>
internal readonly record struct Edit(int Start, int End, char First = '\0', char Last = '\0', char Replace = '\0');

internal static class EditWriter
{
    /// <summary>Writes <paramref name="source"/> with <paramref name="edits"/> applied. Sorts the edits in place.</summary>
    public static void Write(ReadOnlySpan<char> source, Span<Edit> edits, Span<char> dest)
    {
        // Edits arrive nearly sorted; insertion sort is stable and cheap here.
        for (int i = 1; i < edits.Length; i++)
        {
            var e = edits[i];
            int j = i - 1;
            while (j >= 0 && edits[j].Start > e.Start)
            {
                edits[j + 1] = edits[j];
                j--;
            }
            edits[j + 1] = e;
        }

        source.CopyTo(dest);
        foreach (var e in edits)
        {
            if (e.Replace != '\0')
            {
                if (dest[e.Start] == source[e.Start])
                    dest[e.Start] = e.Replace;
                continue;
            }
            for (int i = e.Start; i < e.End; i++)
            {
                char c = source[i];
                if (c is not ('\n' or '\r' or '\u2028' or '\u2029'))
                    dest[i] = ' ';
            }
            if (e.First != '\0' && e.End > e.Start)
                dest[e.Start] = e.First;
            if (e.Last != '\0' && e.End > e.Start)
                dest[e.End - 1] = e.Last;
        }
    }
}
