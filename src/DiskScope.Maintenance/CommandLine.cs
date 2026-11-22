namespace DiskScope.Maintenance;

/// <summary>Extracts the executable from a start-up command line ("C:\a b\x.exe" -arg, %VAR%\x.exe /s, rundll32 ...).</summary>
public static class CommandLine
{
    private static readonly string[] Extensions = [".exe", ".com", ".bat", ".cmd", ".lnk", ".scr", ".ps1", ".vbs", ".js", ".dll", ".msc", ".cpl"];

    /// <summary>
    /// Returns the executable path, environment variables expanded, or null for an empty command.
    /// A quoted command takes the quoted part (a doubled quote, as in ""C:\a b\x.exe"", is tolerated). An unquoted
    /// one whose first token has no directory separator is a bare program name (cmd, rundll32.exe): that token.
    /// Otherwise the shortest prefix ending with a known extension (Windows itself tries prefixes for unquoted paths
    /// with spaces), else the first token. "cmd /c X" and "cmd /k X" give the executable of X.
    /// </summary>
    public static string? ExtractExecutable(string? command, Func<string, string>? expand = null)
    {
        expand ??= Environment.ExpandEnvironmentVariables;
        string? exe = Split(command, expand, out string args);
        string name = exe is null ? string.Empty : Path.GetFileName(exe);
        if (name.Equals("cmd", StringComparison.OrdinalIgnoreCase) || name.Equals("cmd.exe", StringComparison.OrdinalIgnoreCase))
        {
            // Switches before /c or /k (/q, /d, /s, /e:on...) are skipped; the payload is what follows /c or /k.
            while (args.StartsWith('/'))
            {
                int sp = args.IndexOf(' ');
                string sw = sp < 0 ? args : args[..sp];
                args = sp < 0 ? string.Empty : args[(sp + 1)..].TrimStart();
                if (sw.Equals("/c", StringComparison.OrdinalIgnoreCase) || sw.Equals("/k", StringComparison.OrdinalIgnoreCase))
                {
                    return Split(args, expand, out _) ?? exe;
                }
            }
        }

        return exe;
    }

    /// <summary>Command line of a task action: the path is quoted unless it is quoted already (no doubled quotes).</summary>
    public static string Join(string? path, string? arguments)
    {
        string p = (path ?? string.Empty).Trim();
        return $"{(p.Length > 0 && p[0] != '"' ? $"\"{p}\"" : p)} {arguments}".Trim();
    }

    private static string? Split(string? command, Func<string, string> expand, out string args)
    {
        args = string.Empty;
        if (string.IsNullOrWhiteSpace(command))
        {
            return null;
        }

        string c = command.Trim();
        if (c[0] == '"')
        {
            int start = 0;
            while (start < c.Length && c[start] == '"')
            {
                start++;
            }

            int end = c.IndexOf('"', start);
            string quoted = (end >= 0 ? c[start..end] : c[start..]).Trim();
            args = end >= 0 ? c[end..].TrimStart('"').Trim() : string.Empty;
            return quoted.Length == 0 ? null : expand(quoted);
        }

        string expanded = expand(c);
        int space = expanded.IndexOf(' ');
        if ((space < 0 ? expanded : expanded[..space]).IndexOfAny(['\\', '/']) < 0)
        {
            args = space < 0 ? string.Empty : expanded[(space + 1)..].Trim();
            return space < 0 ? expanded : expanded[..space];
        }

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

        int cut = best > 0 ? best : space < 0 ? expanded.Length : space;
        args = expanded[cut..].Trim();
        return expanded[..cut];
    }
}
