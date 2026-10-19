namespace DiskScope.Scanner;

public readonly record struct ScanProgress(
    long Nodes,
    long Files,
    long Directories,
    long LogicalBytes,
    int Errors,
    string? CurrentDirectory,
    TimeSpan Elapsed);
