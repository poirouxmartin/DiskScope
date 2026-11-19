namespace DiskScope.App.Services;

public interface IShellActions
{
    void CopyText(string text);

    void ShowInExplorer(string path);

    /// <summary>Save dialog whose filter follows the suggested extension (.csv, .json, .html).</summary>
    string? AskSavePath(string suggestedFileName);
}
