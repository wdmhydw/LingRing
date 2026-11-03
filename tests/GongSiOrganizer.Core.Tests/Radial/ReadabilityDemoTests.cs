using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using GongSiOrganizer.Core;
using GongSiOrganizer.RadialPrototype.Presentation;
using GongSiOrganizer.RadialPrototype.Radial;
using Xunit;

namespace GongSiOrganizer.Core.Tests.Radial;

public sealed class ReadabilityDemoTests
{
    [Theory]
    [InlineData(4)]
    [InlineData(6)]
    [InlineData(8)]
    public void CapacityControlsVisibleItemsAndRotation(int capacity)
    {
        var roots = Enumerable.Range(0, 12).Select(i => new FolderMenuNode($"Item {i}", $"item{i}", [])).ToArray();
        using var session = new RadialMenuSession(roots, [capacity, capacity, capacity]);
        Assert.Equal(capacity, session.GetRingView(0).Slots.Count);
        Assert.Equal($"item{capacity - 1}", session.GetFolder(0, capacity - 1)!.RelativePath);
        Assert.Null(session.GetFolder(0, capacity));
        Assert.Equal(capacity, session.GetRingView(2).Slots.Count);
        Assert.True(session.Rotate(0, 1));
        Assert.Equal($"item{capacity}", session.GetFolder(0, capacity - 1)!.RelativePath);
        using var exact = new RadialMenuSession(roots.Take(capacity).ToArray(), [capacity, capacity, capacity]);
        Assert.False(exact.Rotate(0, 1));
        Assert.Equal(-1, RadialInteractionPolicy.ResolveScrollableRing(new RadialHit(0, 0, false), [exact.GetRingView(0)]));
    }

    [Theory]
    [InlineData(4, false)]
    [InlineData(6, false)]
    [InlineData(8, false)]
    [InlineData(8, true)]
    public void EverySlotMapsBackToItsOwnHitIncludingCorners(int capacity, bool corner)
    {
        var layout = RadialLayout.Create(corner ? new RadialPoint(50, 900) : new RadialPoint(800, 500),
            new RadialRect(0, 0, 1600, 960), ringWidth: 82);
        for (var ring = 0; ring < 3; ring++)
        for (var slot = 0; slot < capacity; slot++)
        {
            var band = layout.Rings[ring];
            var middle = RadialLayout.SlotStart(band, capacity) + (slot + .5) * RadialLayout.SlotSweep(band, capacity);
            var point = RadialGeometry.Polar(layout.LocalCenter, (band.InnerRadius + band.OuterRadius) / 2, middle);
            Assert.Equal(new RadialHit(ring, slot, false), RadialLayout.HitTest(layout, new RadialPoint(point.X, point.Y), capacity, capacity, capacity));
            if (ring != 2) continue;
            var arc = RadialInteractionPolicy.EntryArc(layout, new RadialHit(ring, slot, false), capacity);
            var start = RadialLayout.SlotStart(band, capacity) + slot * RadialLayout.SlotSweep(band, capacity);
            Assert.True(arc.Start > start && arc.End < start + RadialLayout.SlotSweep(band, capacity));
        }
    }

    [Theory]
    [InlineData(4)]
    [InlineData(6)]
    [InlineData(8)]
    public void ExpandedCapacityStillFinishesWithinThe280MillisecondClock(int capacity)
    {
        var finalTime = UiMotion.RingSlotDelay(capacity - 1, capacity) + UiMotion.RingSlotDuration;
        Assert.InRange(finalTime, 250, UiMotion.RingOpen);
    }

    [Fact]
    public void CornerAdaptiveCapacityRespectsUsableArcLength()
    {
        var layout = RadialLayout.Create(new RadialPoint(50, 650), new RadialRect(0, 0, 720, 720), ringWidth: 82);
        var adaptive = new RadialDisplayOptions(8, AdaptCountToSpace: true).Capacities(layout);
        Assert.Contains(adaptive, capacity => capacity < 8);
        Assert.All(adaptive, capacity => Assert.InRange(capacity, 2, 8));
        Assert.Equal(new[] { 8, 8, 8 }, new RadialDisplayOptions(8).Capacities(layout));
    }

