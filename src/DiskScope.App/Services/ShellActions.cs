using System.Diagnostics;
using System.Windows;

namespace DiskScope.App.Services;

public sealed class ShellActions : IShellActions
{
    public void CopyText(string text)
    {
        Clipboard.SetText(text);
    }

    public void ShowInExplorer(string path)
    {
        Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{path}\"") { UseShellExecute = false });
    }
}
