using System.Text;
using System.Windows;
using DiskScope.App.Formatting;
using DiskScope.Core;
using DiskScope.Scanner;
using DiskScope.Scanner.Standard;

namespace DiskScope.App.ViewModels;

public sealed class MainViewModel : ObservableObject
{
    private const int MaxErrorsInSummary = 20;

    private string _rootPath = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    private bool _isScanning;
    private string _statusText = "Ready";
    private string _summary = string.Empty;
    private CancellationTokenSource? _cts;

    public MainViewModel()
    {
        ScanCommand = new RelayCommand(
            async () =>
            {
                try
                {
                    await ScanAsync();
                }
                catch (Exception ex)
                {
                    StatusText = $"Scan failed: {ex.Message}";
                }
            },
            () => !IsScanning);

        CancelCommand = new RelayCommand(() => _cts?.Cancel(), () => IsScanning);
    }

    public string RootPath
    {
        get => _rootPath;
        set => SetProperty(ref _rootPath, value);
    }

    public bool IsScanning
    {
        get => _isScanning;
        private set
        {
            if (SetProperty(ref _isScanning, value))
            {
                ScanCommand.RaiseCanExecuteChanged();
                CancelCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public string StatusText
    {
        get => _statusText;
        set => SetProperty(ref _statusText, value);
    }

    public string Summary
    {
        get => _summary;
        private set => SetProperty(ref _summary, value);
    }

    public RelayCommand ScanCommand { get; }

    public RelayCommand CancelCommand { get; }

    private async Task ScanAsync()
    {
        IsScanning = true;
        var cts = new CancellationTokenSource();
        _cts = cts;
        try
        {
            var request = new ScanRequest(RootPath);
            var orchestrator = new ScanOrchestrator([new StandardScanEngine()]);

            IScanEngine engine;
            try
            {
                engine = orchestrator.SelectEngine(request);
            }
            catch (InvalidOperationException ex)
            {
                StatusText = ex.Message;
                return;
            }

            StatusText = "Scanning...";
            ScanResult result = await engine.ScanAsync(request, new ProgressObserver(this), cts.Token);
            Summary = BuildSummary(result);
            StatusText = $"Scan finished: {result.Outcome}";
        }
        finally
        {
            _cts = null;
            cts.Dispose();
            IsScanning = false;
        }
    }

    private static string BuildSummary(ScanResult result)
    {
        NodeIndex index = result.Index;
        long logical = index.GetTotalLogicalSize(NodeIndex.RootId);
        long allocated = index.GetTotalAllocatedSize(NodeIndex.RootId);
        bool partial = result.Outcome != ScanOutcome.Completed
            || (index.GetStatus(NodeIndex.RootId) & NodeStatus.SubtreeIncomplete) != 0;

        var sb = new StringBuilder();
        sb.AppendLine($"Engine:      {result.EngineName}");
        sb.AppendLine($"Outcome:     {result.Outcome}");
        sb.AppendLine($"Elapsed:     {result.Elapsed.TotalSeconds:N2} s");
        sb.AppendLine($"Logical:     {ByteFormatter.Format(logical)} ({logical:N0} bytes)");
        sb.AppendLine($"Allocated:   {ByteFormatter.Format(allocated)} ({allocated:N0} bytes)");
        sb.AppendLine($"Files:       {index.GetRecursiveFileCount(NodeIndex.RootId):N0}");
        sb.AppendLine($"Directories: {index.GetRecursiveDirectoryCount(NodeIndex.RootId):N0}");
        sb.AppendLine($"Errors:      {index.Errors.Count:N0}");

        if (partial)
        {
            sb.AppendLine("PARTIAL RESULT: one or more subtrees are incomplete.");
        }

        if (index.Errors.Count > 0)
        {
            sb.AppendLine();
            foreach (ScanError error in index.Errors.Take(MaxErrorsInSummary))
            {
                sb.AppendLine($"{error.Kind} {error.Win32Error} {index.GetFullPath(error.NodeId)}");
            }
        }

        return sb.ToString();
    }

    private sealed class ProgressObserver(MainViewModel owner) : IScanObserver
    {
        public void OnProgress(in ScanProgress progress)
        {
            // 'in' parameters cannot be captured by a lambda (CS1628), so copy to a local first.
            ScanProgress snapshot = progress;
            string text =
                $"Scanning: {snapshot.Nodes:N0} items, {ByteFormatter.Format(snapshot.LogicalBytes)}, {snapshot.Errors} errors";

            Application.Current.Dispatcher.BeginInvoke(new Action(() => owner.StatusText = text));
        }
    }
}
