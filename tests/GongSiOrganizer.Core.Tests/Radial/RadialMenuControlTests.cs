using System.Runtime.ExceptionServices;
using GongSiOrganizer.RadialPrototype.Radial;
using Xunit;

namespace GongSiOrganizer.Core.Tests.Radial;

public sealed class RadialMenuControlTests
{
    [Fact]
    public void KeyboardFocus_RemainsEnabledWithoutDefaultFocusRectangle()
    {
        RunOnSta(() =>
        {
            var control = new RadialMenuControl();
            Assert.True(control.Focusable);
            Assert.Null(control.FocusVisualStyle);
        });
    }

    [Fact]
    public void NavigateBack_FromDriveShowsThisPcBeforeClosing()
    {
        RunOnSta(() =>
        {
            var drive = new GongSiOrganizer.Core.FolderMenuNode(
                "E: Storage",
                "E",
                []);
            var session = new RadialMenuSession([drive]);
            Assert.True(session.Hover(0, 0));

            var control = new RadialMenuControl();
            control.Configure(
                RadialLayout.Create(
                    new RadialPoint(960, 540),
                    new RadialRect(0, 0, 1920, 1080)),
                session);

            Assert.True(control.NavigateBack());
            Assert.Empty(session.Path);
            Assert.Equal("E: Storage", session.GetRingView(0).Slots[0]!.DisplayName);
            Assert.False(control.NavigateBack());
        });
    }

    [Fact]
    public void SelectionOnly_DoesNotStartExpansionAnimation()
    {
        var previous = new[]
        {
            View(null, "叶子目录"),
            View(null),
            View(null)
        };
        var current = new[]
        {
            View("叶子目录", "叶子目录"),
            View(null),
            View(null)
        };

        Assert.False(RadialMenuControl.ShouldAnimateExpansion(previous, current));
    }

    [Fact]
    public void NewChildRing_StartsExpansionAnimation()
    {
        var previous = new[]
        {
            View(null, "父目录"),
            View(null),
            View(null)
        };
        var current = new[]
        {
            View("父目录", "父目录"),
            View(null, @"父目录\子目录"),
            View(null)
        };

        Assert.True(RadialMenuControl.ShouldAnimateExpansion(previous, current));
    }

    [Fact]
    public void CollapsingToLeaf_DoesNotStartExpansionAnimation()
    {
        var previous = new[]
        {
            View("父目录", "父目录"),
            View(null, @"父目录\子目录"),
            View(null)
        };
        var current = new[]
        {
            View("叶子目录", "叶子目录"),
            View(null),
            View(null)
        };

        Assert.False(RadialMenuControl.ShouldAnimateExpansion(previous, current));
    }

    private static RadialRingView View(
        string? selectedPath,
        params string[] paths)
    {
        var slots = paths
            .Select(path => new GongSiOrganizer.Core.FolderMenuNode(
                path.Split('\\')[^1],
                path,
                []))
            .Cast<GongSiOrganizer.Core.FolderMenuNode?>()
            .Concat(Enumerable.Repeat<GongSiOrganizer.Core.FolderMenuNode?>(
                null,
                RadialLayout.SlotsPerRing))
            .Take(RadialLayout.SlotsPerRing)
            .ToArray();
        return new RadialRingView(slots, paths.Length, 0, selectedPath);
    }

    private static void RunOnSta(Action action)
    {
        Exception? failure = null;
        var completed = false;
        var thread = new Thread(() =>
        {
            try
            {
                action();
            }
            catch (Exception exception)
            {
                failure = exception;
            }
            finally
            {
                completed = true;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();

        Assert.True(thread.Join(TimeSpan.FromSeconds(10)));
        Assert.True(completed);
        if (failure is not null) ExceptionDispatchInfo.Capture(failure).Throw();
    }
}
