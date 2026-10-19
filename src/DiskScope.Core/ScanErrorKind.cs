namespace DiskScope.Core;

public enum ScanErrorKind : byte
{
    AccessDenied,
    NotFound,
    IoError,
    Unexpected,
}
