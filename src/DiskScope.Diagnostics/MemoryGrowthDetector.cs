namespace DiskScope.Diagnostics;

/// <summary>A process whose private memory kept growing over the observation window. A suspicion, not a proven leak.</summary>
public sealed record GrowthSuspicion<TKey>(TKey Key, DateTime FromUtc, DateTime ToUtc, long FromBytes, long ToBytes)
{
    public long GrowthBytes => ToBytes - FromBytes;

    public double GrowthBytesPerMinute => GrowthBytes / Math.Max(1e-9, (ToUtc - FromUtc).TotalMinutes);
}

/// <summary>
/// Flags sustained growth of a per-process memory value (private bytes / commit charge).
/// Formula, over the samples of the last <see cref="Window"/>: the observed span covers at least
/// <see cref="MinSpan"/>, the value grew by at least max(<see cref="MinGrowthBytes"/>, <see cref="MinGrowthRatio"/> x start),
/// at least <see cref="MinRisingShare"/> of consecutive steps did not decrease, and each half of the span carries at
/// least <see cref="MinHalfShare"/> of the growth.
/// Known false positives: caches that fill up then plateau, loading a large document, a browser opening tabs.
/// Hence the "Watch" level and the "to verify" wording: growth is a hypothesis, only a plateau-free trend over a longer
/// run, or the process's own diagnostics, can confirm a leak.
/// Not thread-safe: fed from the single process refresh loop.
/// </summary>
public sealed class MemoryGrowthDetector<TKey>
    where TKey : notnull
{
    private readonly Dictionary<TKey, Queue<(DateTime T, long Bytes)>> _samples = new();

    public TimeSpan Window { get; init; } = TimeSpan.FromMinutes(10);

    public TimeSpan MinSpan { get; init; } = TimeSpan.FromMinutes(5);

    public long MinGrowthBytes { get; init; } = 100L * 1024 * 1024;

    public double MinGrowthRatio { get; init; } = 0.2;

    public double MinRisingShare { get; init; } = 0.8;

    public double MinHalfShare { get; init; } = 0.25;

    public int TrackedCount => _samples.Count;

    public void Observe(TKey key, DateTime timestampUtc, long bytes)
    {
        if (!_samples.TryGetValue(key, out Queue<(DateTime, long)>? q))
        {
            _samples[key] = q = new Queue<(DateTime, long)>();
        }

        q.Enqueue((timestampUtc, bytes));
        while (q.Count > 0 && timestampUtc - q.Peek().Item1 > Window)
        {
            q.Dequeue();
        }
    }

    /// <summary>Drops keys not observed since <paramref name="olderThanUtc"/> (exited processes).</summary>
    public void Forget(DateTime olderThanUtc)
    {
        foreach (TKey key in _samples.Where(kv => kv.Value.Count == 0 || kv.Value.Last().T < olderThanUtc).Select(kv => kv.Key).ToList())
        {
            _samples.Remove(key);
        }
    }

    public IReadOnlyList<GrowthSuspicion<TKey>> Evaluate()
    {
        var result = new List<GrowthSuspicion<TKey>>();
        foreach (var (key, q) in _samples)
        {
            if (q.Count < 3)
            {
                continue;
            }

            (DateTime t0, long b0) = q.Peek();
            (DateTime t1, long b1) = q.Last();
            if (t1 - t0 < MinSpan || b1 - b0 < Math.Max(MinGrowthBytes, (long)(MinGrowthRatio * b0)))
            {
                continue;
            }

            int rising = 0, steps = 0;
            long previous = b0;
            foreach ((DateTime _, long b) in q.Skip(1))
            {
                steps++;
                rising += b >= previous ? 1 : 0;
                previous = b;
            }

            // A jump followed by a plateau (a cache filling, a file loaded) is not sustained growth: each half of the
            // span must carry at least MinHalfShare of the total growth.
            DateTime middle = t0 + ((t1 - t0) / 2);
            long bMiddle = q.Last(s => s.T <= middle).Bytes;
            double total = b1 - b0;
            bool spread = (bMiddle - b0) / total >= MinHalfShare && (b1 - bMiddle) / total >= MinHalfShare;

            double share = (double)rising / steps;
            if (share >= MinRisingShare && spread)
            {
                result.Add(new GrowthSuspicion<TKey>(key, t0, t1, b0, b1));
            }
        }

        return result;
    }
}
