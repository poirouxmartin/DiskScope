using System.Collections.Generic;
using System.IO;
using DiskScope.App.Services;
using DiskScope.App.ViewModels;

namespace DiskScope.Tests.App;

public sealed class UxRefreshTests
{
    private const long Gb = 1024L * 1024 * 1024;

    [Fact]
    public void DriveCard_KnownSize_ComputesUsageAndText()
    {
        var card = new DriveCardViewModel(new DriveSnapshot("C:\\", "Windows", DriveType.Fixed, 1000 * Gb, 183 * Gb));

        Assert.Equal("C:", card.Name);
        Assert.Equal("C: Windows", card.Title);
        Assert.Equal(81.7, card.UsedPercent!.Value, 1);
        Assert.Equal("81.7 % used", card.UsedText);
        Assert.Equal("183.0 GB free of 1000.0 GB", card.FreeText);
        Assert.True(card.IsWarning);
        Assert.False(card.IsCritical);
    }

    [Fact]
    public void DriveCard_UnknownSize_IsNeverShownAsZero()
    {
        var card = new DriveCardViewModel(new DriveSnapshot("E:\\", null, DriveType.Removable, null, null));

        Assert.Null(card.UsedPercent);
        Assert.False(card.IsSizeKnown);
        Assert.Equal("n/a", card.UsedText);
        Assert.DoesNotContain("0", card.FreeText);
        Assert.Equal("E: Removable drive", card.Title);
        Assert.False(card.IsWarning);
        Assert.False(card.IsCritical);
    }

    [Theory]
    [InlineData(79.9, false, false)]
    [InlineData(80.0, true, false)]
    [InlineData(89.9, true, false)]
    [InlineData(90.0, false, true)]
    [InlineData(100.0, false, true)]
    public void DriveCard_Thresholds(double usedPercent, bool warning, bool critical)
    {
        long total = 1000 * Gb;
        long free = total - (long)(total * usedPercent / 100);
        var card = new DriveCardViewModel(new DriveSnapshot("D:\\", "Data", DriveType.Fixed, total, free));

        Assert.Equal(warning, card.IsWarning);
        Assert.Equal(critical, card.IsCritical);
    }

    [Fact]
    public void DriveCard_InconsistentSizes_AreUnknown()
    {
        var card = new DriveCardViewModel(new DriveSnapshot("D:\\", "Data", DriveType.Fixed, 10 * Gb, 20 * Gb));

        Assert.Null(card.UsedPercent);
    }

    [Fact]
    public void Storage_StartScreen_ListsDrives_UntilAnIndexIsLoaded()
    {
        var drives = new List<DriveSnapshot>
        {
            new("C:\\", "Windows", DriveType.Fixed, 100 * Gb, 20 * Gb),
            new("D:\\", "Data", DriveType.Fixed, 200 * Gb, 150 * Gb),
        };
        var vm = new StorageViewModel(new NullShell(), () => drives);

        Assert.True(vm.ShowStartScreen);
        Assert.Equal(new[] { "C: Windows", "D: Data" }, vm.Drives.Select(d => d.Title).ToArray());

        var builder = new DiskScope.Storage.NodeIndexBuilder();
        builder.AddRoot("C:\\data");
        vm.LoadIndex(builder.Build());

        Assert.False(vm.ShowStartScreen);
    }

    [Fact]
    public void Storage_ChooseFolder_Cancelled_KeepsStartScreen()
    {
        var vm = new StorageViewModel(new NullShell(), () => []);

        vm.ChooseFolderCommand.Execute(null);

        Assert.True(vm.ShowStartScreen);
        Assert.False(vm.IsScanning);
    }

    [Fact]
    public void FileRow_SplitsNameAndFolder()
    {
        var row = new FileRowViewModel(1, "C:\\data\\sub\\big.iso", 10, "10 B");

        Assert.Equal("big.iso", row.Name);
        Assert.Equal("C:\\data\\sub", row.Folder);
    }

    [Theory]
    [InlineData(0, 0, 0, 0, false, "Monitoring starting")]
    [InlineData(6, 6, 0, 0, true, "Monitoring OK")]
    [InlineData(6, 6, 3, 0, true, "Monitoring OK (3 missed readings)")]
    [InlineData(5, 6, 0, 0, false, "1 of 6 monitors unavailable")]
    [InlineData(6, 6, 0, 2, false, "History not saved (2 write failures)")]
    public void DescribeHealth_PlainLanguage(int running, int total, long failed, long store, bool healthy, string text)
    {
        (bool h, string t) = MonitoringViewModel.DescribeHealth(running, total, failed, store);

        Assert.Equal(healthy, h);
        Assert.Equal(text, t);
    }

    private sealed class NullShell : IShellActions
    {
        public void CopyText(string text) { }

        public void ShowInExplorer(string path) { }

        public string? AskSavePath(string suggestedFileName) => null;
    }
}
