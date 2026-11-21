using DiskScope.Core;

namespace DiskScope.App.ViewModels;

/// <summary>One metric shown as current value + short trend + recent maximum. Unknown values read "n/a".</summary>
public sealed class MetricTileViewModel : ObservableObject
{
    private string _valueText = MetricFormatter.Unknown;
    private string _peakText = string.Empty;
    private bool _isAvailable;
    private IReadOnlyList<MetricPoint> _points = Array.Empty<MetricPoint>();
    private DateTime _end;
    private double _maximum = double.NaN;
    private string _detail = string.Empty;
    private double? _gaugePercent;

    public MetricTileViewModel(MetricDescriptor descriptor, TimeSpan window, int? navigateTo = null)
    {
        Descriptor = descriptor;
        Window = window;
        NavigateTo = navigateTo;
        ToolTip = descriptor.Definition is null
            ? $"Source: {descriptor.Source}"
            : $"{descriptor.Definition}\nSource: {descriptor.Source}";
    }

    public MetricDescriptor Descriptor { get; }

    public string Id => Descriptor.Id;

    public string Title => Descriptor.DisplayName;

    public string ToolTip { get; }

    public TimeSpan Window { get; }

    /// <summary>Tab index opened when the dashboard card is clicked; null when the card has no detail view.</summary>
    public int? NavigateTo { get; }

    public string ValueText { get => _valueText; private set => SetProperty(ref _valueText, value); }

    public string PeakText { get => _peakText; private set => SetProperty(ref _peakText, value); }

    /// <summary>Secondary line, for example "of 31.7 GB".</summary>
    public string Detail { get => _detail; set => SetProperty(ref _detail, value); }

    public bool IsAvailable { get => _isAvailable; private set => SetProperty(ref _isAvailable, value); }

    public IReadOnlyList<MetricPoint> Points { get => _points; private set => SetProperty(ref _points, value); }

    public DateTime End { get => _end; private set => SetProperty(ref _end, value); }

    public double Maximum { get => _maximum; private set => SetProperty(ref _maximum, value); }

    /// <summary>Gauge fill for percent metrics, 0-100; null when the metric is not a percent or the value is unknown.</summary>
    public double? GaugePercent { get => _gaugePercent; private set => SetProperty(ref _gaugePercent, value); }

    /// <summary>Gauge state: 0 normal, 1 warning (80 % and above), 2 critical (90 % and above).</summary>
    public int GaugeLevel => GaugePercent switch
    {
        >= DriveCardViewModel.CriticalPercent => 2,
        >= DriveCardViewModel.WarningPercent => 1,
        _ => 0,
    };

    public void Update(MetricValue? latest, IReadOnlyList<MetricPoint> points, DateTime end, double maximum)
    {
        IsAvailable = latest is { IsAvailable: true };
        GaugePercent = Descriptor.Unit == MetricUnit.Percent && latest is { IsAvailable: true, Value: double v } && double.IsFinite(v)
            ? Math.Clamp(v, 0, 100)
            : null;
        RaisePropertyChanged(nameof(GaugeLevel));
        ValueText = MetricFormatter.Format(latest?.Value, Descriptor.Unit);
        Points = points;
        End = end;
        Maximum = maximum;
        PeakText = points.Count > 0
            ? $"max {MetricFormatter.Format(points.Max(p => p.Max), Descriptor.Unit)} over {FormatWindow(Window)}"
            : "no data yet";
    }

    private static string FormatWindow(TimeSpan w) =>
        w.TotalMinutes >= 1 ? $"{w.TotalMinutes:0} min" : $"{w.TotalSeconds:0} s";
}

/// <summary>Usage bar of one logical processor.</summary>
public sealed class CoreViewModel(int index) : ObservableObject
{
    private double _percent;
    private bool _hasValue;
    private string _text = MetricFormatter.Unknown;

    public int Index { get; } = index;

    public string Label => $"CPU {Index}";

    public double Percent { get => _percent; private set => SetProperty(ref _percent, value); }

    /// <summary>False when the value is unknown: the bar is hidden, the text says n/a.</summary>
    public bool HasValue { get => _hasValue; private set => SetProperty(ref _hasValue, value); }

    public string Text { get => _text; private set => SetProperty(ref _text, value); }

    public void Update(MetricValue? value)
    {
        HasValue = value?.Value is not null;
        Percent = value?.Value ?? 0; // the bar needs a length; see HasValue
        Text = MetricFormatter.Format(value?.Value, MetricUnit.Percent);
    }
}

/// <summary>Label, value and source of one metric, for detail tables.</summary>
public sealed class MetricRowViewModel(MetricDescriptor descriptor) : ObservableObject
{
    private string _value = MetricFormatter.Unknown;

    public MetricDescriptor Descriptor { get; } = descriptor;

    public string Name => Descriptor.DisplayName;

    public string Source => Descriptor.Source;

    public string Definition => Descriptor.Definition ?? string.Empty;

    public string Value { get => _value; private set => SetProperty(ref _value, value); }

    public void Update(MetricValue? latest) => Value = MetricFormatter.Format(latest?.Value, Descriptor.Unit);
}
