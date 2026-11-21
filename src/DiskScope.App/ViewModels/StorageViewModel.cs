using System.Collections.ObjectModel;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Input;
using DiskScope.App.Formatting;
using DiskScope.App.Services;
using DiskScope.Storage;
using DiskScope.Scanner;
using DiskScope.Scanner.Standard;

namespace DiskScope.App.ViewModels;

public sealed class StorageViewModel : ObservableObject
{
    private const int MaxErrorsInSummary = 20;
    private const int MaxLargestFiles = 1000;

    // First provisional tree after 2 s; the engine doubles the delay after each one (see ScanOptions).
    private static readonly TimeSpan PartialResultDelay = TimeSpan.FromSeconds(2);

    private readonly IShellActions _shell;
    private readonly Func<IReadOnlyList<DriveSnapshot>> _readDrives;
    private string _rootPath = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    private bool _isScanning;
    private string _statusText = "Ready";
    private string _summary = string.Empty;
    private CancellationTokenSource? _cts;
    private NodeIndex? _index;
    private SizeKind _sizeKind = SizeKind.Logical;
    private bool _isLogical = true;
    private bool _isAllocated;
    private ObservableCollection<NodeViewModel> _rootNodes = new();
    private ObservableCollection<FileRowViewModel> _largestFiles = new();
    private ObservableCollection<ExtensionRowViewModel> _extensions = new();
    private int _treemapRootId;
    private int _selectedNodeId = -1;
    private NodeViewModel? _revealedNode;

    public StorageViewModel(IShellActions? shell = null, Func<IReadOnlyList<DriveSnapshot>>? readDrives = null)
    {
        _shell = shell ?? new ShellActions();
        _readDrives = readDrives ?? DriveCardViewModel.ReadSystemDrives;

        ScanCommand = new AsyncRelayCommand(
            ScanAsync,
            ex => StatusText = $"Scan failed: {ex.Message}",
            () => !IsScanning);

        CancelCommand = new RelayCommand(() => _cts?.Cancel(), () => IsScanning);

        ExportCsvCommand = new AsyncRelayCommand(
            () =>
            {
                Task export = ExportCsvAsync();
                PendingExport = export;
                return export;
            },
            ex => StatusText = $"Export failed: {ex.Message}",
            () => _index is not null && !IsScanning);

        CopyPathCommand = new ParameterCommand(CopyPath);
        OpenInExplorerCommand = new ParameterCommand(OpenInExplorer);
        Search = new SearchViewModel(() => _index, () => SizeKind);

        AnalyzeDriveCommand = new ParameterCommand(p =>
        {
            if (p is DriveCardViewModel drive && !IsScanning)
            {
                StartScan(drive.RootPath);
            }
        });
        ChooseFolderCommand = new ParameterCommand(_ =>
        {
            if (IsScanning)
            {
                return;
            }

            string? folder = _shell.AskFolder(RootPath);
            if (!string.IsNullOrEmpty(folder))
            {
                StartScan(folder);
            }
        });
        RefreshDrives();
    }

    /// <summary>Drives offered on the start screen, before any scan.</summary>
    public ObservableCollection<DriveCardViewModel> Drives { get; } = new();

    public ICommand AnalyzeDriveCommand { get; }

    public ICommand ChooseFolderCommand { get; }

    /// <summary>True until a first scan starts: the view shows the drive cards instead of empty panels.</summary>
    public bool ShowStartScreen => _index is null && !IsScanning;

    public void RefreshDrives()
    {
        Drives.Clear();
        foreach (DriveSnapshot drive in _readDrives())
        {
            Drives.Add(new DriveCardViewModel(drive));
        }
    }

