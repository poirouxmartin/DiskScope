using System.Windows;
using System.Windows.Media;

namespace DiskScope.App.Controls;

/// <summary>
/// Horizontal gauge drawn as equal segments, like a level meter. Percent is 0-100;
/// a null percent draws nothing, so an unknown value never looks like an empty gauge at 0.
/// </summary>
public sealed class SegmentGaugeControl : FrameworkElement
{
    public static readonly DependencyProperty PercentProperty = DependencyProperty.Register(
        nameof(Percent), typeof(double?), typeof(SegmentGaugeControl),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty SegmentsProperty = DependencyProperty.Register(
        nameof(Segments), typeof(int), typeof(SegmentGaugeControl),
        new FrameworkPropertyMetadata(20, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty FillProperty = DependencyProperty.Register(
        nameof(Fill), typeof(Brush), typeof(SegmentGaugeControl),
        new FrameworkPropertyMetadata(Brushes.SteelBlue, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty TrackProperty = DependencyProperty.Register(
        nameof(Track), typeof(Brush), typeof(SegmentGaugeControl),
        new FrameworkPropertyMetadata(Brushes.DimGray, FrameworkPropertyMetadataOptions.AffectsRender));

    public double? Percent { get => (double?)GetValue(PercentProperty); set => SetValue(PercentProperty, value); }

    public int Segments { get => (int)GetValue(SegmentsProperty); set => SetValue(SegmentsProperty, value); }

    public Brush Fill { get => (Brush)GetValue(FillProperty); set => SetValue(FillProperty, value); }

    public Brush Track { get => (Brush)GetValue(TrackProperty); set => SetValue(TrackProperty, value); }

    /// <summary>Number of lit segments for a percent: a non-zero value always lights at least one.</summary>
    public static int LitSegments(double? percent, int segments)
    {
        if (percent is not double p || !double.IsFinite(p) || segments <= 0)
        {
            return 0;
        }

        p = Math.Clamp(p, 0, 100);
        int lit = (int)Math.Round(p / 100 * segments, MidpointRounding.AwayFromZero);
        return p > 0 ? Math.Max(1, lit) : 0;
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        int segments = Segments;
        if (Percent is null || segments <= 0 || ActualWidth <= 0 || ActualHeight <= 0)
        {
            return;
        }

        const double gap = 2;
        double width = (ActualWidth - gap * (segments - 1)) / segments;
        if (width <= 0)
        {
            return;
        }

        int lit = LitSegments(Percent, segments);
        for (int i = 0; i < segments; i++)
        {
            var rect = new Rect(i * (width + gap), 0, width, ActualHeight);
            drawingContext.DrawRectangle(i < lit ? Fill : Track, null, rect);
        }
    }
}
