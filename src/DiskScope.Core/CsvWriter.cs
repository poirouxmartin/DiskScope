using System.Globalization;

namespace DiskScope.Core;

/// <summary>
/// RFC 4180 CSV field writer: comma separator, CRLF line ends, fields quoted when they contain a comma, quote, CR or
/// LF, quotes doubled. A null field is written empty (unknown), never "0". Fields that a spreadsheet could run as a
/// formula are neutralized.
/// </summary>
public static class CsvWriter
{
    public static bool NeedsQuoting(ReadOnlySpan<char> value)
    {
        foreach (char c in value)
        {
            if (c is ',' or '"' or '\r' or '\n')
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Writes the value with every quote doubled, in runs to avoid per-character calls.</summary>
    public static void WriteEscaped(TextWriter writer, ReadOnlySpan<char> value)
    {
        int start = 0;
        for (int i = 0; i < value.Length; i++)
        {
            if (value[i] == '"')
            {
                writer.Write(value.Slice(start, i - start + 1));
                writer.Write('"');
                start = i + 1;
            }
        }

        writer.Write(value.Slice(start));
    }

    /// <summary>
    /// True when a spreadsheet could read the field as a formula (OWASP CSV injection: leading = + - @ tab or CR).
    /// Invariant-culture numbers such as "-12.5" are data, not formulas, and stay untouched.
    /// </summary>
    public static bool IsFormulaLike(ReadOnlySpan<char> field) =>
        field.Length > 0 && field[0] is '=' or '+' or '-' or '@' or '\t' or '\r'
        && !double.TryParse(field, NumberStyles.Float, CultureInfo.InvariantCulture, out _);

    public static void WriteField(TextWriter writer, ReadOnlySpan<char> value)
    {
        if (IsFormulaLike(value))
        {
            // Third-party text such as process names, command lines and registry values: a spreadsheet would run
            // "=..." as a formula, so a leading apostrophe inside a quoted field makes it plain text.
            writer.Write("\"'");
            WriteEscaped(writer, value);
            writer.Write('"');
        }
        else if (NeedsQuoting(value))
        {
            writer.Write('"');
            WriteEscaped(writer, value);
            writer.Write('"');
        }
        else
        {
            writer.Write(value);
        }
    }

    public static void WriteRow(TextWriter writer, IReadOnlyList<string?> fields)
    {
        for (int i = 0; i < fields.Count; i++)
        {
            if (i > 0)
            {
                writer.Write(',');
            }

            string? f = fields[i];
            if (f is not null)
            {
                WriteField(writer, f);
            }
        }

        writer.Write("\r\n");
    }
}
