namespace DiskScope.Reports;

/// <summary>
/// RFC 4180 CSV: comma separator, CRLF line ends, fields quoted when they contain a comma, quote, CR or LF, quotes
/// doubled. Text that a spreadsheet would run as a formula is prefixed with a quote and quoted (see
/// <see cref="IsFormulaLike"/>). A null field is written empty (unknown), never "0". Formatting of numbers and dates is the caller's job
/// (invariant culture expected).
/// </summary>
public static class CsvTable
{
    public static int Write(TextWriter writer, IReadOnlyList<string> header, IEnumerable<IReadOnlyList<string?>> rows)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(header);
        ArgumentNullException.ThrowIfNull(rows);
        WriteRow(writer, header);
        int count = 0;
        foreach (IReadOnlyList<string?> row in rows)
        {
            if (row.Count != header.Count)
            {
                throw new ArgumentException($"Row {count} has {row.Count} fields, the header has {header.Count}.", nameof(rows));
            }

            WriteRow(writer, row);
            count++;
        }

        return count;
    }

    internal static void WriteRow(TextWriter writer, IReadOnlyList<string?> fields)
    {
        for (int i = 0; i < fields.Count; i++)
        {
            if (i > 0)
            {
                writer.Write(',');
            }

            string? f = fields[i];
            if (f is null)
            {
                continue;
            }

            if (IsFormulaLike(f))
            {
                // Process names, command lines and registry values are third-party text. A spreadsheet would run
                // "=..." as a formula, so it gets a leading quote, which makes it plain text.
                writer.Write("\"'");
                writer.Write(f.Replace("\"", "\"\"", StringComparison.Ordinal));
                writer.Write('"');
            }
            else if (f.AsSpan().IndexOfAny(",\"\r\n") >= 0)
            {
                writer.Write('"');
                writer.Write(f.Replace("\"", "\"\"", StringComparison.Ordinal));
                writer.Write('"');
            }
            else
            {
                writer.Write(f);
            }
        }

        writer.Write("\r\n");
    }

    /// <summary>
    /// True when a spreadsheet could read the field as a formula (OWASP CSV injection: leading = + - @ tab or CR).
    /// Invariant-culture numbers such as "-12.5" are data, not formulas, and stay untouched.
    /// </summary>
    internal static bool IsFormulaLike(string field) =>
        field.Length > 0
        && field[0] is '=' or '+' or '-' or '@' or '\t' or '\r'
        && !double.TryParse(field, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out _);
}
