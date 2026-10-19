namespace DiskScope.App.Services;

public interface IShellActions
{
    void CopyText(string text);

    void ShowInExplorer(string path);
}
