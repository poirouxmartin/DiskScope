using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using DiskScope.App.Formatting;
using DiskScope.Storage;
using DiskScope.Storage.Treemap;

namespace DiskScope.App.Controls;

/// <summary>
/// Draws a treemap of a <see cref="NodeIndex"/> subtree. Layout comes from <see cref="TreemapLayout"/>.
/// </summary>
/// <remarks>
/// Everything is painted in OnRender by this single element. A scan can hold millions of nodes, and one WPF
/// control per file would make the visual tree the bottleneck. The layout is capped at maxItems rectangles,
/// and rectangles smaller than minArea are dropped inside TreemapLayout.Build.
/// </remarks>
public sealed class TreemapControl : FrameworkElement
{
    private const double MinOutlineArea = 16;

    // Gray ramp for directories, one step per depth level; deeper levels reuse the last brush.
    private static readonly SolidColorBrush[] DirectoryBrushes =
    [
        FrozenRgb(0x1E252D),
        FrozenRgb(0x252D36),
        FrozenRgb(0x2C3540),
        FrozenRgb(0x333D49),
        FrozenRgb(0x3A4552),
    ];

    // Files take a color from this palette, selected by a hash of the extension.
    private static readonly SolidColorBrush[] FilePalette =
    [
        FrozenRgb(0x4E79A7),
        FrozenRgb(0xF28E2B),
        FrozenRgb(0x59A14F),
        FrozenRgb(0xB07AA1),
        FrozenRgb(0x76B7B2),
        FrozenRgb(0xEDC948),
        FrozenRgb(0xFF9DA7),
        FrozenRgb(0x9C755F),
        FrozenRgb(0xBAB0AC),
        FrozenRgb(0x86BCB6),
        FrozenRgb(0xD37295),
        FrozenRgb(0xA0CBE8),
    ];

    private static readonly Pen OutlinePen = FrozenPen(FrozenBrush(Color.FromArgb(0x80, 0x0E, 0x13, 0x17)), 0.5);
    private static readonly Pen SelectedPen = FrozenPen(FrozenRgb(0x57B7E8), 2);
    private static readonly SolidColorBrush CanvasBrush = FrozenRgb(0x161B21);

    public static readonly DependencyProperty IndexProperty =
        DependencyProperty.Register(nameof(Index), typeof(NodeIndex), typeof(TreemapControl),
            new PropertyMetadata(null, OnLayoutInputChanged));

    public static readonly DependencyProperty RootIdProperty =
        DependencyProperty.Register(nameof(RootId), typeof(int), typeof(TreemapControl),
            new FrameworkPropertyMetadata(0, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnLayoutInputChanged));

    public static readonly DependencyProperty SizeKindProperty =
        DependencyProperty.Register(nameof(SizeKind), typeof(SizeKind), typeof(TreemapControl),
            new PropertyMetadata(DiskScope.Storage.SizeKind.Logical, OnLayoutInputChanged));

    public static readonly DependencyProperty SelectedIdProperty =
        DependencyProperty.Register(nameof(SelectedId), typeof(int), typeof(TreemapControl),
            new FrameworkPropertyMetadata(-1, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnSelectionChanged));

    private TreemapItem[] _items = [];
    private int _tooltipId = -1;

    public TreemapControl()
    {
        Focusable = false;
        SnapsToDevicePixels = true;
        ToolTipService.SetInitialShowDelay(this, 300);
    }

    public NodeIndex? Index
    {
        get => (NodeIndex?)GetValue(IndexProperty);
        set => SetValue(IndexProperty, value);
    }

    public int RootId
    {
        get => (int)GetValue(RootIdProperty);
        set => SetValue(RootIdProperty, value);
    }

    public SizeKind SizeKind
    {
        get => (SizeKind)GetValue(SizeKindProperty);
        set => SetValue(SizeKindProperty, value);
    }

    public int SelectedId
    {
        get => (int)GetValue(SelectedIdProperty);
        set => SetValue(SelectedIdProperty, value);
    }

