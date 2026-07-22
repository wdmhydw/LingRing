using Xunit;

namespace GongSiOrganizer.Core.Tests;

public sealed class FolderCatalogTests
{
    [Fact]
    public void Scan_BuildsNestedTreeAndSkipsSynologyInternalFolder()
    {
        using var workspace = new TestWorkspace();
        var root = workspace.DirectoryAt("archive");
        Directory.CreateDirectory(Path.Combine(root, "公司甲", "项目一"));
        Directory.CreateDirectory(Path.Combine(root, "公司乙"));
        Directory.CreateDirectory(Path.Combine(root, ".SynologyWorkingDirectory", "internal"));

        var result = new FolderCatalog().Scan(root);

        Assert.Equal(3, result.FolderCount);
        Assert.Equal(2, result.Folders.Count);
        var company = Assert.Single(result.Folders, item => item.DisplayName == "公司甲");
        Assert.Equal(@"公司甲\项目一", Assert.Single(company.Children).RelativePath);
        Assert.True(result.SkippedCount >= 1);
    }

    [Fact]
    public void Scan_SkipsHiddenFolders()
    {
        using var workspace = new TestWorkspace();
        var root = workspace.DirectoryAt("archive");
        var hidden = Directory.CreateDirectory(Path.Combine(root, "隐藏目录"));
        hidden.Attributes |= FileAttributes.Hidden;

        var result = new FolderCatalog().Scan(root);

        Assert.Empty(result.Folders);
    }
}
