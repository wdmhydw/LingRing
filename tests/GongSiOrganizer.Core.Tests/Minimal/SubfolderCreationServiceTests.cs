using GongSiOrganizer.Core;
using Xunit;

namespace GongSiOrganizer.Core.Tests;

public sealed class SubfolderCreationServiceTests
{
    private readonly SubfolderCreationService _service = new();

    [Fact]
    public void Create_CreatesFolderUnderTheSelectedParent()
    {
        using var workspace = new TestWorkspace();
        workspace.DirectoryAt("公司甲", "项目A");

        var result = _service.Create(workspace.Root, @"公司甲\项目A", " 新资料 ");

        Assert.True(Directory.Exists(result.FullPath));
        Assert.Equal(@"公司甲\项目A\新资料", result.RelativePath);
        Assert.Equal("新资料", result.DisplayName);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(".")]
    [InlineData("..")]
    [InlineData("CON")]
    [InlineData("com1.txt")]
    [InlineData("上级\\下级")]
    [InlineData("非法:名称")]
    public void ValidateName_RejectsUnsafeNames(string name)
    {
        Assert.Throws<ArgumentException>(() => SubfolderCreationService.ValidateName(name));
    }

    [Fact]
    public void Create_RejectsAnExistingFileOrFolder()
    {
        using var workspace = new TestWorkspace();
        workspace.DirectoryAt("公司甲", "已有目录");

        var exception = Assert.Throws<IOException>(() =>
            _service.Create(workspace.Root, "公司甲", "已有目录"));

        Assert.Contains("已经存在", exception.Message);
    }

    [Fact]
    public void Create_RejectsAnExistingFileWithTheRequestedName()
    {
        using var workspace = new TestWorkspace();
        workspace.DirectoryAt("公司甲");
        workspace.FileAt("内容", "公司甲", "已有名称");

        var exception = Assert.Throws<IOException>(() =>
            _service.Create(workspace.Root, "公司甲", "已有名称"));

        Assert.Contains("已经存在", exception.Message);
    }

    [Fact]
    public void Create_RejectsParentOutsideTheArchive()
    {
        using var workspace = new TestWorkspace();

        Assert.Throws<ArgumentException>(() =>
            _service.Create(workspace.Root, "..", "新目录"));
    }

    [Fact]
    public void Create_RejectsMissingParent()
    {
        using var workspace = new TestWorkspace();

        Assert.Throws<DirectoryNotFoundException>(() =>
            _service.Create(workspace.Root, "不存在", "新目录"));
    }

    [Fact]
    public void Create_WorksInsideTheThisPcVirtualScope()
    {
        using var workspace = new TestWorkspace();
        var parent = workspace.DirectoryAt("归档");
        var parentToken = ArchiveScope.GetDestinationToken(
            ArchiveScope.ThisPcRoot,
            parent);

        var result = _service.Create(
            ArchiveScope.ThisPcRoot,
            parentToken,
            "新目录");

        Assert.True(Directory.Exists(result.FullPath));
        Assert.Equal(
            Path.Combine(parentToken, "新目录"),
            result.RelativePath);
    }
}
