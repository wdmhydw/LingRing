using GongSiOrganizer.Core;
using Xunit;

namespace GongSiOrganizer.Core.Tests;

public sealed class ArchiveScopeTests
{
    [Fact]
    public void ThisPcRoot_NormalizesWithoutBecomingAFileSystemPath()
    {
        Assert.Equal(
            ArchiveScope.ThisPcRoot,
            ArchiveScope.Normalize("  SHELL:thispc  "));
        Assert.True(ArchiveScope.IsThisPc(ArchiveScope.ThisPcRoot));
        Assert.Equal(
            "此电脑（所有本地磁盘）",
            ArchiveScope.GetDisplayName(ArchiveScope.ThisPcRoot));
    }

    [Fact]
    public void ThisPcDestination_RoundTripsARealLocalFolder()
    {
        using var workspace = new TestWorkspace();

        var token = ArchiveScope.GetDestinationToken(
            ArchiveScope.ThisPcRoot,
            workspace.Root);
        var resolved = ArchiveScope.ResolveDestination(
            ArchiveScope.ThisPcRoot,
            token);

        Assert.Equal(
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(workspace.Root)),
            Path.TrimEndingDirectorySeparator(resolved),
            ignoreCase: true);
    }

    [Fact]
    public void LocalFolder_BecomesADefaultStartUnderThisPc()
    {
        using var workspace = new TestWorkspace();

        var profile = ArchiveScope.CreateNavigationProfile(workspace.Root);

        Assert.Equal(ArchiveScope.ThisPcRoot, profile.RootPath);
        Assert.Equal(
            ArchiveScope.GetDestinationToken(
                ArchiveScope.ThisPcRoot,
                workspace.Root),
            profile.StartPath);
        Assert.Equal(
            Path.TrimEndingDirectorySeparator(workspace.Root),
            Path.TrimEndingDirectorySeparator(
                ArchiveScope.GetNavigationSelectionPath(profile)),
            ignoreCase: true);
    }

    [Fact]
    public void DriveFolderProfile_DoesNotRequireTheDriveToBeOnlineWhenSaved()
    {
        var profile = ArchiveScope.CreateNavigationProfile(@"Q:\归档\资料");

        Assert.Equal(ArchiveScope.ThisPcRoot, profile.RootPath);
        Assert.Equal(@"Q\归档\资料", profile.StartPath);
        Assert.Equal(
            Path.GetFullPath(@"Q:\归档\资料"),
            ArchiveScope.GetNavigationSelectionPath(profile),
            ignoreCase: true);
    }

    [Theory]
    [InlineData(".")]
    [InlineData("..\\Windows")]
    [InlineData("not-a-drive\\folder")]
    public void ThisPcDestination_RejectsTargetsWithoutAValidDrive(string token)
    {
        Assert.ThrowsAny<Exception>(() =>
            ArchiveScope.ResolveDestination(ArchiveScope.ThisPcRoot, token));
    }
}
