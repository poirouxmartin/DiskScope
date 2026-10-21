namespace DiskScope.Core;

/// <summary>Spec section 13. A measured alert and a diagnostic hypothesis are never the same level.</summary>
public enum FindingLevel
{
    /// <summary>Observed state, no problem established.</summary>
    Information,

    /// <summary>Unusual trend or sustained threshold, worth watching.</summary>
    Watch,

    /// <summary>Measured, persistent condition beyond a configured threshold.</summary>
    Alert,

    /// <summary>Possible explanation to confirm; a correlation, never a proven cause.</summary>
    Hypothesis,
}

public enum ThresholdComparison
{
    Above,
    Below,
}

/// <summary>
/// "Metric beyond a threshold for at least <see cref="MinDuration"/>". One episode raises one event: it ends after the
/// condition has been false for <see cref="Recovery"/>, and a new episode cannot start before <see cref="Cooldown"/>
/// has passed since the previous one ended (no repeated alerts for one episode).
/// </summary>
public sealed record ThresholdRule(
    string Id,
    string Name,
    string MetricId,
    ThresholdComparison Comparison,
    double Threshold,
    MetricUnit Unit,
    TimeSpan MinDuration,
    TimeSpan Recovery,
    TimeSpan Cooldown,
    FindingLevel Level,
    string Rationale,
    string KnownFalsePositives,
    bool Enabled = true)
{
    public bool IsMet(double value) => Comparison == ThresholdComparison.Above ? value > Threshold : value < Threshold;
}

/// <summary>A value that took part in a finding, kept so the user sees what triggered it.</summary>
public sealed record Evidence(string MetricId, string Label, double Value, MetricUnit Unit, DateTime TimestampUtc);

/// <summary>One alert episode. <see cref="EndedUtc"/> is null while the condition still holds.</summary>
public sealed record AlertEvent(
    Guid Id,
    string RuleId,
    string Title,
    FindingLevel Level,
    DateTime StartedUtc,
    DateTime? EndedUtc,
    string Explanation,
    IReadOnlyList<Evidence> Evidence,
    bool Acknowledged);

/// <summary>Evaluates threshold rules against published batches and keeps the episodes.</summary>
public interface IAlertEngine
{
    IReadOnlyList<ThresholdRule> Rules { get; }

    /// <summary>Replaces a rule by id (to change a threshold or disable it). Its running episode, if any, is closed.</summary>
    void UpdateRule(ThresholdRule rule);

    /// <summary>Returns the events created or changed by this batch.</summary>
    IReadOnlyList<AlertEvent> Evaluate(MetricBatch batch);

    IReadOnlyList<AlertEvent> Events { get; }

    bool Acknowledge(Guid eventId);
}
