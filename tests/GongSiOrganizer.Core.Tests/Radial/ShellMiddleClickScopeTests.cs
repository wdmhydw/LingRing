using GongSiOrganizer.RadialPrototype.Services;
using Xunit;

namespace GongSiOrganizer.Core.Tests.Radial;

public sealed class ShellMiddleClickScopeTests
{
    [Theory]
    [InlineData("explorer", "DirectUIHWND", "SHELLDLL_DefView", "CabinetWClass")]
    [InlineData("EXPLORER", "SysListView32", "SHELLDLL_DefView", "WorkerW")]
    public void IsSupportedHierarchy_AcceptsDesktopAndExplorerFileViews(
        string processName,
        params string[] hierarchy)
    {
        Assert.True(ShellMiddleClickScope.IsSupportedHierarchy(processName, hierarchy));
    }

    [Theory]
    [InlineData("explorer", "Shell_TrayWnd", "ReBarWindow32")]
    [InlineData("explorer", "DirectUIHWND", "CabinetWClass")]
    [InlineData("chrome", "SHELLDLL_DefView", "Chrome_WidgetWin_1")]
    [InlineData("Codex", "SysListView32", "Chrome_WidgetWin_1")]
    public void IsSupportedHierarchy_RejectsOtherWindowsAndExplorerChrome(
        string processName,
        params string[] hierarchy)
    {
        Assert.False(ShellMiddleClickScope.IsSupportedHierarchy(processName, hierarchy));
    }
}