    private void StartScan(string path)
    {
        RootPath = path;
        if (ScanCommand.CanExecute(null))
        {
            ScanCommand.Execute(null);
        }
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
                ExportCsvCommand.RaiseCanExecuteChanged();
                RaisePropertyChanged(nameof(ShowStartScreen));
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

    public SizeKind SizeKind
    {
        get => _sizeKind;
        set
        {
            if (!SetProperty(ref _sizeKind, value))
            {
                return;
            }

            SetProperty(ref _isLogical, value == SizeKind.Logical);
            SetProperty(ref _isAllocated, value == SizeKind.Allocated);
            Rebuild();
        }
    }

    public bool IsLogical
    {
        get => _isLogical;
        set
        {
            if (value)
            {
                SizeKind = SizeKind.Logical;
            }
        }
    }

    public bool IsAllocated
    {
        get => _isAllocated;
        set
        {
            if (value)
            {
                SizeKind = SizeKind.Allocated;
            }
        }
    }

    public ObservableCollection<NodeViewModel> RootNodes
    {
        get => _rootNodes;
        private set => SetProperty(ref _rootNodes, value);
    }

    public ObservableCollection<FileRowViewModel> LargestFiles
    {
        get => _largestFiles;
        private set => SetProperty(ref _largestFiles, value);
    }

    public ObservableCollection<ExtensionRowViewModel> Extensions
    {
        get => _extensions;
        private set => SetProperty(ref _extensions, value);
    }

    public AsyncRelayCommand ScanCommand { get; }

    public RelayCommand CancelCommand { get; }

    public AsyncRelayCommand ExportCsvCommand { get; }

    /// <summary>Set by <see cref="ExportCsvCommand"/> so tests can await the export.</summary>
    public Task? PendingExport { get; private set; }

    public ICommand CopyPathCommand { get; }

    public ICommand OpenInExplorerCommand { get; }

    public SearchViewModel Search { get; }

    public NodeIndex? Index => _index;

    public int TreemapRootId
    {
        get => _treemapRootId;
        set => SetProperty(ref _treemapRootId, value);
    }

    public int SelectedNodeId
    {
        get => _selectedNodeId;
        set
        {
            if (SetProperty(ref _selectedNodeId, value) && value >= 0)
            {
                RevealNode(value);
            }
        }
    }

    public void LoadIndex(NodeIndex index)
    {
        SetProperty(ref _index, index, nameof(Index));
        RaisePropertyChanged(nameof(ShowStartScreen));
        TreemapRootId = 0;
        Rebuild();
        ExportCsvCommand.RaiseCanExecuteChanged();
    }

    /// <summary>Expands the ancestors of <paramref name="id"/> in the tree and selects its node. No-op if not found.</summary>
    public void RevealNode(int id)
    {
        NodeIndex? index = _index;
        if (index is null || id < 0 || id >= index.Count)
        {
            return;
        }

        // Target first, then ancestors up to (excluding) the root; walked root-first below.
        var chain = new List<int>();
        for (int current = id; current != NodeIndex.RootId; current = index.GetParent(current))
        {
            chain.Add(current);
        }

        NodeViewModel node = RootNodes[0];
        for (int i = chain.Count - 1; i >= 0; i--)
        {
            node.IsExpanded = true;
            NodeViewModel? child = node.Children.FirstOrDefault(c => c.Id == chain[i]);
            if (child is null)
            {
                return;
            }

            node = child;
        }

        if (_revealedNode is not null)
        {
            _revealedNode.IsSelected = false;
        }

        node.IsSelected = true;
        _revealedNode = node;
    }

    private void Rebuild()
    {
        NodeIndex? index = _index;
        if (index is null)
        {
            return;
        }

        var root = new NodeViewModel(index, NodeIndex.RootId, _sizeKind, parentTotal: 0);
        root.IsExpanded = true;
        RootNodes = new ObservableCollection<NodeViewModel> { root };

        SizeKind kind = _sizeKind;
        LargestFiles = new ObservableCollection<FileRowViewModel>(
            index.GetLargestFiles(MaxLargestFiles, kind).Select(id => ToFileRow(index, id, kind)));

        long rootTotal = index.GetTotalSize(NodeIndex.RootId, kind);
        Extensions = new ObservableCollection<ExtensionRowViewModel>(
            ExtensionStats.Compute(index, kind).Select(stat => ToExtensionRow(stat, rootTotal)));

        _ = Search.RefreshAsync();
    }

    internal static FileRowViewModel ToFileRow(NodeIndex index, int id, SizeKind kind)
    {
        long size = index.GetTotalSize(id, kind);
        return new FileRowViewModel(id, index.GetFullPath(id), size, ByteFormatter.Format(size));
    }

    private static ExtensionRowViewModel ToExtensionRow(ExtensionStat stat, long rootTotal)
    {
        double percent = rootTotal > 0 ? stat.TotalSize * 100.0 / rootTotal : 0;
        return new ExtensionRowViewModel(
            stat.Extension,
            stat.FileCount,
            stat.TotalSize,
            ByteFormatter.Format(stat.TotalSize),
            FormattableString.Invariant($"{percent:F1} %"));
    }

    private void CopyPath(object? parameter)
    {
        if (TryGetFullPath(parameter, out string path))
        {
            try
            {
                _shell.CopyText(path);
            }
            catch (Exception ex) when (ex is System.Runtime.InteropServices.ExternalException or System.ComponentModel.Win32Exception or InvalidOperationException)
            {
                // Clipboard.SetText throws COMException when another process holds the clipboard.
                StatusText = $"Copy failed: {ex.Message}";
            }
        }
    }

    private void OpenInExplorer(object? parameter)
    {
        if (TryGetFullPath(parameter, out string path))
        {
            try
            {
                _shell.ShowInExplorer(path);
            }
            catch (Exception ex) when (ex is System.Runtime.InteropServices.ExternalException or System.ComponentModel.Win32Exception or InvalidOperationException)
            {
                StatusText = $"Could not open Explorer: {ex.Message}";
            }
        }
    }

    private bool TryGetFullPath(object? parameter, out string path)
    {
        path = string.Empty;
        NodeIndex? index = _index;
        int? id = parameter switch
        {
            FileRowViewModel row => row.Id,
            NodeViewModel { IsPlaceholder: false } node => node.Id,
            _ => null,
        };

        if (index is null || id is null)
        {
            return false;
        }

        path = index.GetFullPath(id.Value);
        return true;
    }

    private async Task ScanAsync()
    {
        IsScanning = true;
        var cts = new CancellationTokenSource();
        _cts = cts;
        try
        {
            var request = new ScanRequest(RootPath, new ScanOptions { PartialResultInterval = PartialResultDelay });
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
            ScanResult result = await engine.ScanAsync(request, new ProgressObserver(this, cts), cts.Token);
            Summary = BuildSummary(result);
            LoadIndex(result.Index);
            StatusText = $"Scan finished: {result.Outcome}";
        }
        finally
        {
            _cts = null;
            cts.Dispose();
            IsScanning = false;
        }
    }

    private async Task ExportCsvAsync()
    {
        if (_index is not { } index)
        {
            return;
        }

        if (_shell.AskSavePath("DiskScope-export.csv") is not { } path)
        {
            return;
        }

        long rows = await Task.Run(() =>
        {
            using var writer = new StreamWriter(path, false, new UTF8Encoding(true));
            return CsvExporter.Write(index, writer);
        });
        StatusText = $"Exported {rows:N0} rows to {path}";
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

    /// <summary>Shows a provisional index while a scan runs. The tree is rebuilt, so expansion state is not kept.</summary>
    internal void LoadPartialIndex(NodeIndex partial)
    {
        Summary = "PARTIAL RESULT: scan in progress, unvisited directories are flagged NotScanned.";
        LoadIndex(partial);
    }

    private sealed class ProgressObserver(StorageViewModel owner, CancellationTokenSource scan) : IScanObserver
    {
        public void OnPartialResult(NodeIndex partial) =>
            Application.Current?.Dispatcher.BeginInvoke(new Action(() =>
            {
                // A late partial must never overwrite the final result or a newer scan.
                if (ReferenceEquals(owner._cts, scan))
                {
                    owner.LoadPartialIndex(partial);
                }
            }));

        public void OnProgress(in ScanProgress progress)
        {
            // 'in' parameters cannot be captured by a lambda (CS1628), so copy to a local first.
            ScanProgress snapshot = progress;
            string text =
                $"Scanning: {snapshot.Nodes:N0} items, {ByteFormatter.Format(snapshot.LogicalBytes)}, {snapshot.Errors} errors";

            Application.Current?.Dispatcher.BeginInvoke(new Action(() => owner.StatusText = text));
        }
    }
}
