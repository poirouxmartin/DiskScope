namespace DiskScope.Scanner;

public sealed record ScanRequest(string RootPath, ScanOptions Options)
{
    public ScanRequest(string rootPath) : this(rootPath, new ScanOptions())
    {
    }
}
