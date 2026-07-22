using Xunit;

namespace GongSiOrganizer.Core.Tests;

public sealed class ExplorerMenuInstallerTests
{
    [Fact]
    public void GetShellExtensionFilePaths_UsesAdjacentShellDirectory()
    {
        var executable = @"C:\Tools\公司工具\GongSiOrganizer.exe";

        var files = ExplorerMenuInstaller.GetShellExtensionFilePaths(executable);

        Assert.Equal(4, files.Count);
        Assert.Equal(
            @"C:\Tools\公司工具\shell\GongSiOrganizer.ShellExtension.V4.comhost.dll",
            files[0]);
        Assert.All(files, file => Assert.Equal(@"C:\Tools\公司工具\shell", Path.GetDirectoryName(file)));
    }
}
