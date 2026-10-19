namespace DiskScope.Scanner;

/// <summary>
/// Receives scan progress.
/// Called from the scan thread, at most once per ProgressInterval plus once at the end.
/// Implementations must be cheap and thread-safe; they marshal to the UI thread themselves.
/// </summary>
public interface IScanObserver
{
    void OnProgress(in ScanProgress progress);
}
