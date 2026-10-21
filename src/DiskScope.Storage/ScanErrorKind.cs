namespace DiskScope.Storage;

public enum ScanErrorKind : byte
{
    AccessDenied,
    NotFound,
    IoError,
    Unexpected,
}