    protected override void OnRenderSizeChanged(SizeChangedInfo sizeInfo)
    {
        base.OnRenderSizeChanged(sizeInfo);
        Relayout();
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        // Single DrawingContext for the whole treemap (see class remarks).
        drawingContext.DrawRectangle(CanvasBrush, null, new Rect(0, 0, ActualWidth, ActualHeight));

        NodeIndex? index = Index;
        if (index is not null)
        {
            foreach (TreemapItem item in _items)
            {
                Brush fill = item.Kind == NodeKind.Directory
                    ? DirectoryBrushes[Math.Clamp(item.Depth, 0, DirectoryBrushes.Length - 1)]
                    : FilePalette[ExtensionPaletteIndex(NodeSearch.GetExtension(index.GetName(item.NodeId)))];

                Pen? outline = item.Rect.Area >= MinOutlineArea ? OutlinePen : null;
                drawingContext.DrawRectangle(fill, outline, ToRect(item.Rect));
            }
        }

        int selectedId = SelectedId;
        if (selectedId >= 0)
        {
            foreach (TreemapItem item in _items)
            {
                if (item.NodeId == selectedId)
                {
                    drawingContext.DrawRectangle(null, SelectedPen, ToRect(item.Rect));
                    break;
                }
            }
        }
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);

        NodeIndex? index = Index;
        if (index is null)
        {
            return;
        }

        Point p = e.GetPosition(this);
        int hit = TreemapLayout.HitTest(_items, p.X, p.Y);
        if (hit < 0)
        {
            return;
        }

        if (e.ClickCount == 2 && index.GetKind(hit) == NodeKind.Directory)
        {
            RootId = hit;
        }
        else
        {
            SelectedId = hit;
        }
    }

    protected override void OnMouseRightButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseRightButtonDown(e);

        NodeIndex? index = Index;
        if (index is null)
        {
            return;
        }

        int root = EffectiveRoot(index, RootId);
        if (root != 0)
        {
            RootId = index.GetParent(root);
        }
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);

        // MouseMove fires on every pixel; rebuild the tooltip text only when the hovered node changes.
        Point p = e.GetPosition(this);
        int hit = TreemapLayout.HitTest(_items, p.X, p.Y);
        if (hit == _tooltipId)
        {
            return;
        }

        _tooltipId = hit;

        NodeIndex? index = Index;
        if (hit < 0 || index is null)
        {
            ToolTip = null;
            return;
        }

        NodeStatus status = index.GetStatus(hit);
        string incomplete = status != NodeStatus.None ? $"\n(incomplete: {status})" : string.Empty;
        ToolTip = $"{index.GetFullPath(hit)}\n{ByteFormatter.FormatNodeSize(index.GetTotalSize(hit, SizeKind), status)}{incomplete}";
    }

    private void Relayout()
    {
        NodeIndex? index = Index;
        double width = ActualWidth;
        double height = ActualHeight;
        if (index is null || width <= 0 || height <= 0)
        {
            _items = [];
            return;
        }

        int root = EffectiveRoot(index, RootId);
        _items = TreemapLayout.Build(index, root, SizeKind, new LayoutRect(0, 0, width, height));
    }

    // An out-of-range or non-directory RootId falls back to the scan root, so Build never receives a bad id.
    private static int EffectiveRoot(NodeIndex index, int rootId) =>
        rootId > 0 && rootId < index.Count && index.GetKind(rootId) == NodeKind.Directory ? rootId : 0;

    // FNV-1a over the lowercased extension. A stable hash keeps each extension on the same color across rebuilds.
    private static int ExtensionPaletteIndex(ReadOnlySpan<char> extension)
    {
        uint hash = 2166136261;
        foreach (char c in extension)
        {
            hash = unchecked((hash ^ char.ToLowerInvariant(c)) * 16777619);
        }

        return (int)(hash % (uint)FilePalette.Length);
    }

    private static Rect ToRect(LayoutRect rect) => new(rect.X, rect.Y, rect.Width, rect.Height);

    private static void OnLayoutInputChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var control = (TreemapControl)d;
        control.Relayout();
        control.InvalidateVisual();
    }

    private static void OnSelectionChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        ((TreemapControl)d).InvalidateVisual();
    }

    private static SolidColorBrush FrozenBrush(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }

    private static SolidColorBrush FrozenRgb(int rgb) =>
        FrozenBrush(Color.FromRgb((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb));

    private static Pen FrozenPen(Brush brush, double thickness)
    {
        var pen = new Pen(brush, thickness);
        pen.Freeze();
        return pen;
    }
}
