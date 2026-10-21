using System.Globalization;
using DiskScope.Core;

namespace DiskScope.App.ViewModels;

public sealed class AlertRowViewModel(AlertEvent alert, Action<Guid> acknowledge)
{
    public AlertEvent Alert { get; } = alert;

    public string Started => Alert.StartedUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);

    public string Ended => Alert.EndedUtc is { } e ? e.ToLocalTime().ToString("HH:mm:ss", CultureInfo.InvariantCulture) : "ongoing";

    public string Level => Alert.Level.ToString();

    public string Title => Alert.Title;

    public string Explanation => Alert.Explanation;

    public string Evidence => string.Join(
        "; ",
        Alert.Evidence.Select(e => $"{e.Label}: {MetricFormatter.Format(e.Value, e.Unit)} at {e.TimestampUtc.ToLocalTime():HH:mm:ss}"));

    public bool Acknowledged => Alert.Acknowledged;

    public RelayCommand AcknowledgeCommand { get; } = new(() => acknowledge(alert.Id), () => !alert.Acknowledged);
}

/// <summary>One diagnostic hypothesis, with its confidence, evidence, limits and the checks to run.</summary>
public sealed class FindingRowViewModel(Finding finding)
{
    public Finding Finding { get; } = finding;

    public string Period => $"{Finding.FromUtc.ToLocalTime():HH:mm:ss} - {Finding.ToUtc.ToLocalTime():HH:mm:ss}";

    public string Level => $"{Finding.Level} ({Finding.Confidence} confidence)";

    public string Title => Finding.Title;

    public string Explanation => Finding.Explanation;

    public string Evidence => string.Join("; ", Finding.Evidence.Select(e => $"{e.Label}: {MetricFormatter.Format(e.Value, e.Unit)}"));

    public string Checks => string.Join(" ", Finding.Checks.Select((c, i) => $"{i + 1}. {c}"));

    public string Limits => Finding.Limits;

    public static FindingRowViewModel ForError(string error, DateTime now) => new(new Finding(
        "error", FindingLevel.Information, Confidence.Low, "Diagnostic rule failed", error, [], [], "This rule produced no result.", now, now));
}

/// <summary>
/// Editable view of one threshold rule. Byte thresholds are edited in MB, the others in their own unit.
/// Changes apply only through <see cref="ApplyCommand"/>, so a half-typed number never reaches the engine.
/// </summary>
public sealed class RuleViewModel : ObservableObject
{
    private const double BytesPerMb = 1024 * 1024;
    private readonly IAlertEngine _engine;
    private ThresholdRule _rule;
    private bool _enabled;
    private string _thresholdText;
    private string _error = string.Empty;

    public RuleViewModel(ThresholdRule rule, IAlertEngine engine)
    {
        _rule = rule;
        _engine = engine;
        _enabled = rule.Enabled;
        _thresholdText = ToDisplay(rule.Threshold).ToString("0.##", CultureInfo.InvariantCulture);
        ApplyCommand = new RelayCommand(Apply);
    }

    public string Name => _rule.Name;

    public string Condition =>
        $"{_rule.MetricId} {(_rule.Comparison == ThresholdComparison.Above ? ">" : "<")} threshold for {_rule.MinDuration.TotalSeconds:0} s " +
        $"(recovery {_rule.Recovery.TotalSeconds:0} s, cooldown {_rule.Cooldown.TotalSeconds:0} s)";

    public string ThresholdUnit => _rule.Unit == MetricUnit.Bytes ? "MB" : _rule.Unit == MetricUnit.Percent ? "%" : _rule.Unit.ToString();

    public string Rationale => _rule.Rationale;

    public string KnownFalsePositives => _rule.KnownFalsePositives;

    public bool Enabled { get => _enabled; set => SetProperty(ref _enabled, value); }

    public string ThresholdText { get => _thresholdText; set => SetProperty(ref _thresholdText, value); }

    public string Error { get => _error; private set => SetProperty(ref _error, value); }

    public RelayCommand ApplyCommand { get; }

    internal void Apply()
    {
        if (!double.TryParse(ThresholdText, NumberStyles.Float, CultureInfo.InvariantCulture, out double display) || !double.IsFinite(display) || display < 0)
        {
            Error = "Threshold: enter a non-negative number (use '.' as decimal separator)";
            return;
        }

        if (_rule.Unit == MetricUnit.Percent && display > 100)
        {
            Error = "Threshold: a percentage cannot exceed 100";
            return;
        }

        _rule = _rule with { Threshold = FromDisplay(display), Enabled = Enabled };
        _engine.UpdateRule(_rule);
        Error = string.Empty;
    }

    private double ToDisplay(double v) => _rule.Unit == MetricUnit.Bytes ? v / BytesPerMb : v;

    private double FromDisplay(double v) => _rule.Unit == MetricUnit.Bytes ? v * BytesPerMb : v;
}
