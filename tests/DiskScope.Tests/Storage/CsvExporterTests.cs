using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using DiskScope.Storage;

namespace DiskScope.Tests;

public sealed class CsvExporterTests
{
    [Fact]
    public void Header_IsExactColumnList()
    {
        string[] lines = Lines(Export(BuildSimple()));

        Assert.Equal("Path,Kind,LogicalSize,AllocatedSize,TotalLogicalSize,TotalAllocatedSize,RecursiveFiles,RecursiveDirectories,LastWriteUtc,Status", lines[0]);
    }

    [Fact]
    public void RootRow_IsFirstWithRootPath()
    {
        string[] lines = Lines(Export(BuildSimple()));

        Assert.Equal(3, lines.Length);
        Assert.StartsWith("C:\\data,Directory,", lines[1]);
    }

    [Fact]
    public void Rows_AreDepthFirstWithChildrenInIdOrder()
    {
        var builder = new NodeIndexBuilder();
        int root = builder.AddRoot("C:\\r");
        int dirA = builder.Add(root, "dA", NodeKind.Directory, FileAttributes.Directory, 0, 0, 0, 0);
        builder.Add(dirA, "a1.txt", NodeKind.File, FileAttributes.Normal, 1, 0, 0, 0);
        int dirB = builder.Add(root, "dB", NodeKind.Directory, FileAttributes.Directory, 0, 0, 0, 0);
        builder.Add(dirB, "b1.txt", NodeKind.File, FileAttributes.Normal, 1, 0, 0, 0);
        builder.Add(root, "z.txt", NodeKind.File, FileAttributes.Normal, 1, 0, 0, 0);

        string[] paths = Lines(Export(builder.Build())).Skip(1).Select(line => line.Split(',')[0]).ToArray();

        Assert.Equal(new[] { "C:\\r", "C:\\r\\dA", "C:\\r\\dA\\a1.txt", "C:\\r\\dB", "C:\\r\\dB\\b1.txt", "C:\\r\\z.txt" }, paths);
    }

    [Fact]
    public void Name_WithCommaAndQuote_IsQuotedWithDoubledQuotes()
    {
        var builder = new NodeIndexBuilder();
        int root = builder.AddRoot("C:\\data");
        builder.Add(root, "a,\"b.txt", NodeKind.File, FileAttributes.Normal, 1, 0, 0, 0);
        string[] lines = Lines(Export(builder.Build()));

        Assert.StartsWith(@"""C:\data\a,""""b.txt"",File,", lines[2]);
    }

    [Fact]
    public void LastWriteUtc_UnknownIsEmpty_KnownIsIso8601()
    {
        var builder = new NodeIndexBuilder();
        int root = builder.AddRoot("C:\\d");
        long knownTicks = new DateTime(2024, 3, 5, 7, 8, 9, DateTimeKind.Utc).Ticks;
        builder.Add(root, "known.txt", NodeKind.File, FileAttributes.Normal, 1, 0, knownTicks, 0);
        builder.Add(root, "unknown.txt", NodeKind.File, FileAttributes.Normal, 1, 0, 0, 0);
        string[] lines = Lines(Export(builder.Build()));

        string[] knownFields = lines[2].Split(',');
        string[] unknownFields = lines[3].Split(',');
        Assert.Equal("2024-03-05T07:08:09Z", knownFields[8]);
        Assert.Equal(string.Empty, unknownFields[8]);
        Assert.Equal(string.Empty, unknownFields[9]);
    }

    [Fact]
    public void Status_WithSeveralFlags_IsQuoted()
    {
        var builder = new NodeIndexBuilder();
        int root = builder.AddRoot("C:\\s");
        int locked = builder.Add(root, "locked", NodeKind.Directory, FileAttributes.Directory, 0, 0, 0, 0);
        builder.AddStatus(locked, NodeStatus.AccessDenied);
        string[] lines = Lines(Export(builder.Build()));

        Assert.EndsWith(",\"AccessDenied, SubtreeIncomplete\"", lines[2]);
    }

    [Fact]
    public void Numbers_AreInvariantEvenUnderFrenchCulture()
    {
        var builder = new NodeIndexBuilder();
        int root = builder.AddRoot("C:\\n");
        builder.Add(root, "big.bin", NodeKind.File, FileAttributes.Normal, 1234567, 4096, 0, 0);
        NodeIndex index = builder.Build();

        CultureInfo previous = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = new CultureInfo("fr-FR");
        try
        {
            string[] rootFields = Lines(Export(index))[1].Split(',');
            string[] childFields = Lines(Export(index))[2].Split(',');

            Assert.Equal("1234567", rootFields[4]);
            Assert.Equal("1234567", childFields[2]);
            Assert.Equal("4096", childFields[3]);
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    [Fact]
    public void Write_ReturnsRowCountExcludingHeader()
    {
        NodeIndex index = BuildSimple();
        using var writer = new StringWriter();

        long rows = CsvExporter.Write(index, writer);

        Assert.Equal((long)index.Count, rows);
        Assert.Equal(index.Count + 1, Lines(writer.ToString()).Length);
    }

    [Fact]
    public void RootWithTrailingSeparator_DoesNotDoubleTheSeparator()
    {
        var builder = new NodeIndexBuilder();
        int root = builder.AddRoot("C:\\");
        builder.Add(root, "x.txt", NodeKind.File, FileAttributes.Normal, 1, 0, 0, 0);
        string[] lines = Lines(Export(builder.Build()));

        Assert.Equal("C:\\x.txt", lines[2].Split(',')[0]);
    }

    [Fact]
    public void Write_WithCancelledToken_ThrowsOperationCanceledException()
    {
        var builder = new NodeIndexBuilder();
        int root = builder.AddRoot("C:\\big");
        for (int i = 0; i < 5000; i++)
        {
            builder.Add(root, "f" + i, NodeKind.File, FileAttributes.Normal, 1, 0, 0, 0);
        }

        NodeIndex index = builder.Build();
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        using var writer = new StringWriter();

        Assert.Throws<OperationCanceledException>(() => { CsvExporter.Write(index, writer, cts.Token); });
    }

    private static NodeIndex BuildSimple()
    {
        var builder = new NodeIndexBuilder();
        int root = builder.AddRoot("C:\\data");
        builder.Add(root, "a.txt", NodeKind.File, FileAttributes.Normal, 10, 4096, 0, 0);
        return builder.Build();
    }

    private static string Export(NodeIndex index)
    {
        using var writer = new StringWriter();
        CsvExporter.Write(index, writer);
        return writer.ToString();
    }

    private static string[] Lines(string csv) => csv.Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
}
