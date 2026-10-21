using System.Globalization;
using DiskScope.Core;

namespace DiskScope.Diagnostics;

/// <summary>
/// Threshold rules evaluated on each batch, one state machine per rule:
/// idle -> condition met -> (met for MinDuration) event raised -> (not met for Recovery) event ended.
/// If the condition comes back within Cooldown of the end, the same event is reopened instead of a new one being
/// raised, so a flapping metric produces one episode, not a stream of alerts.
/// Unavailable values change nothing: a gap neither starts nor ends an episode.
/// Thread-safe: batches of different collectors may be evaluated concurrently.
/// </summary>
public sealed class AlertEngine : IAlertEngine
{
    public const int MaxEvents = 1000;

    private readonly Lock _gate = new();
    private readonly List<ThresholdRule> _rules;
    private readonly Dictionary<string, RuleState> _states = new(StringComparer.Ordinal);
    private readonly List<AlertEvent> _events = new();

    public AlertEngine(IEnumerable<ThresholdRule> rules)
    {
        ArgumentNullException.ThrowIfNull(rules);
        _rules = rules.ToList();
        if (_rules.Select(r => r.Id).Distinct(StringComparer.Ordinal).Count() != _rules.Count)
        {
            throw new ArgumentException("Rule ids must be unique.", nameof(rules));
        }

        foreach (ThresholdRule rule in _rules)
        {
            _states[rule.Id] = new RuleState();
        }
    }

    public IReadOnlyList<ThresholdRule> Rules
    {
        get
        {
            lock (_gate)
            {
                return _rules.ToArray();
            }
        }
    }

    public IReadOnlyList<AlertEvent> Events
    {
        get
        {
            lock (_gate)
            {
                return _events.ToArray();
            }
        }
    }

    public void UpdateRule(ThresholdRule rule)
    {
        ArgumentNullException.ThrowIfNull(rule);
        lock (_gate)
        {
            int index = _rules.FindIndex(r => r.Id == rule.Id);
            if (index < 0)
            {
                throw new ArgumentException($"Unknown rule '{rule.Id}'.", nameof(rule));
            }

            _rules[index] = rule;
            RuleState state = _states[rule.Id];
            if (state.ActiveIndex is int active && _events[active].EndedUtc is null)
            {
                // The episode was judged against the old threshold; close it at its last observation rather than reinterpret it.
                _events[active] = _events[active] with { EndedUtc = state.LastSeenUtc };
            }

            _states[rule.Id] = new RuleState();
        }
    }

    public IReadOnlyList<AlertEvent> Evaluate(MetricBatch batch)
    {
        ArgumentNullException.ThrowIfNull(batch);
        var changed = new List<AlertEvent>();
        lock (_gate)
        {
            foreach (MetricValue value in batch.Values)
            {
                if (value.Value is not { } v)
                {
                    continue;
                }

                foreach (ThresholdRule rule in _rules)
                {
                    if (rule.Enabled && rule.MetricId == value.Id && Step(rule, _states[rule.Id], v, batch.TimestampUtc) is { } e)
                    {
                        changed.Add(e);
                    }
                }
            }
        }

        return changed;
    }

    public bool Acknowledge(Guid eventId)
    {
        lock (_gate)
        {
            int i = _events.FindIndex(e => e.Id == eventId);
            if (i < 0)
            {
                return false;
            }

            _events[i] = _events[i] with { Acknowledged = true };
            return true;
        }
    }

    private AlertEvent? Step(ThresholdRule rule, RuleState s, double value, DateTime t)
    {
        s.LastSeenUtc = t;
        bool met = rule.IsMet(value);
        bool open = s.ActiveIndex is int i && _events[i].EndedUtc is null;

        if (open)
        {
            int index = s.ActiveIndex!.Value;
            if (met)
            {
                s.FalseSinceUtc = null;
                if (IsWorse(rule, value, s.Peak))
                {
                    s.Peak = value;
                    _events[index] = WithEvidence(rule, _events[index], s, value, t);
                }

                return null;
            }

            s.FalseSinceUtc ??= t;
            if (t - s.FalseSinceUtc.Value < rule.Recovery)
            {
                return null;
            }

            _events[index] = _events[index] with { EndedUtc = s.FalseSinceUtc };
            s.LastEndedUtc = s.FalseSinceUtc;
            s.FalseSinceUtc = null;
            s.MetSinceUtc = null;
            return _events[index];
        }

        if (!met)
        {
            s.MetSinceUtc = null;
            return null;
        }

        s.MetSinceUtc ??= t;
        if (t - s.MetSinceUtc.Value < rule.MinDuration)
        {
            return null;
        }

        if (s.ActiveIndex is int previous && s.LastEndedUtc is { } ended && s.MetSinceUtc.Value - ended < rule.Cooldown)
        {
            // Same episode: the condition came back before the cooldown ran out.
            _events[previous] = _events[previous] with { EndedUtc = null };
            s.Peak = IsWorse(rule, value, s.Peak) ? value : s.Peak;
            return _events[previous];
        }

        s.Peak = value;
        var created = new AlertEvent(
            Guid.NewGuid(),
            rule.Id,
            rule.Name,
            rule.Level,
            s.MetSinceUtc.Value,
            null,
            Explain(rule, value),
            [new Evidence(rule.MetricId, "First value over the duration", value, rule.Unit, t)],
            false);
        if (_events.Count >= MaxEvents)
        {
            _events.RemoveAt(0);
            foreach (RuleState other in _states.Values)
            {
                other.ActiveIndex = other.ActiveIndex is int a ? (a == 0 ? null : a - 1) : null;
            }
        }

        _events.Add(created);
        s.ActiveIndex = _events.Count - 1;
        return created;
    }

    private static bool IsWorse(ThresholdRule rule, double value, double peak) =>
        rule.Comparison == ThresholdComparison.Above ? value > peak : value < peak;

    private static AlertEvent WithEvidence(ThresholdRule rule, AlertEvent e, RuleState s, double peak, DateTime t) =>
        e with
        {
            Evidence = [e.Evidence[0], new Evidence(rule.MetricId, "Worst value so far", peak, rule.Unit, t)],
            Explanation = Explain(rule, peak),
        };

    private static string Explain(ThresholdRule rule, double worst)
    {
        string direction = rule.Comparison == ThresholdComparison.Above ? "above" : "below";
        string seconds = rule.MinDuration.TotalSeconds.ToString("0", CultureInfo.InvariantCulture);
        return $"{rule.MetricId} stayed {direction} {MetricFormatter.Format(rule.Threshold, rule.Unit)} for at least {seconds} s " +
               $"(worst {MetricFormatter.Format(worst, rule.Unit)}). Measured fact, not a diagnosis.";
    }

    private sealed class RuleState
    {
        public DateTime? MetSinceUtc;
        public DateTime? FalseSinceUtc;
        public DateTime? LastEndedUtc;
        public DateTime LastSeenUtc;
        public int? ActiveIndex;
        public double Peak;
    }
}
