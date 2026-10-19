namespace DiskScope.Core;

public readonly record struct ScanError(int NodeId, ScanErrorKind Kind, int Win32Error, string Message);