    [Theory]
    [InlineData(6)]
    [InlineData(8)]
    public void LeftArrowWrapsToLastVisibleItem(int capacity) => OnSta(() =>
    {
        using var session = new RadialMenuSession(Enumerable.Range(0, 10).Select(i => new FolderMenuNode(i.ToString(), i.ToString(), [])).ToArray(), [capacity, capacity, capacity]);
        var control = new RadialMenuControl();
        control.Configure(RadialLayout.Create(new RadialPoint(500, 400), new RadialRect(0, 0, 1000, 800)), session);
        var window = new Window { Content = control, Width = 600, Height = 600, Left = -32000, Top = -32000, ShowInTaskbar = false, ShowActivated = false };
        try
        {
            window.Show(); control.HoverDemo(0);
            control.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(control), 0, Key.Left) { RoutedEvent = Keyboard.KeyDownEvent });
            Assert.Equal(capacity - 1, control.ReadingHit.SlotIndex);
        }
        finally { control.ReleaseSession(); window.Close(); }
    });

    [Theory]
    [InlineData(-90)]
    [InlineData(0)]
    [InlineData(90)]
    [InlineData(180)]
    public void ArcTextFollowsTheBandAndEveryGlyphStaysInsideItsCell(double middle) => OnSta(() =>
    {
        var center = new RadialPoint(420, 420);
        var shape = RadialGeometry.Wedge(center, 220, 302, middle - 29, middle + 29);
        var label = RadialArcLabel.Create("市场推广方案与产品介绍_2026正式发布", shape,
            new RadialLabelArc(center, 220, 302, middle - 29, middle + 29), 1);
        Assert.True(label.VisibleCharacters >= 15);
        Assert.NotEmpty(label.Drawing.Children);
        Assert.NotNull(label.InkBoxes);
        Assert.All(label.InkBoxes!, box => Assert.Equal(IntersectionDetail.FullyContains, shape.FillContainsWithDetail(box)));
    });

    [Fact]
    public void AdaptiveLabelsDoNotRevealLessThanEitherAvailableLayout() => OnSta(() =>
    {
        var center = new RadialPoint(420, 420);
        var shape = RadialGeometry.Wedge(center, 220, 302, 151, 209);
        var anchor = RadialGeometry.Polar(center, 261, 180);
        var arc = new RadialLabelArc(center, 220, 302, 151, 209);
        const string name = "客户方案与多轮修改记录_最终确认版";
        var horizontal = RadialExperimentalLabel.Create(name, shape, anchor, RadialLabelDirection.Horizontal, 1, arc);
        var curved = RadialExperimentalLabel.Create(name, shape, anchor, RadialLabelDirection.Arc, 1, arc);
        var adaptive = RadialExperimentalLabel.Create(name, shape, anchor, RadialLabelDirection.Adaptive, 1, arc);
        Assert.True(adaptive.VisibleCharacters >= horizontal.VisibleCharacters);
        Assert.True(adaptive.VisibleCharacters >= curved.VisibleCharacters);
    });

    [Fact]
    public void ArcLayoutPreservesGraphemeClusters()
    {
        const string name = "客户e\u0301👨‍👩‍👧‍👦AI2026";
        var elements = RadialExperimentalLabel.TextElements(name);
        Assert.Contains("e\u0301", elements);
        Assert.Contains("👨‍👩‍👧‍👦", elements);
        Assert.Equal(name, string.Concat(elements));
    }

    private static void OnSta(Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try { action(); }
            catch (Exception exception) { failure = exception; }
            finally { System.Windows.Threading.Dispatcher.CurrentDispatcher.InvokeShutdown(); }
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA); thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(15)));
        if (failure is not null) ExceptionDispatchInfo.Capture(failure).Throw();
    }
}
