using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using GongSiOrganizer.Core;
using GongSiOrganizer.RadialPrototype;
using GongSiOrganizer.RadialPrototype.Presentation;
using GongSiOrganizer.RadialPrototype.Radial;
using GongSiOrganizer.RadialPrototype.Services;
using Xunit;

namespace GongSiOrganizer.Core.Tests.Radial;

public sealed class MoveFeedbackTests
{
    [Fact]
    public void FastMoveNeverShowsWaitingOrInventedProgress()
    {
        var timeline = new MoveFeedbackTimeline();
        timeline.Complete(MoveFeedbackOutcome.Success, 3, 3, 12);
        Assert.Equal(0, timeline.At(60, true).Badge);
        Assert.False(timeline.At(300, true).Pending);
        Assert.True(timeline.At(400, true).Badge > 0);
        Assert.False(timeline.At(800, true).Finished);
        Assert.True(timeline.At(1100, true).Finished);
    }

    [Fact]
    public void SlowMoveWaitsForActualResultBeforeShowingSuccess()
    {
        var timeline = new MoveFeedbackTimeline();
        Assert.False(timeline.At(240, true).Pending);
        Assert.True(timeline.At(500, true).Pending);
        Assert.True(timeline.At(60000, true).Pending);
        Assert.Null(timeline.Outcome);
        Assert.False(timeline.At(60000, true).Finished);
        timeline.Complete(MoveFeedbackOutcome.Success, 5, 5, 60000);
        Assert.False(timeline.At(60200, true).Pending);
        Assert.True(timeline.At(61000, true).Finished);
    }

    [Theory]
    [InlineData(1, 2)]
    [InlineData(2, 0)]
    public void FailureCountsAndOutcomeSurviveDuplicateCompletion(int outcomeValue, int moved)
    {
        var outcome = (MoveFeedbackOutcome)outcomeValue;
        var timeline = new MoveFeedbackTimeline();
        timeline.Complete(outcome, moved, 3, 500);
        timeline.Complete(MoveFeedbackOutcome.Success, 3, 3, 600);
        Assert.Equal(outcome, timeline.Outcome);
        Assert.Equal(moved, timeline.Moved);
        Assert.Equal(3, timeline.Total);
        Assert.False(timeline.At(1600, true).Finished);
        Assert.True(timeline.At(1900, true).Finished);
    }

    [Fact]
    public void ReducedMotionKeepsReadableOutcomeWithoutSpatialAnimation()
    {
        var timeline = new MoveFeedbackTimeline();
        var waiting = timeline.At(500, false);
        Assert.Equal(1, waiting.Collapse);
        Assert.Equal(waiting.SpinnerAngle, timeline.At(900, false).SpinnerAngle);
        timeline.Complete(MoveFeedbackOutcome.Partial, 2, 3, 900);
        var result = timeline.At(901, false);
        Assert.Equal(1, result.Badge);
        Assert.Equal(1, result.Opacity);
        Assert.False(result.Finished);
        Assert.True(timeline.At(2200, false).Finished);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(250, 0)]
    [InlineData(0, 250)]
    [InlineData(250, 250)]
    public void FeedbackCircleAndLabelsFitAtEveryCorner(double x, double y)
    {
        var center = MoveFeedbackControl.FitCenter(new Point(x, y), new Size(270, 270));
        Assert.InRange(center.X - 77, 0, 270);
        Assert.InRange(center.X + 77, 0, 270);
        Assert.InRange(center.Y - 44, 0, 270);
        Assert.InRange(center.Y + 84, 0, 270);
    }

    [Fact]
    public void ReducedMotionRendersDistinctPendingSuccessAndFailureFrames() => OnSta(() =>
    {
        var view = new MoveFeedbackControl(null, new Point(135, 135)) { Width = 270, Height = 270 };
        view.Advance(500, false);
        var waiting = Render(view);
        view.Advance(700, false);
        Assert.Equal(waiting, Render(view));
        view.Timeline.Complete(MoveFeedbackOutcome.Success, 3, 3, 800);
        view.Advance(900, false);
        var success = Render(view);
        Assert.NotEqual(waiting, success);
        var failed = new MoveFeedbackControl(null, new Point(135, 135)) { Width = 270, Height = 270 };
        failed.Timeline.Complete(MoveFeedbackOutcome.Failure, 0, 3, 800);
        failed.Advance(900, false);
        Assert.NotEqual(success, Render(failed));
    });

