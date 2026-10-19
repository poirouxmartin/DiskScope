namespace DiskScope.Scanner;

/// <summary>
/// Contract for a scan engine.
/// Cancellation does not throw: it returns a ScanResult with Outcome Cancelled.
/// Only argument errors throw.
/// </summary>
public interface IScanEngine
{
    string Name { get; }

    EngineAvailability CheckAvailability(ScanRequest request);

    Task<ScanResult> ScanAsync(ScanRequest request, IScanObserver? observer, CancellationToken cancellationToken);
}
