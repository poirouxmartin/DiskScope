namespace DiskScope.Maintenance;

/// <summary>Extracts the executable from a start-up command line ("C:\a b\x.exe" -arg, %VAR%\x.exe /s, rundll32 ...).</summary>
public static class CommandLine
{
    private static readonly string[] Extensions = [".exe", ".com", ".bat", ".cmd", ".lnk", ".scr", ".ps1", ".vbs", ".js"];

    /// <summary>
    /// Returns the executable path, environment variables expanded, or null for an empty command.
    /// A quoted command takes the quoted part. An unquoted one takes the shortest prefix ending with a known
    /// extension (Windows itself tries prefixes for unquoted paths with spaces); otherwise the first token.
    /// </summary>
    public static string? ExtractExecutable(string? command, Func<string, string>? expand = null)
    {
        if (string.IsNullOrWhiteSpace(command))
        {
            return null;
        }

        expand ??= Environment.ExpandEnvironmentVariables;
        string c = command.Trim();
        if (c[0] == '"')
        {
            int end = c.IndexOf('"', 1);
            string quoted = end >= 1 ? c[1..end] : c[1..];
            return quoted.Length == 0 ? null : expand(quoted);
        }

        string expanded = expand(c);
        int best = -1;
        foreach (string ext in Extensions)
        {
            int at = expanded.IndexOf(ext, StringComparison.OrdinalIgnoreCase);
            while (at >= 0)
            {
                int after = at + ext.Length;
                if (after == expanded.Length || expanded[after] == ' ')
                {
                    best = best < 0 ? after : Math.Min(best, after);
                    break;
                }

                at = expanded.IndexOf(ext, after, StringComparison.OrdinalIgnoreCase);
            }
        }

        if (best > 0)
        {
            return expanded[..best];
        }

        int space = expanded.IndexOf(' ');
        return space < 0 ? expanded : expanded[..space];
    }
}
