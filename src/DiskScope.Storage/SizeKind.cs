namespace DiskScope.Storage;

/// <summary>
/// Which size column a query reads: logical (bytes in the file) or allocated (bytes on disk).
/// </summary>
public enum SizeKind
{
    Logical,
    Allocated,
}
