namespace DiskScope.App.ViewModels;

public sealed record FileRowViewModel(int Id, string Path, long Size, string SizeText)
{
    /// <summary>File name, shown first in lists so long paths do not hide it.</summary>
    public string Name => System.IO.Path.GetFileName(Path);

    /// <summary>Containing folder, shown in grey after the name.</summary>
    public string Folder => System.IO.Path.GetDirectoryName(Path) ?? string.Empty;

    /// <summary>UI Automation names list rows from ToString.</summary>
    public override string ToString() => Path;
}
