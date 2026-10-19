namespace DiskScope.App.ViewModels;

public sealed record ExtensionRowViewModel(string Extension, long FileCount, long TotalSize, string SizeText, string PercentText)
{
    /// <summary>Files without an extension are shown as "(none)".</summary>
    public string DisplayName => Extension.Length == 0 ? "(none)" : Extension;

    /// <summary>UI Automation names list rows from ToString.</summary>
    public override string ToString() => DisplayName;
}