    [Fact]
    public void NewMenuDismissesOldClickThroughFeedbackAndLateResultDoesNotReopenIt() => OnSta(() =>
    {
        var point = NativeDisplay.GetPrimaryWorkAreaCenter();
        var overlay = new RadialOverlayWindow();
        MoveFeedbackWindow? feedback = null;
        try
        {
            var catalog = new FolderCatalogResult("C:\\", [new FolderMenuNode("测试目录", "test", [])], 1, 0);
            overlay.ShowMenu(point, ["fixture.txt"], catalog);
            Pump(60);
            feedback = overlay.StartMoveFeedback();
            Assert.NotNull(feedback);
            Assert.False(overlay.IsVisible);
            Assert.True(feedback.IsVisible);
            var handle = new WindowInteropHelper(feedback).Handle;
            var style = GetWindowLongPtr(handle, -20).ToInt64();
            Assert.Equal(0x08000020, style & 0x08000020);
            Assert.NotEqual(handle, GetForegroundWindow());
            overlay.ShowMenu(point, ["second.txt"], catalog);
            Assert.True(feedback.IsDismissed);
            feedback.Complete(MoveFeedbackOutcome.Success, 1, 1);
            Pump(100);
            Assert.False(feedback.IsVisible);
            Assert.True(overlay.IsVisible);
        }
        finally { feedback?.Dismiss(); overlay.Close(); }
    });

    [Theory]
    [InlineData("success")]
    [InlineData("partial")]
    [InlineData("failure")]
    [InlineData("busy")]
    public void FeedbackPreviewIsAlwaysIsolatedFromNormalInput(string outcome)
    {
        var options = PrototypeOptions.Parse(["--demo-feedback", outcome, "--demo-reduced-motion"]);
        Assert.True(options.IsDemoLaunch);
        Assert.Equal(outcome, options.DemoFeedback);
        Assert.True(options.DemoReducedMotion);
    }

    [Fact]
    public void ConfirmMovesFixtureOnceWithoutWaitingForFeedbackToFinish() => OnSta(() =>
    {
        using var workspace = new TestWorkspace();
        var source = System.IO.Path.Combine(workspace.Root, "source.txt");
        var destination = System.IO.Path.Combine(workspace.Root, "target");
        System.IO.File.WriteAllText(source, "fixture");
        System.IO.Directory.CreateDirectory(destination);
        var overlay = new RadialOverlayWindow();
        MoveFeedbackWindow? feedback = null;
        var requests = 0;
        try
        {
            overlay.MoveRequested += (sources, relative) =>
            {
                requests++;
                feedback = overlay.StartMoveFeedback();
                var result = new DirectMoveBatchService().MoveToArchive(sources, workspace.Root, relative);
                feedback?.Complete(MoveFeedbackOutcome.Success, result.Moved.Count, result.TotalCount);
            };
            var point = NativeDisplay.GetPrimaryWorkAreaCenter();
            var placement = NativeDisplay.CreatePlacement(point, false);
            overlay.ShowMenu(point, [source], new FolderCatalogResult(workspace.Root,
                [new FolderMenuNode("目标", "target", [])], 1, 0));
            Pump(UiMotion.RingOpen + 80);
            var canvas = Assert.IsType<System.Windows.Controls.Canvas>(overlay.Content);
            var control = Assert.IsType<RadialMenuControl>(canvas.Children[0]);
            var body = RadialGeometry.Polar(placement.Layout.LocalCenter, 88, -90);
            control.ProcessPrimaryClick(body);
            control.ProcessPrimaryClick(body);
            Assert.Equal(1, requests);
            Assert.False(System.IO.File.Exists(source));
            Assert.True(System.IO.File.Exists(System.IO.Path.Combine(destination, "source.txt")));
            Assert.NotNull(feedback);
            Assert.True(feedback.IsVisible);
            Assert.False(overlay.IsVisible);
        }
        finally { feedback?.Dismiss(); overlay.Close(); }
    });

    private static byte[] Render(FrameworkElement view)
    {
        view.Measure(new Size(view.Width, view.Height));
        view.Arrange(new Rect(0, 0, view.Width, view.Height));
        view.UpdateLayout();
        var bitmap = new RenderTargetBitmap((int)view.Width, (int)view.Height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(view);
        var bytes = new byte[bitmap.PixelWidth * bitmap.PixelHeight * 4];
        bitmap.CopyPixels(bytes, bitmap.PixelWidth * 4, 0);
        Assert.Contains(bytes, value => value != 0);
        return System.Security.Cryptography.SHA256.HashData(bytes);
    }

    private static void Pump(int milliseconds)
    {
        var frame = new DispatcherFrame();
        var timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(milliseconds) };
        timer.Tick += (_, _) => { timer.Stop(); frame.Continue = false; };
        timer.Start();
        Dispatcher.PushFrame(frame);
    }

    private static void OnSta(Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try { action(); }
            catch (Exception ex) { failure = ex; }
            finally { Dispatcher.CurrentDispatcher.InvokeShutdown(); }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(12)));
        if (failure is not null) ExceptionDispatchInfo.Capture(failure).Throw();
    }

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern IntPtr GetWindowLongPtr(IntPtr window, int index);
    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();
}
