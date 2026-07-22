using Xunit;

namespace GongSiOrganizer.Core.Tests;

public sealed class DirectMoveServiceTests
{
    [Fact]
    public void MoveToArchive_ResolvesNestedDestinationAndMovesSource()
    {
        using var workspace = new TestWorkspace();
        var source = workspace.FileAt("内容", "desktop", "合同.pdf");
        var archive = workspace.DirectoryAt("archive");
        workspace.DirectoryAt("archive", "公司甲", "合同");

        var result = new DirectMoveService().MoveToArchive(
            source,
            archive,
            @"公司甲\合同");

        Assert.False(File.Exists(source));
        Assert.Equal(
            Path.Combine(archive, "公司甲", "合同", "合同.pdf"),
            result.DestinationPath);
        Assert.Equal("内容", File.ReadAllText(result.DestinationPath));
    }

    [Fact]
    public void MoveToArchive_RejectsDestinationTraversalWithoutMovingSource()
    {
        using var workspace = new TestWorkspace();
        var source = workspace.FileAt("内容", "desktop", "合同.pdf");
        var archive = workspace.DirectoryAt("archive");

        Assert.Throws<ArgumentException>(() =>
            new DirectMoveService().MoveToArchive(source, archive, @"..\外部"));

        Assert.True(File.Exists(source));
    }

    [Fact]
    public void Move_FileMovesDirectlyAndRemovesSource()
    {
        using var workspace = new TestWorkspace();
        var source = workspace.FileAt("内容", "desktop", "报价.docx");
        var destination = workspace.DirectoryAt("archive", "公司甲");

        var result = new DirectMoveService().Move(source, destination);

        Assert.False(File.Exists(source));
        Assert.Equal("内容", File.ReadAllText(result.DestinationPath));
        Assert.False(result.WasDirectory);
    }

    [Fact]
    public void Move_NameConflictAddsNumberWithoutOverwrite()
    {
        using var workspace = new TestWorkspace();
        var source = workspace.FileAt("新内容", "desktop", "方案.pptx");
        var destination = workspace.DirectoryAt("archive", "公司甲");
        File.WriteAllText(Path.Combine(destination, "方案.pptx"), "原内容");

        var result = new DirectMoveService().Move(source, destination);

        Assert.Equal("原内容", File.ReadAllText(Path.Combine(destination, "方案.pptx")));
        Assert.Equal("方案 (1).pptx", Path.GetFileName(result.DestinationPath));
        Assert.Equal("新内容", File.ReadAllText(result.DestinationPath));
    }

    [Fact]
    public void Move_CrossVolumeStyleFolderMovesEachFileAndKeepsEmptyFolders()
    {
        using var workspace = new TestWorkspace();
        var source = workspace.DirectoryAt("desktop", "活动资料");
        Directory.CreateDirectory(Path.Combine(source, "图片", "空目录"));
        File.WriteAllText(Path.Combine(source, "说明.txt"), "说明");
        File.WriteAllText(Path.Combine(source, "图片", "海报.png"), "png");
        var destination = workspace.DirectoryAt("archive", "公司甲");

        var result = new DirectMoveService(forcePerFileDirectoryMove: true).Move(source, destination);

        Assert.False(Directory.Exists(source));
        Assert.True(Directory.Exists(Path.Combine(result.DestinationPath, "图片", "空目录")));
        Assert.Equal("说明", File.ReadAllText(Path.Combine(result.DestinationPath, "说明.txt")));
        Assert.Equal("png", File.ReadAllText(Path.Combine(result.DestinationPath, "图片", "海报.png")));
    }

    [Fact]
    public void Move_RejectsMovingFolderInsideItself()
    {
        using var workspace = new TestWorkspace();
        var source = workspace.DirectoryAt("desktop", "资料");
        var child = workspace.DirectoryAt("desktop", "资料", "子目录");

        var exception = Assert.Throws<InvalidOperationException>(
            () => new DirectMoveService().Move(source, child));

        Assert.Contains("自己的内部", exception.Message);
        Assert.True(Directory.Exists(source));
    }
}
