using System.IO;
using GongSiOrganizer.Core;
using GongSiOrganizer.RadialPrototype.Presentation;
using Forms = System.Windows.Forms;

namespace GongSiOrganizer.RadialPrototype.Services;

internal static class ArchiveRootPicker
{
    public static string? Pick(string? currentRoot)
    {
        var choice = ArchiveRootChoiceWindow.Show(currentRoot);
        if (choice == ArchiveRootChoice.None) return null;
        if (choice == ArchiveRootChoice.ThisPc) return ArchiveScope.ThisPcRoot;

        using var dialog = new Forms.FolderBrowserDialog
        {
            Description = "从“此电脑”中选择轮盘的默认起点。之后仍可右键逐级返回磁盘和“此电脑”。",
            UseDescriptionForTitle = true,
            ShowNewFolderButton = true,
            AutoUpgradeEnabled = true,
            RootFolder = Environment.SpecialFolder.MyComputer
        };
        if (!string.IsNullOrWhiteSpace(currentRoot) &&
            !ArchiveScope.IsThisPc(currentRoot) &&
            Directory.Exists(currentRoot))
            dialog.SelectedPath = currentRoot;

        return dialog.ShowDialog() == Forms.DialogResult.OK &&
               !string.IsNullOrWhiteSpace(dialog.SelectedPath)
            ? ArchiveScope.Normalize(dialog.SelectedPath)
            : null;
    }
}
