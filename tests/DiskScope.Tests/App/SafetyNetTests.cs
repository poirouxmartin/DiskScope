using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using DiskScope.App.Services;
using DiskScope.App.ViewModels;
using DiskScope.Maintenance;
using DiskScope.Storage;

namespace DiskScope.Tests.App;

public sealed class SafetyNetTests
{
    [Fact]
    public void ErrorLog_Write_AppendsTypeMessageAndSource()
    {
        string dir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "ds-errorlog-" + Guid.NewGuid().ToString("N"));
        try
        {
            var log = new ErrorLog(dir);

            string? path = log.Write("unit", new InvalidOperationException("boom"));

            Assert.Equal(log.FilePath, path);
            string text = File.ReadAllText(path!);
            Assert.Contains("unit", text);
            Assert.Contains("InvalidOperationException", text);
            Assert.Contains("boom", text);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void ErrorLog_UnwritableDirectory_ReturnsNullWithoutThrowing()
    {
        string file = System.IO.Path.GetTempFileName();
        try
        {
            var log = new ErrorLog(System.IO.Path.Combine(file, "sub"));

            Assert.Null(log.Write("unit", new InvalidOperationException("boom")));
        }
        finally
        {
            File.Delete(file);
        }
    }

    [Fact]
    public async Task AsyncRelayCommand_Throwing_ReportsErrorInsteadOfThrowing()
    {
        Exception? reported = null;
        var command = new AsyncRelayCommand(
            async () =>
            {
                await Task.Yield();
                throw new InvalidOperationException("boom");
            },
            ex => reported = ex);

        await command.ExecuteAsync();

        Assert.IsType<InvalidOperationException>(reported);
    }

    [Fact]
    public async Task AsyncRelayCommand_Cancelled_IsNotAnError()
    {
        Exception? reported = null;
        var command = new AsyncRelayCommand(
            async () =>
            {
                await Task.Yield();
                throw new OperationCanceledException();
            },
            ex => reported = ex);

        await command.ExecuteAsync();

        Assert.Null(reported);
    }

    [Fact]
    public async Task StorageExport_NonIoFailure_SetsStatusInsteadOfCrashing()
    {
        var vm = new StorageViewModel(new FakeShell { SavePath = "bad\0name.csv" });
        vm.LoadIndex(BuildIndex());

        await vm.ExportCsvCommand.ExecuteAsync();

        Assert.StartsWith("Export failed", vm.StatusText);
    }

    [Fact]
    public void CopyPath_ClipboardBusy_SetsStatus()
    {
        var vm = new StorageViewModel(new FakeShell { CopyFailure = new COMException("busy") });
        vm.LoadIndex(BuildIndex());

        vm.CopyPathCommand.Execute(vm.LargestFiles[0]);

        Assert.StartsWith("Copy failed", vm.StatusText);
    }

    [Fact]
    public void ProcessesTryRead_AnyException_ReturnsMessage()
    {
        ProcessesViewModel.Snapshot? snapshot = ProcessesViewModel.TryRead(() => throw new NullReferenceException("x"), out string? error);

        Assert.Null(snapshot);
        Assert.Contains("NullReferenceException", error!);
    }

    [Fact]
    public void ProcessesTryRead_Cancellation_IsRethrown()
    {
        Assert.Throws<OperationCanceledException>(() => ProcessesViewModel.TryRead(() => throw new OperationCanceledException(), out _));
    }

    [Fact]
    public async Task StartupRefresh_ReaderThrows_StatusSaysFailed()
    {
        var vm = new StartupViewModel(
            readStartup: () => throw new UnauthorizedAccessException("denied"),
            readServices: () => new InventoryResult<ServiceEntry>([], []));

        await vm.RefreshAsync();

        Assert.Contains("failed", vm.Status);
        Assert.Contains("denied", vm.Status);
        Assert.True(vm.RefreshCommand.CanExecute(null));
    }

    // root/ a.bin (file, 100 bytes), so one file row is available for the copy test.
    private static NodeIndex BuildIndex()
    {
        var builder = new NodeIndexBuilder();
        int root = builder.AddRoot("C:\\data");
        builder.Add(root, "a.bin", NodeKind.File, FileAttributes.Normal, 100, 4096, 0, 0);
        return builder.Build();
    }

    private sealed class FakeShell : IShellActions
    {
        public string? SavePath { get; init; }

        public Exception? CopyFailure { get; init; }

        public void CopyText(string text)
        {
            if (CopyFailure is not null)
            {
                throw CopyFailure;
            }
        }

        public void ShowInExplorer(string path)
        {
        }

        public string? AskSaveCsvPath(string suggestedFileName) => SavePath;
    }
}
