using GongSiOrganizer.RadialPrototype;
using GongSiOrganizer.Core;
using Xunit;

namespace GongSiOrganizer.Core.Tests.Radial;

public sealed class PrototypeOptionsTests
{
    [Fact]
    public void Parse_DefaultRoot_IsNotMarkedAsExplicit()
    {
        var options = PrototypeOptions.Parse([]);

        Assert.Equal(ArchiveScope.ThisPcRoot, options.RootPath);
        Assert.Null(options.StartPath);
        Assert.False(options.RootWasSpecified);
    }

    [Fact]
    public void Parse_RootArgument_IsMarkedAsExplicit()
    {
        using var workspace = new TestWorkspace();

        var options = PrototypeOptions.Parse(["--root", workspace.Root]);

        Assert.Equal(ArchiveScope.ThisPcRoot, options.RootPath);
        Assert.Equal(
            ArchiveScope.GetDestinationToken(
                ArchiveScope.ThisPcRoot,
                workspace.Root),
            options.StartPath);
        Assert.True(options.RootWasSpecified);
    }

    [Fact]
    public void Parse_ThisPcRoot_RemainsVirtual()
    {
        var options = PrototypeOptions.Parse(
            ["--root", ArchiveScope.ThisPcRoot]);

        Assert.Equal(ArchiveScope.ThisPcRoot, options.RootPath);
        Assert.Null(options.StartPath);
        Assert.True(options.RootWasSpecified);
    }

    [Theory]
    [InlineData("--demo-onboarding")]
    [InlineData("--demo-root-choice")]
    [InlineData("--demo-tray-menu")]
    public void Parse_VisualReviewModes_SuppressAutomaticOnboarding(string argument)
    {
        var options = PrototypeOptions.Parse([argument]);

        Assert.True(options.IsDemoLaunch);
    }

    [Theory]
    [InlineData("0", 0)]
    [InlineData("3", 3)]
    [InlineData("99", 3)]
    [InlineData("-5", 0)]
    public void Parse_DemoOnboardingPage_ClampsToAvailablePages(string value, int expected)
    {
        var options = PrototypeOptions.Parse(["--demo-onboarding-page", value]);

        Assert.Equal(expected, options.DemoOnboardingPage);
    }
}
