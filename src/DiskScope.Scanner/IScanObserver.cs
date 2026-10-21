using DiskScope.Core;

namespace DiskScope.Scanner;

/// <summary>
/// Receives scan progress and provisional results.
/// Called from the scan thread. Implementations must be cheap and thread-safe; they marshal to the UI thread themselves.
/// </summary>
public interface IScanObserver
{
    /// <summary>At most once per <see cref="ScanOptions.ProgressInterval"/>, plus once at the end.</summary>
    void OnProgress(in ScanProgress progress);

    /// <summary>
    /// Provisional index published while the scan runs (see <see cref="ScanOptions.PartialResultInterval"/>).
    /// Unvisited directories carry NotScanned, so the root reads SubtreeIncomplete until the final result.
    /// The index is an immutable copy: it can be read from any thread.
    /// </summary>
    void OnPartialResult(NodeIndex partial)
    {
    }
}
