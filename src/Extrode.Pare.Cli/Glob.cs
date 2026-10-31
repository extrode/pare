namespace Extrode.Pare.Cli;

/// <summary>
/// Minimal glob for shells that do not expand them (cmd, PowerShell): <c>*</c> and <c>?</c> within
/// one path segment, <c>**</c> for any number of directories. Paths use <c>/</c> or <c>\</c>. A
/// Windows device prefix (<c>\\?\</c>, <c>\\.\</c>, or either with <c>/</c>) belongs to the root:
/// its <c>?</c> is not a wildcard.
/// </summary>
internal static class Glob
{
    public static bool HasWildcard(string pattern) => pattern.AsSpan(DevicePrefix(pattern)).IndexOfAny('*', '?') >= 0;

    /// <summary>The directory before the first segment with a wildcard, or "." when there is none.</summary>
    public static string BaseDirectory(string pattern)
    {
        int prefix = DevicePrefix(pattern);
        if (prefix > 0)
        {
            // Joined with the prefix's own separator: Windows does not normalize a \\?\ path, so "/"
            // there is part of a name.
            string rest = pattern[prefix..];
            var parts = Split(rest);
            int n = 0;
            while (n < parts.Length && !HasWildcard(parts[n]))
                n++;
            char sep = pattern[0];
            string head = string.Join(sep, parts, 0, n);
            // "C:" alone is the current directory on drive C; the root needs its separator.
            if (n == 1 && head.Length == 2 && head[1] == ':')
                head += sep;
            return pattern[..prefix] + head;
        }
        var segments = Split(pattern);
        int i = 0;
        while (i < segments.Length && !HasWildcard(segments[i]))
            i++;
        if (i == 0)
            return Path.IsPathRooted(pattern) ? Path.GetPathRoot(pattern)! : ".";
        string dir = string.Join('/', segments, 0, i);
        // "C:" alone is the current directory on drive C; the root needs its separator.
        if (dir.Length == 2 && char.IsAsciiLetter(pattern[0]) && pattern[1] == ':')
            return dir + "/";
        if (pattern.StartsWith("//") || pattern.StartsWith("\\\\"))
            return "//" + dir;
        return pattern.StartsWith('/') ? "/" + dir : dir;
    }

    /// <summary>How many directory levels under <see cref="BaseDirectory"/> can hold a match: all of them with a <c>**</c>, else one per segment of the wildcard part.</summary>
    public static int Levels(string pattern)
    {
        var segments = Split(pattern[DevicePrefix(pattern)..]);
        int i = 0;
        while (i < segments.Length && !HasWildcard(segments[i]))
            i++;
        for (int j = i; j < segments.Length; j++)
        {
            if (segments[j] == "**")
                return int.MaxValue;
        }
        return segments.Length - i;
    }

    /// <summary>Whether <paramref name="relative"/> (to <see cref="BaseDirectory"/>) matches the wildcard part.</summary>
    public static bool Matches(string pattern, string relative)
    {
        var segments = Split(pattern[DevicePrefix(pattern)..]);
        int i = 0;
        while (i < segments.Length && !HasWildcard(segments[i]))
            i++;
        return MatchSegments(segments.AsSpan(i), Split(relative));
    }

    /// <summary>4 for a leading <c>\\?\</c>, <c>\\.\</c>, <c>//?/</c> or <c>//./</c>, else 0.</summary>
    private static int DevicePrefix(string path) =>
        path.Length >= 4 && path[0] is '/' or '\\' && path[1] is '/' or '\\' && path[2] is '?' or '.' && path[3] is '/' or '\\' ? 4 : 0;

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
