using DiskScope.Core;

namespace DiskScope.Reports;

/// <summary>
/// RFC 4180 CSV: comma separator, CRLF line ends, fields quoted when they contain a comma, quote, CR or LF, quotes
/// doubled. A null field is written empty (unknown), never "0". Formula-like fields (leading = + - @ tab or CR) are
/// neutralized with a leading apostrophe (see <see cref="CsvWriter"/>). Formatting of numbers and dates is the
/// caller's job (invariant culture expected).
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

    internal static void WriteRow(TextWriter writer, IReadOnlyList<string?> fields) => CsvWriter.WriteRow(writer, fields);
}
