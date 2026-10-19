namespace DiskScope.Scanner;

/// <summary>
/// Runs the first engine, in preference order, that reports itself available for the request.
/// </summary>
public sealed class ScanOrchestrator(IReadOnlyList<IScanEngine> enginesByPreference)
{
    public IScanEngine SelectEngine(ScanRequest request)
    {
        var reasons = new List<string>();
        foreach (IScanEngine engine in enginesByPreference)
        {
            EngineAvailability availability = engine.CheckAvailability(request);
            if (availability.IsAvailable)
            {
                return engine;
            }

            reasons.Add($"{engine.Name}: {availability.Reason}");
        }

        throw new InvalidOperationException(
            $"No scan engine is available for '{request.RootPath}'. " + string.Join("; ", reasons));
    }

    public Task<ScanResult> ScanAsync(ScanRequest request, IScanObserver? observer, CancellationToken cancellationToken)
    {
        return SelectEngine(request).ScanAsync(request, observer, cancellationToken);
    }
}
