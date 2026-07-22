using GongSiOrganizer.RadialPrototype.Services;
using Xunit;

namespace GongSiOrganizer.Core.Tests.Radial;

public sealed class FileTargetResolverTests
{
    [Fact]
    public void Exists_AcceptsOrdinaryFilesAndFolders()
    {
        using var workspace = new TestWorkspace();
        var folder = workspace.DirectoryAt("桌面文件夹");
        var file = workspace.FileAt("test", "桌面文件夹", "文档.txt");

        Assert.True(FileTargetResolver.Exists(folder));
        Assert.True(FileTargetResolver.Exists(file));
        Assert.False(FileTargetResolver.Exists(Path.Combine(workspace.Root, "不存在")));
    }

    [Fact]
    public void NamesMatch_IgnoresLineBreaksInsertedByDesktopIconLabels()
    {
        using var workspace = new TestWorkspace();
        var folder = workspace.DirectoryAt("副本花名册-山西明明生殖科");

        Assert.True(FileTargetResolver.NamesMatch(
            "副本花名册-\r\n山西明明生殖科",
            "副本花名册-山西明明生殖科",
            folder));
    }

    [Fact]
    public void ChooseEffectivePaths_UsesWholeSelectionWhenClickedItemIsSelected()
    {
        using var workspace = new TestWorkspace();
        var first = workspace.FileAt("1", "桌面", "一.txt");
        var second = workspace.FileAt("2", "桌面", "二.txt");

        var paths = FileTargetResolver.ChooseEffectivePaths(
            second,
            [first, second, first]);

        Assert.Equal(2, paths.Count);
        Assert.Contains(first, paths, StringComparer.OrdinalIgnoreCase);
        Assert.Contains(second, paths, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void ChooseEffectivePaths_IgnoresUnrelatedExistingSelection()
    {
        using var workspace = new TestWorkspace();
        var clicked = workspace.FileAt("点击", "桌面", "点击.txt");
        var unrelated = workspace.FileAt("旧选择", "桌面", "旧选择.txt");

        var paths = FileTargetResolver.ChooseEffectivePaths(clicked, [unrelated]);

        Assert.Equal(Path.GetFullPath(clicked), Assert.Single(paths));
    }
}
