using System.Windows;
using System.Windows.Media;
using DiskScope.Core;

namespace DiskScope.App.Controls;

/// <summary>
/// Time-series line drawn in OnRender (one element, no child visuals). The X axis is the fixed window
/// [End - Window, End]; a period without points is drawn as a gap, never bridged, so missing data stays visible.
/// The Y axis runs from 0 to <see cref="Maximum"/> (NaN = largest point).
/// </summary>
public sealed class SparklineControl : FrameworkElement
{
    public static readonly DependencyProperty PointsProperty = DependencyProperty.Register(
        nameof(Points), typeof(IReadOnlyList<MetricPoint>), typeof(SparklineControl),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty EndProperty = DependencyProperty.Register(
        nameof(End), typeof(DateTime), typeof(SparklineControl),
        new FrameworkPropertyMetadata(default(DateTime), FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty WindowProperty = DependencyProperty.Register(
        nameof(Window), typeof(TimeSpan), typeof(SparklineControl),
        new FrameworkPropertyMetadata(TimeSpan.FromMinutes(1), FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty MaximumProperty = DependencyProperty.Register(
        nameof(Maximum), typeof(double), typeof(SparklineControl),
        new FrameworkPropertyMetadata(double.NaN, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty StrokeProperty = DependencyProperty.Register(
        nameof(Stroke), typeof(Brush), typeof(SparklineControl),
        new FrameworkPropertyMetadata(Brushes.SteelBlue, FrameworkPropertyMetadataOptions.AffectsRender));

    private static readonly Brush Background = Freeze(new SolidColorBrush(Color.FromRgb(0xF4, 0xF6, 0xF8)));
    private static readonly Pen GridPen = Freeze(new Pen(new SolidColorBrush(Color.FromRgb(0xDD, 0xE1, 0xE5)), 1));

    public IReadOnlyList<MetricPoint>? Points
    {
        get => (IReadOnlyList<MetricPoint>?)GetValue(PointsProperty);
        set => SetValue(PointsProperty, value);
    }

    public DateTime End
    {
        get => (DateTime)GetValue(EndProperty);
        set => SetValue(EndProperty, value);
    }

    public TimeSpan Window
    {
        get => (TimeSpan)GetValue(WindowProperty);
        set => SetValue(WindowProperty, value);
    }

    public double Maximum
    {
        get => (double)GetValue(MaximumProperty);
        set => SetValue(MaximumProperty, value);
    }

    public Brush Stroke
    {
        get => (Brush)GetValue(StrokeProperty);
        set => SetValue(StrokeProperty, value);
    }

    /// <summary>
    /// Splits points into runs separated by gaps: a gap is a spacing larger than 2.5x the median spacing.
    /// Pure, so the gap rule is tested without WPF rendering.
    /// </summary>
    internal static List<List<MetricPoint>> SplitRuns(IReadOnlyList<MetricPoint> points)
    {
        var runs = new List<List<MetricPoint>>();
        if (points.Count == 0)
        {
            return runs;
        }

        var spacings = new List<long>(points.Count);
        for (int i = 1; i < points.Count; i++)
        {
            spacings.Add((points[i].TimestampUtc - points[i - 1].TimestampUtc).Ticks);
        }

        spacings.Sort();
        long median = spacings.Count > 0 ? spacings[spacings.Count / 2] : 0;
        var current = new List<MetricPoint> { points[0] };
        for (int i = 1; i < points.Count; i++)
        {
            long gap = (points[i].TimestampUtc - points[i - 1].TimestampUtc).Ticks;
            if (median > 0 && gap > median * 5 / 2)
            {
                runs.Add(current);
                current = new List<MetricPoint>();
            }

            current.Add(points[i]);
        }

        runs.Add(current);
        return runs;
    }

    protected override void OnRender(DrawingContext dc)
    {
        double w = ActualWidth, h = ActualHeight;
        if (w < 2 || h < 2)
        {
            return;
        }

        dc.DrawRectangle(Background, null, new Rect(0, 0, w, h));
        dc.DrawLine(GridPen, new Point(0, h / 2), new Point(w, h / 2));

        IReadOnlyList<MetricPoint>? points = Points;
        if (points is null || points.Count == 0 || Window <= TimeSpan.Zero)
        {
            return;
        }

        double max = double.IsNaN(Maximum) ? points.Max(p => p.Average) : Maximum;
        if (max <= 0)
        {
            max = 1;
        }

        DateTime start = End - Window;
        double X(DateTime t) => (t - start).Ticks / (double)Window.Ticks * w;
        double Y(double v) => h - 1 - (Math.Clamp(v / max, 0, 1) * (h - 2));

        var pen = new Pen(Stroke, 1.5);
        pen.Freeze();
        foreach (List<MetricPoint> run in SplitRuns(points))
        {
            if (run.Count == 1)
            {
                dc.DrawEllipse(Stroke, null, new Point(X(run[0].TimestampUtc), Y(run[0].Average)), 1.5, 1.5);
                continue;
            }

            var geometry = new StreamGeometry();
            using (StreamGeometryContext g = geometry.Open())
            {
                g.BeginFigure(new Point(X(run[0].TimestampUtc), Y(run[0].Average)), false, false);
                for (int i = 1; i < run.Count; i++)
                {
                    g.LineTo(new Point(X(run[i].TimestampUtc), Y(run[i].Average)), true, false);
                }
            }

            geometry.Freeze();
            dc.DrawGeometry(null, pen, geometry);
        }
    }

    private static T Freeze<T>(T freezable)
        where T : Freezable
    {
        freezable.Freeze();
        return freezable;
    }
}
