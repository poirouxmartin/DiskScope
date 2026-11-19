using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using DiskScope.App.Services;
using DiskScope.App.ViewModels;
using DiskScope.Storage;

namespace DiskScope.Tests.App;

public sealed class SearchViewModelTests
{
    private static readonly long KnownTicks = new DateTime(2026, 6, 15, 12, 0, 0, DateTimeKind.Utc).Ticks;

    [Fact]
    public async Task ExtensionsText_Jpg_ReturnsMatchesSortedBySizeDescending()
    {
        NodeIndex index = BuildIndex();
        var vm = new SearchViewModel(() => index, () => SizeKind.Logical);
        vm.ExtensionsText = "jpg";

        await vm.RefreshAsync();

        Assert.Equal(
            new[] { "C:\\data\\b.JPG", "C:\\data\\sub\\d.jpg", "C:\\data\\a.jpg" },
            vm.Results.Select(r => r.Path).ToArray());
        Assert.Equal(new[] { 300L, 200L, 100L }, vm.Results.Select(r => r.Size).ToArray());
        Assert.Equal("3 matches", vm.ResultText);
    }

    [Fact]
    public async Task NameText_IsDebounced_AndMatchesByName()
    {
        NodeIndex index = BuildIndex();
        var vm = new SearchViewModel(() => index, () => SizeKind.Logical);

        vm.NameText = "b";
        await vm.PendingSearch!;

        Assert.Equal(new[] { "C:\\data\\b.JPG", "C:\\data\\sub" }, vm.Results.Select(r => r.Path).ToArray());
    }

    [Fact]
    public async Task MinSizeText_NotANumber_SetsInputErrorAndClearsResults()
    {
        NodeIndex index = BuildIndex();
        var vm = new SearchViewModel(() => index, () => SizeKind.Logical);
        vm.ExtensionsText = "jpg";
        await vm.RefreshAsync();
        Assert.NotEmpty(vm.Results);

        vm.MinSizeMbText = "abc";
        await vm.PendingSearch!;

        Assert.Equal("Min size: not a number", vm.InputError);
        Assert.Empty(vm.Results);
    }

    [Fact]
    public async Task EmptyCriteria_ShowsEnterCriteria()
    {
        NodeIndex index = BuildIndex();
        var vm = new SearchViewModel(() => index, () => SizeKind.Logical);

        await vm.RefreshAsync();

        Assert.Equal("Enter criteria", vm.ResultText);
        Assert.Empty(vm.Results);
    }

    [Fact]
    public async Task ModifiedRange_ExcludesUnknownDate_AndReportsCount()
    {
        NodeIndex index = BuildIndex();
        var vm = new SearchViewModel(() => index, () => SizeKind.Logical);
        vm.ModifiedFrom = new DateTime(2026, 1, 1);
        vm.ModifiedTo = new DateTime(2026, 12, 31);
        vm.Kind = KindFilter.Files;

        await vm.PendingSearch!;

        Assert.Equal("4 matches; 1 with unknown date excluded", vm.ResultText);
        Assert.DoesNotContain("C:\\data\\e", vm.Results.Select(r => r.Path));
    }

    [Fact]
    public async Task NoIndex_ShowsNoScanLoaded()
    {
        var vm = new SearchViewModel(() => null, () => SizeKind.Logical);

        await vm.RefreshAsync();

        Assert.Equal("No scan loaded", vm.ResultText);
    }

    [Fact]
    public void LoadIndex_FillsExtensionsWithLargestGroupFirst()
    {
        var vm = new StorageViewModel(new NullShell());

        vm.LoadIndex(BuildIndex());

        Assert.Equal("jpg", vm.Extensions[0].Extension);
        Assert.Equal(3L, vm.Extensions[0].FileCount);
        Assert.Equal(600L, vm.Extensions[0].TotalSize);
    }

    // a.jpg 100, b.JPG 300, c.txt 50, e (no extension, unknown date) 10, sub/d.jpg 200.
    private static NodeIndex BuildIndex()
    {
        var builder = new NodeIndexBuilder();
        int root = builder.AddRoot("C:\\data");
        builder.Add(root, "a.jpg", NodeKind.File, FileAttributes.Normal, 100, 4096, KnownTicks, 0);
        builder.Add(root, "b.JPG", NodeKind.File, FileAttributes.Normal, 300, 4096, KnownTicks, 0);
        builder.Add(root, "c.txt", NodeKind.File, FileAttributes.Normal, 50, 4096, KnownTicks, 0);
        builder.Add(root, "e", NodeKind.File, FileAttributes.Normal, 10, 4096, 0, 0);
        int sub = builder.Add(root, "sub", NodeKind.Directory, FileAttributes.Directory, 0, 0, KnownTicks, 0);
        builder.Add(sub, "d.jpg", NodeKind.File, FileAttributes.Normal, 200, 4096, KnownTicks, 0);
        return builder.Build();
    }

    private sealed class NullShell : IShellActions
    {
        public void CopyText(string text)
        {
        }

        public void ShowInExplorer(string path)
        {
        }

        public string? AskSavePath(string suggestedFileName) => null;
    }
}
