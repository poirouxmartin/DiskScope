namespace DiskScope.App.ViewModels;

public sealed record FileRowViewModel(int Id, string Path, long Size, string SizeText)
{
    /// <summary>UI Automation names list rows from ToString.</summary>
    public override string ToString() => Path;
}
