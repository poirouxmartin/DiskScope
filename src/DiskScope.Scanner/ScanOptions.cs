namespace DiskScope.Scanner;

public sealed record ScanOptions
{
    public TimeSpan ProgressInterval { get; init; } = TimeSpan.FromMilliseconds(100);
}
