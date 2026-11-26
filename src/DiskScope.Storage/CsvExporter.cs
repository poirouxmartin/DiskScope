using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Threading;
using DiskScope.Core;

namespace DiskScope.Storage;

/// <summary>
/// Streams a <see cref="NodeIndex"/> as RFC 4180 CSV, one row per node, depth-first.
/// </summary>
/// <remarks>
/// Written for trees with millions of nodes. A row's path is written straight to the writer from the parent
/// directory path and the child name, so files never get a path string. Only directories get one, and only
/// while they are open on the traversal stack.
/// </remarks>
public static class CsvExporter
{
    public const string Header = "Path,Kind,LogicalSize,AllocatedSize,TotalLogicalSize,TotalAllocatedSize,RecursiveFiles,RecursiveDirectories,LastWriteUtc,SubtreeLastWriteUtc,Status";

    private const int CancellationCheckInterval = 4096;
    private const string DateFormat = "yyyy-MM-dd'T'HH:mm:ss'Z'";

    /// <summary>
    /// Writes the header, then one row per node (root included) in depth-first order, children in ascending id order.
    /// Returns the number of rows written, excluding the header.
    /// </summary>
    public static long Write(NodeIndex index, TextWriter writer, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(index);
        ArgumentNullException.ThrowIfNull(writer);

        writer.Write(Header);
        writer.Write("\r\n");

        cancellationToken.ThrowIfCancellationRequested();
        long rows = 0;
        WritePath(writer, ReadOnlySpan<char>.Empty, false, index.GetName(NodeIndex.RootId));
        WriteColumns(index, writer, NodeIndex.RootId);
        rows++;

        // Open ancestors with the cursor of their next child. Only directories are stacked.
        var ancestors = new Stack<(int Id, string Path)>();
        var cursors = new Stack<int>();
        int dirId = NodeIndex.RootId;
        string dirPath = index.GetNameString(NodeIndex.RootId);
        int cursor = 0;

        while (true)
        {
            ReadOnlySpan<int> children = index.GetChildren(dirId);
            if (cursor < children.Length)
            {
                if (rows % CancellationCheckInterval == 0)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                }

                int child = children[cursor++];
                ReadOnlySpan<char> name = index.GetName(child);
                bool separator = dirPath.Length == 0 || dirPath[dirPath.Length - 1] != '\\';
                WritePath(writer, dirPath, separator, name);
                WriteColumns(index, writer, child);
                rows++;

                if (index.GetKind(child) == NodeKind.Directory)
                {
                    ancestors.Push((dirId, dirPath));
                    cursors.Push(cursor);
                    dirPath = string.Concat(dirPath, separator ? "\\" : string.Empty, name.ToString());
                    dirId = child;
                    cursor = 0;
                }
            }
            else if (ancestors.Count > 0)
            {
                (dirId, dirPath) = ancestors.Pop();
                cursor = cursors.Pop();
            }
            else
            {
                return rows;
            }
        }
    }

    private static void WriteColumns(NodeIndex index, TextWriter writer, int id)
    {
        NodeStatus status = index.GetStatus(id);

        // When nothing of the node itself could be read (own error), a 0 means unknown, not zero: the cell stays
        // empty, as in the UI (ByteFormatter.FormatNodeSize). A non-zero value is a lower bound, kept as is and
        // flagged by the Status column.
        bool zeroIsUnknown = (status & NodeStatusExtensions.OwnErrorMask) != 0;

        writer.Write(',');
        CsvWriter.WriteField(writer, index.GetKind(id).ToString());
        writer.Write(',');
        WriteNumber(writer, index.GetLogicalSize(id), zeroIsUnknown);
        writer.Write(',');
        WriteNumber(writer, index.GetAllocatedSize(id), zeroIsUnknown);
        writer.Write(',');
        WriteNumber(writer, index.GetTotalLogicalSize(id), zeroIsUnknown);
        writer.Write(',');
        WriteNumber(writer, index.GetTotalAllocatedSize(id), zeroIsUnknown);
        writer.Write(',');
        WriteNumber(writer, index.GetRecursiveFileCount(id), zeroIsUnknown);
        writer.Write(',');
        WriteNumber(writer, index.GetRecursiveDirectoryCount(id), zeroIsUnknown);
        writer.Write(',');
        WriteDate(writer, index.GetLastWriteUtc(id));
        writer.Write(',');
        WriteDate(writer, index.GetSubtreeLastWriteUtc(id));
        writer.Write(',');

        if (status != NodeStatus.None)
        {
            CsvWriter.WriteField(writer, status.ToString());
        }

        writer.Write("\r\n");
    }

    /// <summary>Writes dirPath, an optional separator, then name, as one quoted field when needed.</summary>
    private static void WritePath(TextWriter writer, ReadOnlySpan<char> dirPath, bool separator, ReadOnlySpan<char> name)
    {
        // Rare case: a path starting like a formula gets the neutralizing field writer, at the cost of a string.
        char first = dirPath.Length > 0 ? dirPath[0] : name.Length > 0 ? name[0] : '\0';
        if (first is '=' or '+' or '-' or '@' or '\t' or '\r')
        {
            CsvWriter.WriteField(writer, string.Concat(dirPath, separator ? "\\" : string.Empty, name));
            return;
        }

        bool quote = CsvWriter.NeedsQuoting(dirPath) || CsvWriter.NeedsQuoting(name);
        if (quote)
        {
            writer.Write('"');
        }

        CsvWriter.WriteEscaped(writer, dirPath);
        if (separator)
        {
            writer.Write('\\');
        }

        CsvWriter.WriteEscaped(writer, name);
        if (quote)
        {
            writer.Write('"');
        }
    }

    private static void WriteNumber(TextWriter writer, long value, bool zeroIsUnknown)
    {
        if (value == 0 && zeroIsUnknown)
        {
            return;
        }

        Span<char> buffer = stackalloc char[20];
        value.TryFormat(buffer, out int written, ReadOnlySpan<char>.Empty, CultureInfo.InvariantCulture);
        writer.Write(buffer.Slice(0, written));
    }

    private static void WriteDate(TextWriter writer, DateTime utc)
    {
        // Ticks 0 means unknown. It is never written as 0001-01-01.
        if (utc.Ticks == 0)
        {
            return;
        }

        Span<char> buffer = stackalloc char[32];
        utc.TryFormat(buffer, out int written, DateFormat, CultureInfo.InvariantCulture);
        writer.Write(buffer.Slice(0, written));
    }
}
