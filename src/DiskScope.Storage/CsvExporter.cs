using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Threading;

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
        writer.Write(',');
        WriteField(writer, index.GetKind(id).ToString());
        writer.Write(',');
        WriteNumber(writer, index.GetLogicalSize(id));
        writer.Write(',');
        WriteNumber(writer, index.GetAllocatedSize(id));
        writer.Write(',');
        WriteKnownNumber(writer, index.GetKnownTotalSize(id, SizeKind.Logical));
        writer.Write(',');
        WriteKnownNumber(writer, index.GetKnownTotalSize(id, SizeKind.Allocated));
        writer.Write(',');
        WriteNumber(writer, index.GetRecursiveFileCount(id));
        writer.Write(',');
        WriteNumber(writer, index.GetRecursiveDirectoryCount(id));
        writer.Write(',');
        WriteDate(writer, index.GetLastWriteUtc(id));
        writer.Write(',');
        WriteDate(writer, index.GetSubtreeLastWriteUtc(id));
        writer.Write(',');

        NodeStatus status = index.GetStatus(id);
        if (status != NodeStatus.None)
        {
            WriteField(writer, status.ToString());
        }

        writer.Write("\r\n");
    }

    /// <summary>Writes dirPath, an optional separator, then name, as one quoted field when needed.</summary>
    private static void WritePath(TextWriter writer, ReadOnlySpan<char> dirPath, bool separator, ReadOnlySpan<char> name)
    {
        bool quote = NeedsQuoting(dirPath) || NeedsQuoting(name);
        if (quote)
        {
            writer.Write('"');
        }

        WriteEscaped(writer, dirPath);
        if (separator)
        {
            writer.Write('\\');
        }

        WriteEscaped(writer, name);
        if (quote)
        {
            writer.Write('"');
        }
    }

    private static void WriteField(TextWriter writer, ReadOnlySpan<char> value)
    {
        bool quote = NeedsQuoting(value);
        if (quote)
        {
            writer.Write('"');
        }

        WriteEscaped(writer, value);
        if (quote)
        {
            writer.Write('"');
        }
    }

    private static void WriteNumber(TextWriter writer, long value)
    {
        Span<char> buffer = stackalloc char[20];
        value.TryFormat(buffer, out int written, ReadOnlySpan<char>.Empty, CultureInfo.InvariantCulture);
        writer.Write(buffer.Slice(0, written));
    }

    /// <summary>An unknown total is left empty, never written as 0.</summary>
    private static void WriteKnownNumber(TextWriter writer, long? value)
    {
        if (value is { } known)
        {
            WriteNumber(writer, known);
        }
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

    private static bool NeedsQuoting(ReadOnlySpan<char> value)
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
    private static void WriteEscaped(TextWriter writer, ReadOnlySpan<char> value)
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
}
