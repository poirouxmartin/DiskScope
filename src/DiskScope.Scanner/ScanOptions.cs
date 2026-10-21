namespace DiskScope.Scanner;

public sealed record ScanOptions
{
    public TimeSpan ProgressInterval { get; init; } = TimeSpan.FromMilliseconds(100);

    /// <summary>
    /// Delay before the first provisional index; null disables partial results.
    /// Each snapshot copies the whole builder (O(nodes)), so the delay doubles after every snapshot
    /// and is never shorter than 10x the last snapshot's cost: the copy overhead stays bounded on big volumes.
    /// </summary>
    public TimeSpan? PartialResultInterval { get; init; }

    /// <summary>Upper bound of the doubling delay between provisional indexes.</summary>
    public TimeSpan MaxPartialResultInterval { get; init; } = TimeSpan.FromSeconds(30);
}
