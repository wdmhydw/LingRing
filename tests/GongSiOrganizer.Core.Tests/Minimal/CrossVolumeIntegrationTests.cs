using Xunit;

namespace GongSiOrganizer.Core.Tests;

public sealed class CrossVolumeIntegrationTests
{
    [Fact]
    [Trait("Category", "Integration")]
    public void Move_UsesRealMoveSemanticsAcrossTwoPhysicalVolumesWhenConfigured()
    {
        var destinationBase = Environment.GetEnvironmentVariable(
            "FILE_QUICK_ARCHIVE_CROSS_VOLUME_DESTINATION");
        if (string.IsNullOrWhiteSpace(destinationBase)) return;

        var sourceRoot = Path.Combine(
            Path.GetTempPath(),
            "FileQuickArchive.CrossVolume",
            Guid.NewGuid().ToString("N"));
        var destinationRoot = Path.Combine(
            Path.GetFullPath(destinationBase),
            Guid.NewGuid().ToString("N"));
        if (string.Equals(
                Path.GetPathRoot(sourceRoot),
                Path.GetPathRoot(destinationRoot),
                StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("集成测试需要两个不同的磁盘卷。");

        try
        {
            Directory.CreateDirectory(sourceRoot);
            Directory.CreateDirectory(destinationRoot);
            var sourceFile = Path.Combine(sourceRoot, "真实跨盘测试.txt");
            File.WriteAllText(sourceFile, "跨盘移动验证");

            var result = new DirectMoveService().Move(sourceFile, destinationRoot);

            Assert.False(File.Exists(sourceFile));
            Assert.Equal("跨盘移动验证", File.ReadAllText(result.DestinationPath));

            var sourceFolder = Path.Combine(sourceRoot, "真实跨盘文件夹");
            Directory.CreateDirectory(Path.Combine(sourceFolder, "下一级", "空目录"));
            File.WriteAllText(
                Path.Combine(sourceFolder, "下一级", "内容.txt"),
                "文件夹跨盘验证");

            var folderResult = new DirectMoveService().Move(sourceFolder, destinationRoot);

            Assert.False(Directory.Exists(sourceFolder));
            Assert.True(Directory.Exists(
                Path.Combine(folderResult.DestinationPath, "下一级", "空目录")));
            Assert.Equal(
                "文件夹跨盘验证",
                File.ReadAllText(
                    Path.Combine(folderResult.DestinationPath, "下一级", "内容.txt")));
        }
        finally
        {
            if (Directory.Exists(sourceRoot)) Directory.Delete(sourceRoot, true);
            if (Directory.Exists(destinationRoot)) Directory.Delete(destinationRoot, true);
        }
    }
}
