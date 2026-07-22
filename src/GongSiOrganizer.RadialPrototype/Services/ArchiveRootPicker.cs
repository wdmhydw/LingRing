using System.IO;
using GongSiOrganizer.Core;
using Forms = System.Windows.Forms;

namespace GongSiOrganizer.RadialPrototype.Services;

internal static class ArchiveRootPicker
{
    public static string? Pick(string? currentRoot)
    {
        using var dialog = new Forms.FolderBrowserDialog
        {
            Description = "选择需要归档到的根目录。轮盘会读取并展示该目录下的所有子文件夹。",
            UseDescriptionForTitle = true,
            ShowNewFolderButton = true,
            AutoUpgradeEnabled = true
        };
        if (!string.IsNullOrWhiteSpace(currentRoot) && Directory.Exists(currentRoot))
            dialog.SelectedPath = currentRoot;

        return dialog.ShowDialog() == Forms.DialogResult.OK &&
               !string.IsNullOrWhiteSpace(dialog.SelectedPath)
            ? CompanyArchive.NormalizeRoot(dialog.SelectedPath)
            : null;
    }
}
