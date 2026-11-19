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

    public string? AskSavePath(string suggestedFileName)
    {
        string ext = System.IO.Path.GetExtension(suggestedFileName).ToLowerInvariant();
        var dlg = new Microsoft.Win32.SaveFileDialog
        {
            FileName = suggestedFileName,
            Filter = ext switch
            {
                ".json" => "JSON files (*.json)|*.json",
                ".html" => "HTML files (*.html)|*.html",
                _ => "CSV files (*.csv)|*.csv",
            },
            DefaultExt = ext.Length > 0 ? ext : ".csv",
            OverwritePrompt = true,
        };
        return dlg.ShowDialog() == true ? dlg.FileName : null;
    }
}
