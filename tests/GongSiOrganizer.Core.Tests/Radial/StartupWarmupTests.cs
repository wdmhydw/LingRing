using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using GongSiOrganizer.Core;
using GongSiOrganizer.RadialPrototype.Radial;
using GongSiOrganizer.RadialPrototype.Services;
using Xunit;

namespace GongSiOrganizer.Core.Tests.Radial;

public sealed class StartupWarmupTests
{
    [Fact]
    public void PrepareFirstShow_IsHiddenDoesNotTakeFocusOrLoadChildDirectories()
    {
        RunOnSta(() =>
        {
            var reads = 0;
            var children = new AsyncFolderMenuNodeList(() =>
            {
                reads++;
                return Task.FromResult<IReadOnlyList<FolderMenuNode>>([]);
            });
            var snapshot = new FolderCatalogResult("C:\\", [new("Folder", "Folder", children)], 1, 0);
            var window = new RadialOverlayWindow();
            try
            {
                window.PrepareForFirstShow(snapshot);
                window.PrepareForFirstShow(snapshot);
                var handle = new WindowInteropHelper(window).Handle;
                Assert.NotEqual(IntPtr.Zero, handle);
                Assert.False(window.IsVisible);
                Assert.False(IsWindowVisible(handle));
                Assert.False(window.IsActive);
                Assert.Equal(0, reads);
                var control = (Visual)VisualTreeHelper.GetChild((Visual)window.Content, 0);
                Assert.NotNull(VisualTreeHelper.GetDrawing(control));
            }
            finally { window.Close(); }
        });
    }

    [Fact]
    public void LateWarmup_PreservesAnAlreadyOpenedMenu()
    {
        RunOnSta(() =>
        {
            var snapshot = new FolderCatalogResult("C:\\", [new("Folder", "Folder", [])], 1, 0);
            var window = new RadialOverlayWindow();
            try
            {
                window.ShowMenu(new NativeScreenPoint(500, 400), ["sample.txt"], snapshot);
                var content = window.Content;
                var width = window.Width;
                var height = window.Height;
                window.PrepareForFirstShow(new FolderCatalogResult("C:\\", [], 0, 0));
                Assert.True(window.IsVisible);
                Assert.Same(content, window.Content);
                Assert.Equal(width, window.Width);
                Assert.Equal(height, window.Height);
                var surface = (Visual)window.Content;
                var control = (RadialMenuControl)VisualTreeHelper.GetChild(surface, 0);
                Assert.True(control.HoverDemo(0));
            }
            finally { window.Close(); }
        });
    }

    [Fact]
    public async Task DefaultStart_LoadsOnlyItsAncestorsAndImmediateChildren()
    {
        var loads = new List<string>();
        AsyncFolderMenuNodeList Children(string path, params FolderMenuNode[] nodes) => new(() =>
        {
            loads.Add(path);
            return Task.FromResult<IReadOnlyList<FolderMenuNode>>(nodes);
        });
        var deeper = Children("deeper");
        var target = Children("target", new FolderMenuNode("Child", @"C\Target\Child", deeper));
        var sibling = Children("sibling");
        var drive = Children("drive", new("Target", @"C\Target", target), new("Sibling", @"C\Sibling", sibling));
        var otherDrive = Children("other-drive");
        using var session = new RadialMenuSession([new("C", "C", drive), new("D", "D", otherDrive)]);

        Assert.True(await session.SelectStartPathAsync(@"C\Target"));
        Assert.Equal(new[] { "drive", "target" }, loads);
        Assert.False(deeper.IsLoaded);
        Assert.False(sibling.IsLoaded);
        Assert.False(otherDrive.IsLoaded);
    }

    private static void RunOnSta(Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try { action(); }
            catch (Exception exception) { failure = exception; }
            finally { System.Windows.Threading.Dispatcher.CurrentDispatcher.InvokeShutdown(); }
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(15)));
        if (failure is not null) ExceptionDispatchInfo.Capture(failure).Throw();
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowVisible(IntPtr window);
}
