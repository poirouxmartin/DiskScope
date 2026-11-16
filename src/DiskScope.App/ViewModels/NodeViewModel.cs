using System.Collections.ObjectModel;
using System.Globalization;
using DiskScope.App.Formatting;
using DiskScope.Core;
using DiskScope.Storage;

namespace DiskScope.App.ViewModels;

public sealed class NodeViewModel : ObservableObject
{
    /// <summary>Stand-in child shown under a directory until its children are materialized.</summary>
    public static readonly NodeViewModel Placeholder = new();

    private readonly NodeIndex? _index;
    private readonly SizeKind _sizeKind;
    private bool _isExpanded;
    private bool _isSelected;
    private ObservableCollection<NodeViewModel> _children = new();

    public NodeViewModel(NodeIndex index, int id, SizeKind sizeKind, long parentTotal)
    {
        _index = index;
        _sizeKind = sizeKind;

        Id = id;
        Name = index.GetNameString(id);
        Kind = index.GetKind(id);
        NodeStatus status = index.GetStatus(id);
        TotalSize = index.GetTotalSize(id, sizeKind);
        TotalSizeText = ByteFormatter.FormatNodeSize(TotalSize, status);
        LogicalText = ByteFormatter.FormatNodeSize(index.GetTotalLogicalSize(id), status);
        AllocatedText = ByteFormatter.FormatNodeSize(index.GetTotalAllocatedSize(id), status);
        PercentOfParent = ComputePercentOfParent(id, TotalSize, parentTotal);
        // 0 of 0 is undefined: the bar stays at 0, the text says n/a.
        PercentText = id != NodeIndex.RootId && parentTotal <= 0
            ? MetricFormatter.Unknown
            : PercentOfParent.ToString("F1", CultureInfo.InvariantCulture) + " %";
        FileCount = index.GetRecursiveFileCount(id);

        IsIncomplete = status != NodeStatus.None;
        StatusText = IsIncomplete ? status.ToString() : string.Empty;

        if (index.GetChildren(id).Length > 0)
        {
            Children.Add(Placeholder);
        }
    }

    private NodeViewModel()
    {
    }

    public int Id { get; }

    public string Name { get; } = string.Empty;

    public NodeKind Kind { get; }

    public long TotalSize { get; }

    public string TotalSizeText { get; } = string.Empty;

    public string LogicalText { get; } = string.Empty;

    public string AllocatedText { get; } = string.Empty;

    public double PercentOfParent { get; }

    public string PercentText { get; } = string.Empty;

    public long FileCount { get; }

    public bool IsIncomplete { get; }

    public string StatusText { get; } = string.Empty;

    public ObservableCollection<NodeViewModel> Children
    {
        get => _children;
        private set => SetProperty(ref _children, value);
    }

    public bool ChildrenLoaded { get; private set; }

    public bool IsPlaceholder => _index is null;

    public bool IsExpanded
    {
        get => _isExpanded;
        set
        {
            SetProperty(ref _isExpanded, value);
            if (value && !ChildrenLoaded && _index is not null)
            {
                LoadChildren(_index);
            }
        }
    }

    public bool IsSelected
    {
        get => _isSelected;
        set => SetProperty(ref _isSelected, value);
    }

    /// <summary>UI Automation names tree items from ToString; keep it readable for screen readers and UI tests.</summary>
    public override string ToString() => Name;

    private void LoadChildren(NodeIndex index)
    {
        // Children are materialized only on expansion; the index arrays stay the single source of truth.
        // The collection is built first and published once: one change notification instead of one per child.
        int[] ids = index.GetChildrenSortedBySize(Id, _sizeKind);
        var loaded = new List<NodeViewModel>(ids.Length);
        foreach (int childId in ids)
        {
            loaded.Add(new NodeViewModel(index, childId, _sizeKind, TotalSize));
        }

        ChildrenLoaded = true;
        Children = new ObservableCollection<NodeViewModel>(loaded);
    }

    private static double ComputePercentOfParent(int id, long totalSize, long parentTotal)
    {
        if (id == NodeIndex.RootId || parentTotal <= 0)
        {
            return id == NodeIndex.RootId ? 100 : 0;
        }

        return 100.0 * totalSize / parentTotal;
    }
}
