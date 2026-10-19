using DiskScope.Core;

namespace DiskScope.Scanner;

/// <summary>
/// Result of an engine run.
/// On Cancelled the index is partial; unvisited directories carry NodeStatus.NotScanned.
/// </summary>
public sealed record ScanResult(
    NodeIndex Index,
    ScanOutcome Outcome,
    TimeSpan Elapsed,
    string EngineName,
    string? FailureMessage = null);
