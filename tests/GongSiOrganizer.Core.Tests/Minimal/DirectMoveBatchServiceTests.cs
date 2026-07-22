using Xunit;

namespace GongSiOrganizer.Core.Tests;

public sealed class DirectMoveBatchServiceTests
{
    [Fact]
    public void MoveToArchive_MovesEverySelectedItem()
    {
        using var workspace = new TestWorkspace();
        var first = workspace.FileAt("一", "desktop", "一.txt");
        var second = workspace.FileAt("二", "desktop", "二.txt");
        var archive = workspace.DirectoryAt("archive");
        workspace.DirectoryAt("archive", "公司甲");

        var result = new DirectMoveBatchService().MoveToArchive(
            [first, second],
            archive,
            "公司甲");

        Assert.Equal(2, result.Moved.Count);
        Assert.Empty(result.Failures);
        Assert.False(File.Exists(first));
        Assert.False(File.Exists(second));
        Assert.True(File.Exists(Path.Combine(archive, "公司甲", "一.txt")));
        Assert.True(File.Exists(Path.Combine(archive, "公司甲", "二.txt")));
    }

    [Fact]
    public void MoveToArchive_ContinuesAfterOneSelectedItemFails()
    {
        using var workspace = new TestWorkspace();
        var existing = workspace.FileAt("内容", "desktop", "存在.txt");
        var missing = Path.Combine(workspace.Root, "desktop", "不存在.txt");
        var archive = workspace.DirectoryAt("archive");
        workspace.DirectoryAt("archive", "公司甲");

        var result = new DirectMoveBatchService().MoveToArchive(
            [missing, existing],
            archive,
            "公司甲");

        Assert.Single(result.Moved);
        Assert.Single(result.Failures);
        Assert.Equal(missing, result.Failures[0].SourcePath);
        Assert.True(File.Exists(Path.Combine(archive, "公司甲", "存在.txt")));
    }
}
