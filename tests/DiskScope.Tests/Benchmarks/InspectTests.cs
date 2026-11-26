using System.IO;
using DiskScope.Benchmarks;
using DiskScope.Storage;

namespace DiskScope.Tests.Benchmarks;

/// <summary>The inspect command output: denied directories listed by path, one node described, unknowns never 0.</summary>
public sealed class InspectTests
{
    [Fact]
    public void ListsEveryErrorWithItsPath()
    {
        List<string> lines = Commands.InspectLines(Build(out _), null, out bool found);

        Assert.True(found);
        Assert.Equal("Errors:  1", lines[0]);
        Assert.Equal(@"  AccessDenied 5 C:\data\locked", lines[1]);
        Assert.Equal(2, lines.Count);
    }

    [Fact]
    public void DeniedNode_ShowsUnknownSizesAndLowerBoundNote()
    {
        List<string> lines = Commands.InspectLines(Build(out _), @"C:\DATA\locked", out bool found);

        Assert.True(found);
        Assert.Contains(@"Node:    C:\data\locked (Directory)", lines);
        Assert.Contains("  logical bytes:      n/a", lines);
        Assert.Contains("  allocated bytes:    n/a", lines);
        Assert.Contains("  incomplete: sizes, counts and the subtree date are lower bounds.", lines);
    }

    [Fact]
    public void ReadableNode_ShowsSizesAndSubtreeDate()
    {
        List<string> lines = Commands.InspectLines(Build(out _), @"C:\data\dir", out bool found);

        Assert.True(found);
        Assert.Contains("  logical bytes:      42", lines);
        Assert.Contains("  last write:         n/a", lines);
        Assert.Contains("  subtree last write: 2024-03-05T07:08:09Z", lines);
        Assert.DoesNotContain(lines, l => l.StartsWith("  incomplete", StringComparison.Ordinal));
    }

    [Fact]
    public void MissingNode_IsReportedNotFound()
    {
        List<string> lines = Commands.InspectLines(Build(out _), @"C:\data\nope", out bool found);

        Assert.False(found);
        Assert.Equal(@"Node not found: C:\data\nope", lines[^1]);
    }

    private static NodeIndex Build(out int locked)
    {
        var builder = new NodeIndexBuilder();
        int root = builder.AddRoot(@"C:\data");
        locked = builder.Add(root, "locked", NodeKind.Directory, FileAttributes.Directory, 0, 0, 0, 0);
        builder.AddError(new ScanError(locked, ScanErrorKind.AccessDenied, 5, "Access is denied."));
        int dir = builder.Add(root, "dir", NodeKind.Directory, FileAttributes.Directory, 0, 0, 0, 0);
        long ticks = new DateTime(2024, 3, 5, 7, 8, 9, DateTimeKind.Utc).Ticks;
        builder.Add(dir, "f.bin", NodeKind.File, FileAttributes.Normal, 42, 4096, ticks, 0);
        return builder.Build();
    }
}
