namespace DiskScope.Core;

/// <summary>How much a finding can be trusted, stated with it.</summary>
public enum Confidence
{
    /// <summary>Temporal coincidence only.</summary>
    Low,

    /// <summary>Coincidence plus a known mechanism linking the two (for example paging when memory is short).</summary>
    Medium,

    /// <summary>Direct measurement of the cause.</summary>
    High,
}

/// <summary>
/// An explainable diagnostic result: what was observed, the data, how sure it is, what it does not prove, and what to
/// check next. Never an action: corrective steps stay separate and need confirmation (spec section 13).
/// </summary>
public sealed record Finding(
    string RuleId,
    FindingLevel Level,
    Confidence Confidence,
    string Title,
    string Explanation,
    IReadOnlyList<Evidence> Evidence,
    IReadOnlyList<string> Checks,
    string Limits,
    DateTime FromUtc,
    DateTime ToUtc);

/// <summary>What a diagnostic rule may look at.</summary>
public sealed record DiagnosticContext(IReadOnlyList<AlertEvent> Alerts, IMetricStore History, DateTime NowUtc);

/// <summary>
/// Deterministic rule: same context, same findings. It documents its formula, window, thresholds and known false
/// positives, and returns nothing rather than a weak guess.
/// </summary>
public interface IDiagnosticRule
{
    string Id { get; }

    string Formula { get; }

    string KnownFalsePositives { get; }

    IReadOnlyList<Finding> Evaluate(DiagnosticContext context);
}
