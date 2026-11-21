using System.Globalization;
using DiskScope.App.Localization;
using DiskScope.Core;

namespace DiskScope.App.ViewModels;

public sealed class AlertRowViewModel(AlertEvent alert, Action<Guid> acknowledge)
{
    public AlertEvent Alert { get; } = alert;

    public string Started => Alert.StartedUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);

    public string Ended => Alert.EndedUtc is { } e ? e.ToLocalTime().ToString("HH:mm:ss", CultureInfo.InvariantCulture) : Loc.Get("Alerts_Ongoing");

    public string Level => Loc.Get($"Level_{Alert.Level}");

    public string Title => Alert.Title;

    public string Explanation => Alert.Explanation;

    public string Evidence => string.Join(
        "; ",
        Alert.Evidence.Select(e => Loc.Format("Alerts_EvidenceAt", e.Label, MetricFormatter.Format(e.Value, e.Unit), e.TimestampUtc.ToLocalTime().ToString("HH:mm:ss"))));

    public bool Acknowledged => Alert.Acknowledged;

    public RelayCommand AcknowledgeCommand { get; } = new(() => acknowledge(alert.Id), () => !alert.Acknowledged);
}

/// <summary>One diagnostic hypothesis, with its confidence, evidence, limits and the checks to run.</summary>
public sealed class FindingRowViewModel(Finding finding)
{
    public Finding Finding { get; } = finding;

    public string Period => $"{Finding.FromUtc.ToLocalTime():HH:mm:ss} - {Finding.ToUtc.ToLocalTime():HH:mm:ss}";

    public string Level => Loc.Format("Alerts_FindingLevel", Loc.Get($"Level_{Finding.Level}"), Loc.Get($"Confidence_{Finding.Confidence}"));

    public string Title => Finding.Title;

    public string Explanation => Finding.Explanation;

    public string Evidence => string.Join("; ", Finding.Evidence.Select(e => $"{e.Label}: {MetricFormatter.Format(e.Value, e.Unit)}"));

    public string Checks => string.Join(" ", Finding.Checks.Select((c, i) => $"{i + 1}. {c}"));

    public string Limits => Finding.Limits;

    public static FindingRowViewModel ForError(string error, DateTime now) => new(new Finding(
        "error", FindingLevel.Information, Confidence.Low, Loc.Get("Alerts_RuleFailed"), error, [], [], Loc.Get("Alerts_RuleNoResult"), now, now));
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

    // Built-in rules have translated texts keyed by rule id; other rules show their own text.
    public string Name => Loc.TryGet($"Rule.{_rule.Id}.Name") ?? _rule.Name;

    public string Condition =>
        Loc.Format("Alerts_Condition", _rule.MetricId, _rule.Comparison == ThresholdComparison.Above ? ">" : "<",
            _rule.MinDuration.TotalSeconds.ToString("0", CultureInfo.InvariantCulture), _rule.Recovery.TotalSeconds.ToString("0", CultureInfo.InvariantCulture),
            _rule.Cooldown.TotalSeconds.ToString("0", CultureInfo.InvariantCulture));

    public string ThresholdUnit => _rule.Unit switch
    {
        MetricUnit.Bytes => Loc.Get("Alerts_UnitMb"),
        MetricUnit.Percent => "%",
        MetricUnit.Milliseconds => "ms",
        _ => _rule.Unit.ToString(),
    };

    public string Rationale => Loc.TryGet($"Rule.{_rule.Id}.Rationale") ?? _rule.Rationale;

    public string KnownFalsePositives => Loc.TryGet($"Rule.{_rule.Id}.FalsePositives") ?? _rule.KnownFalsePositives;

    public bool Enabled { get => _enabled; set => SetProperty(ref _enabled, value); }

    public string ThresholdText { get => _thresholdText; set => SetProperty(ref _thresholdText, value); }

    public string Error { get => _error; private set => SetProperty(ref _error, value); }

    public RelayCommand ApplyCommand { get; }

    internal void Apply()
    {
        if (!double.TryParse(ThresholdText, NumberStyles.Float, CultureInfo.InvariantCulture, out double display) || !double.IsFinite(display) || display < 0)
        {
            Error = Loc.Get("Alerts_ThresholdInvalid");
            return;
        }

        if (_rule.Unit == MetricUnit.Percent && display > 100)
        {
            Error = Loc.Get("Alerts_ThresholdPercent");
            return;
        }

        _rule = _rule with { Threshold = FromDisplay(display), Enabled = Enabled };
        _engine.UpdateRule(_rule);
        Error = string.Empty;
    }

    private double ToDisplay(double v) => _rule.Unit == MetricUnit.Bytes ? v / BytesPerMb : v;

    private double FromDisplay(double v) => _rule.Unit == MetricUnit.Bytes ? v * BytesPerMb : v;
}
