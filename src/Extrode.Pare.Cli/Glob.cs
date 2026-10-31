namespace Extrode.Pare.Cli;

/// <summary>
/// Minimal glob for shells that do not expand them (cmd, PowerShell): <c>*</c> and <c>?</c> within
/// one path segment, <c>**</c> for any number of directories. Paths use <c>/</c> or <c>\</c>.
/// </summary>
internal static class Glob
{
    public static bool HasWildcard(string pattern) => pattern.AsSpan().IndexOfAny('*', '?') >= 0;

    /// <summary>The directory before the first segment with a wildcard, or "." when there is none.</summary>
    public static string BaseDirectory(string pattern)
    {
        var segments = Split(pattern);
        int i = 0;
        while (i < segments.Length && !HasWildcard(segments[i]))
            i++;
        if (i == 0)
            return Path.IsPathRooted(pattern) ? Path.GetPathRoot(pattern)! : ".";
        string dir = string.Join('/', segments, 0, i);
        // "C:" alone is the current directory on drive C; the root needs its separator.
        if (dir.Length == 2 && char.IsAsciiLetter(dir[0]) && dir[1] == ':')
            return dir + "/";
        if (pattern.StartsWith("//") || pattern.StartsWith("\\\\"))
            return "//" + dir;
        return pattern.StartsWith('/') ? "/" + dir : dir;
    }

    /// <summary>Whether <paramref name="relative"/> (to <see cref="BaseDirectory"/>) matches the wildcard part.</summary>
    public static bool Matches(string pattern, string relative)
    {
        var segments = Split(pattern);
        int i = 0;
        while (i < segments.Length && !HasWildcard(segments[i]))
            i++;
        return MatchSegments(segments.AsSpan(i), Split(relative));
    }

    private static string[] Split(string path) =>
        path.Split(['/', '\\'], StringSplitOptions.RemoveEmptyEntries);

    private static bool MatchSegments(ReadOnlySpan<string> pattern, ReadOnlySpan<string> path)
    {
        if (pattern.IsEmpty)
            return path.IsEmpty;
        if (pattern[0] == "**")
        {
            for (int skip = 0; skip <= path.Length; skip++)
            {
                if (MatchSegments(pattern[1..], path[skip..]))
                    return true;
            }
            return false;
        }
        return !path.IsEmpty && MatchSegment(pattern[0], path[0]) && MatchSegments(pattern[1..], path[1..]);
    }

    private static bool MatchSegment(ReadOnlySpan<char> pattern, ReadOnlySpan<char> name)
    {
        if (pattern.IsEmpty)
            return name.IsEmpty;
        if (pattern[0] == '*')
        {
            for (int skip = 0; skip <= name.Length; skip++)
            {
                if (MatchSegment(pattern[1..], name[skip..]))
                    return true;
            }
            return false;
        }
        return !name.IsEmpty && (pattern[0] == '?' || pattern[0] == name[0]) && MatchSegment(pattern[1..], name[1..]);
    }
}
