using System.Diagnostics;
using System.Windows;
using DiskScope.App.Localization;

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
                ".json" => Loc.Get("Dialog_JsonFilter"),
                ".html" => Loc.Get("Dialog_HtmlFilter"),
                _ => Loc.Get("Dialog_CsvFilter"),
            },
            DefaultExt = ext.Length > 0 ? ext : ".csv",
            OverwritePrompt = true,
        };
        return dlg.ShowDialog() == true ? dlg.FileName : null;
    }

    public string? AskFolder(string initialFolder)
    {
        var dlg = new Microsoft.Win32.OpenFolderDialog { InitialDirectory = initialFolder };
        return dlg.ShowDialog() == true ? dlg.FolderName : null;
    }
}
