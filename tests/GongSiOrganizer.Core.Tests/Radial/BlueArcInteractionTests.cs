using System.Globalization;
using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Threading;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using GongSiOrganizer.Core;
using GongSiOrganizer.RadialPrototype.Presentation;
using GongSiOrganizer.RadialPrototype.Radial;
using Xunit;

namespace GongSiOrganizer.Core.Tests.Radial;

public sealed class BlueArcInteractionTests
{
    [Theory]
    [InlineData(960, 540, 4, 4, 3)]
    [InlineData(50, 540, 4, 4, 3)]
    [InlineData(1870, 540, 4, 4, 3)]
    [InlineData(960, 50, 4, 4, 3)]
    [InlineData(960, 1030, 4, 4, 3)]
    [InlineData(50, 50, 4, 4, 3)]
    [InlineData(1870, 50, 4, 4, 3)]
    [InlineData(50, 1030, 4, 4, 3)]
    [InlineData(1870, 1030, 4, 4, 3)]
    [InlineData(960, 540, 3, 1, 2)]
    [InlineData(960, 540, 1, 2, 4)]
    [InlineData(50, 50, 3, 1, 2)]
    public void NativeWheelHasUniformDirectoryTypographyAndNoFolderIcons(double x, double y,
        int firstCount, int secondCount, int thirdCount) => OnSta(() =>
    {
        var outer = new[] { "公众号", "圆桌荟", "明亮家", "资料库" }.Take(thirdCount).Select(name => new FolderMenuNode(name, name, [])).ToArray();
        var middle = new[] { "企划", "生殖科", "新媒体", "产科" }.Take(secondCount).Select(name => new FolderMenuNode(name, name, outer)).ToArray();
        var roots = new[] { "明明", "维蜜", "爱尔", "安琪儿" }.Take(firstCount).Select(name => new FolderMenuNode(name, name, middle)).ToArray();
        var plan = RadialLayout.Create(new RadialPoint(x, y), new RadialRect(0, 0, 1920, 1080));
        using var fixture = new MenuFixture(layout: plan, roots: roots);
        fixture.Control.UpdateLayout();
        Pump(40);
        var folderCharacters = "明维蜜爱尔安琪儿企划生殖科新媒体产公众号圆桌荟亮家";
        var glyphs = new List<GlyphRun>();
        var icons = 0;
        void Visit(Drawing drawing, Geometry? clip = null)
        {
            if (drawing is DrawingGroup group) foreach (var child in group.Children) Visit(child, group.ClipGeometry ?? clip);
            if (drawing is ImageDrawing) icons++;
            if (drawing is GlyphRunDrawing text && text.GlyphRun.Characters.Any(folderCharacters.Contains))
            {
                glyphs.Add(text.GlyphRun);
                if (clip is not null)
                    Assert.Equal(IntersectionDetail.FullyContains, clip.FillContainsWithDetail(new RectangleGeometry(text.Bounds)));
            }
        }
        Visit(VisualTreeHelper.GetDrawing(fixture.Control));
        var diagnostics = string.Join(" | ", Enumerable.Range(0, firstCount).Select(slot =>
        {
            var sweep = RadialLayout.SlotSweep(plan.Rings[0], firstCount);
            var start = RadialLayout.SlotStart(plan.Rings[0], firstCount) + slot * sweep;
            var gap = RadialLayout.CellGap(plan.Rings[0], firstCount);
            var shape = RadialGeometry.Wedge(plan.LocalCenter, 58, 118, start + gap, start + sweep - gap);
            var content = RadialCellContent.Fit(shape, RadialGeometry.Polar(plan.LocalCenter, 88, start + sweep / 2), plan.LocalCenter);
            return $"{roots[slot].DisplayName}: one={content.SingleLine.Width}, two={content.TwoLines.Width}";
        }));
        Assert.True(glyphs.Count >= firstCount + secondCount + thirdCount,
            string.Join(" / ", glyphs.Select(g => new string(g.Characters.ToArray()))) + " ; " + diagnostics);
        Assert.All(glyphs, glyph => Assert.Equal(12.5, glyph.FontRenderingEmSize));
        Assert.Single(glyphs.Select(g => (g.GlyphTypeface.FontUri, g.GlyphTypeface.StyleSimulations)).Distinct());
        Assert.Equal(0, icons);
        var renderedNames = string.Concat(glyphs.Select(g => new string(g.Characters.ToArray())));
        foreach (var shortName in roots.Concat(middle).Select(node => node.DisplayName).Where(name => name.Length == 2))
            Assert.True(renderedNames.Contains(shortName, StringComparison.Ordinal), $"Missing {shortName}: {renderedNames}; {diagnostics}");
    });

    [Fact]
    public void FullNameTipShowsDiskPathsInsteadOfInternalTokens()
    {
        Assert.Equal(@"E:\GongSi\客户资料", RadialNameTip.DisplayPath(new FolderMenuNode("客户资料", @"E\GongSi\客户资料", []), ArchiveScope.ThisPcRoot));
        Assert.Equal(@"E:\GongSi\客户资料", RadialNameTip.DisplayPath(new FolderMenuNode("客户资料", "客户资料", []), @"E:\GongSi"));
        Assert.Equal("此电脑", RadialNameTip.DisplayPath(null, ArchiveScope.ThisPcRoot));
    }

    [Theory]
    [InlineData(-135, 360)]
    [InlineData(0, 180)]
    [InlineData(0, 90)]
    [InlineData(90, 90)]
    [InlineData(180, 90)]
    [InlineData(270, 90)]
    public void EntryArcMatchesVisibleCellForEveryFolderCount(double start, double sweep) => OnSta(() =>
    {
        var plan = RadialLayout.Create(new RadialPoint(960, 540), new RadialRect(0, 0, 1920, 1080));
        plan = plan with { Rings = plan.Rings.Select(ring => ring with { StartAngle = start, SweepAngle = sweep }).ToArray() };
        foreach (var count in new[] { 1, 2, 3, 4 })
        for (var slot = 0; slot < count; slot++)
        {
            var hit = new RadialHit(2, slot, false);
            var arc = RadialInteractionPolicy.EntryArc(plan, hit, count);
            var point = RadialGeometry.Polar(plan.LocalCenter, arc.Radius, (arc.Start + arc.End) / 2);
            var local = new RadialPoint(point.X, point.Y);
            Assert.Equal(hit, RadialLayout.HitTest(plan, local, 4, 4, count));
            Assert.True(RadialInteractionPolicy.IsEntryArc(plan, hit, local, count));
            var gap = RadialLayout.CellGap(plan.Rings[2], count);
            var cellStart = RadialLayout.SlotStart(plan.Rings[2], count) + slot * sweep / count;
            var shape = RadialGeometry.Wedge(plan.LocalCenter, 198, 258, cellStart + gap, cellStart + sweep / count - gap);
            Assert.True(shape.FillContains(point));
        }
    });

    [Fact]
    public void BriefExitOutsideTheWheelAlsoAllowsReentry() => OnSta(() =>
    {
        using var fixture = new MenuFixture();
        var (control, session, body, arc) = fixture;
        control.ProcessPointer(body);
        control.ProcessPointer(arc);
        Pump(UiMotion.RingOpen + 190);
        control.ProcessPointer(RadialGeometry.Polar(fixture.Layout.LocalCenter, 275, -116));
        control.ProcessPointer(arc);
        Assert.True(control.EntryPending);
        Pump(160);
        Assert.Equal(4, session.Path.Count);
        Pump(600);
        Assert.Equal(4, session.Path.Count);
    });

    [Fact]
    public void FullNameTipWaitsForReadingAndDismissesOnNavigation() => OnSta(() =>
    {
        using var fixture = new MenuFixture();
        var requested = 0;
        var dismissed = 0;
        fixture.Control.NameTipRequested += (_, _) => requested++;
        fixture.Control.NameTipDismissRequested += () => dismissed++;
        fixture.Control.ProcessPointer(new Point(fixture.Layout.LocalCenter.X, fixture.Layout.LocalCenter.Y));
        Pump(150);
        Assert.Equal(0, requested);
        Pump(480);
        Assert.Equal(1, requested);
        var before = dismissed;
        fixture.Control.ProcessPointer(RadialGeometry.Polar(fixture.Layout.LocalCenter, 228, -90));
        Assert.True(dismissed > before);
        Assert.Equal(2, fixture.Session.Path.Count);
    });

    [Fact]
    public void SlowSubpixelReturnToArcDoesNotRequireALargePointerJump() => OnSta(() =>
    {
        using var fixture = new MenuFixture();
        var (control, session, body, arc) = fixture;
        control.ProcessPointer(body);
        control.ProcessPointer(arc);
        Pump(UiMotion.RingOpen + 190);
        Assert.Equal(3, session.Path.Count);
        for (var radius = 255.75; radius >= 243.25; radius -= .25)
            control.ProcessPointer(RadialGeometry.Polar(fixture.Layout.LocalCenter, radius, -116));
        for (var radius = 243.5; radius <= 256; radius += .25)
            control.ProcessPointer(RadialGeometry.Polar(fixture.Layout.LocalCenter, radius, -116));
        Assert.True(control.EntryPending);
        Pump(160);
        Assert.Equal(4, session.Path.Count);
    });

    [Fact]
    public void ExitAndReturnDuringTransitionIsRememberedAndRunsOnce() => OnSta(() =>
    {
        using var fixture = new MenuFixture();
        var (control, session, body, arc) = fixture;
        control.ProcessPointer(body);
        control.ProcessPointer(arc);
        Pump(160);
        Assert.Equal(3, session.Path.Count);
        control.ProcessPointer(RadialGeometry.Polar(fixture.Layout.LocalCenter, 243, -116));
        control.ProcessPointer(arc);
        Assert.True(control.EntryPending);
        Pump(UiMotion.RingOpen + 160);
        Assert.Equal(4, session.Path.Count);
        Pump(600);
        Assert.Equal(4, session.Path.Count);
    });

    [Fact]
    public void ReturningAroundArcEndpointDoesNotRequireMovingTowardTheCentre() => OnSta(() =>
    {
        using var fixture = new MenuFixture();
        var (control, session, body, _) = fixture;
        var nearEnd = RadialGeometry.Polar(fixture.Layout.LocalCenter, 256, -103);
        var besideEnd = RadialGeometry.Polar(fixture.Layout.LocalCenter, 256, -99);
        control.ProcessPointer(body);
        control.ProcessPointer(nearEnd);
        Pump(UiMotion.RingOpen + 190);
        Assert.Equal(3, session.Path.Count);
        control.ProcessPointer(besideEnd);
        control.ProcessPointer(nearEnd);
        Assert.True(control.EntryPending);
        Pump(160);
        Assert.Equal(4, session.Path.Count);
    });
    [Fact]
    public void TransparentSpaceDoesNotCaptureDesktopClicks() => OnSta(() =>
    {
        using var fixture = new MenuFixture();
        var bounds = fixture.Layout.WindowBounds;
        Assert.False(fixture.Control.HasMenuHit(new Point(10, -1)));
        Assert.False(fixture.Control.HasMenuHit(new Point(10, bounds.Height + 1)));
        Assert.False(fixture.Control.HasMenuHit(new Point(-1, 10)));
        Assert.False(fixture.Control.HasMenuHit(new Point(bounds.Width + 1, 10)));
        Assert.False(fixture.Control.HasMenuHit(new Point(20, 15)));
        Assert.False(fixture.Control.HasMenuHit(new Point(bounds.Width - 5, bounds.Height / 2)));
        Assert.True(fixture.Control.HasMenuHit(new Point(fixture.Layout.LocalCenter.X, fixture.Layout.LocalCenter.Y)));
    });
    [Fact]
    public void DelayedChildrenAnimateOnlyAfterAnExplicitEntry() => OnSta(() =>
    {
        var pending = new TaskCompletionSource<IReadOnlyList<FolderMenuNode>>(TaskCreationOptions.RunContinuationsAsynchronously);
        var children = new AsyncFolderMenuNodeList(() => pending.Task);
        using var fixture = new MenuFixture(children);
        var (control, session, body, arc) = fixture;
        control.ProcessPointer(body);
        Pump(550);
        Assert.Equal(2, session.Path.Count);
        control.ProcessPointer(arc);
        Pump(180);
        Assert.Equal(3, session.Path.Count);
        pending.SetResult([new FolderMenuNode("第四级", @"1\2\3\4", [])]);
        Pump(100);
        Assert.Equal("第四级", session.GetRingView(2).Slots[0]!.DisplayName);
        if (SystemParameters.ClientAreaAnimation) Assert.True(control.IsTransitioning);
        Pump(UiMotion.RingOpen + 40);
        Assert.False(control.IsTransitioning);
        Assert.True(control.EntryLocked);
    });

    [Fact]
    public void OpeningProducesDifferentNativeFramesAndThenSettles() => OnSta(() =>
    {
        using var fixture = new MenuFixture();
        fixture.Control.PlayOpen();
        Pump(35);
        var early = Frame(fixture.Control);
        Pump(500);
        var settled = Frame(fixture.Control);
        Pump(100);
        Assert.Equal(settled, Frame(fixture.Control));
        if (SystemParameters.ClientAreaAnimation) Assert.NotEqual(early, settled);
    });

    [Fact]
    public void FirstAndDeeperMenusAdvanceAtTheSamePace() => OnSta(() =>
    {
        if (!SystemParameters.ClientAreaAnimation) return;
        using var first = new MenuFixture();
        using var deeper = new MenuFixture();
        var previous = Enumerable.Range(0, 3).Select(deeper.Session.GetRingView).ToArray();
        Assert.True(deeper.Session.Hover(2, 0));
        first.Control.PlayOpen();
        typeof(RadialMenuControl).GetMethod("StartRingTransition",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .Invoke(deeper.Control, [previous, 1, -1, true]);
        Pump(UiMotion.RingOpen / 2);
        double Progress(RadialMenuControl control, string property) =>
            (double)typeof(RadialMenuControl).GetProperty(property,
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(control)!;
        var firstProgress = Progress(first.Control, "OpenProgress");
        var deeperProgress = Progress(deeper.Control, "RingTransitionProgress");
        Assert.InRange(firstProgress, .3, .75);
        Assert.InRange(Math.Abs(firstProgress - deeperProgress), 0, .045);
        Pump(UiMotion.RingOpen);
        Assert.False(deeper.Control.IsTransitioning);
    });

    [Theory]
    [InlineData(1, false)]
    [InlineData(2, false)]
    [InlineData(2, true)]
    public void ChildRingSurfaceAndTextExpandOutwardTogether(int ringIndex, bool replaceContents) => OnSta(() =>
    {
        if (!SystemParameters.ClientAreaAnimation) return;
        using var fixture = new MenuFixture();
        while (fixture.Session.Path.Count > (replaceContents ? 2 : ringIndex - 1))
            fixture.Session.NavigateBack();
        fixture.Control.Configure(fixture.Layout, fixture.Session);
        var previous = Enumerable.Range(0, 3).Select(fixture.Session.GetRingView).ToArray();
        Assert.True(fixture.Session.Hover(replaceContents ? 2 : ringIndex - 1, 0));
        typeof(RadialMenuControl).GetMethod("StartRingTransition",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .Invoke(fixture.Control, [previous, 1, -1, true]);
        Assert.True(fixture.Control.IsTransitioning);

        var label = fixture.Session.GetRingView(ringIndex).Slots[0]!.DisplayName;
        var early = ExpansionFrame(fixture, ringIndex, label, .12);
        var middle = ExpansionFrame(fixture, ringIndex, label, .25);
        var settled = ExpansionFrame(fixture, ringIndex, label, 1);
        Assert.True(early.SurfaceWidth < middle.SurfaceWidth, "The new ring surface must expand instead of flashing at its final size.");
        Assert.True(middle.SurfaceWidth < settled.SurfaceWidth);
        Assert.True(early.LabelRadius < middle.LabelRadius, "The new name must start nearer the center and move outward.");
        Assert.True(middle.LabelRadius < settled.LabelRadius);
        Assert.Equal(early.SurfaceWidth / settled.SurfaceWidth, early.LabelRadius / settled.LabelRadius, 5);
        Assert.Equal(early.ParentLabel, middle.ParentLabel);
        Assert.Equal(early.ParentLabel, settled.ParentLabel);

        // Optional native frames for reviewing the actual renderer, including the
        // opening boundary and a settled hold. Normal test runs create no files.
        if (Environment.GetEnvironmentVariable("LINGRING_MOTION_CAPTURE_ROOT") is { Length: > 0 } captureRoot)
        {
            var output = System.IO.Path.Combine(captureRoot, replaceContents ? "deeper" : $"level-{ringIndex + 1}");
            System.IO.Directory.CreateDirectory(output);
            var transitionFrames = (int)Math.Ceiling(UiMotion.RingOpen / 20d);
            for (var index = 0; index < transitionFrames + 43; index++)
            {
                var frame = RenderExpansionFrame(fixture, Math.Clamp((index - 15) / (double)transitionFrames, 0, 1));
                var bitmap = new RenderTargetBitmap((int)fixture.Control.ActualWidth, (int)fixture.Control.ActualHeight,
                    96, 96, PixelFormats.Pbgra32);
                bitmap.Render(frame);
                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(bitmap));
                using var stream = System.IO.File.Create(System.IO.Path.Combine(output, $"{index:000}.png"));
                encoder.Save(stream);
            }
        }
    });

    private static DrawingVisual RenderExpansionFrame(MenuFixture fixture, double progress)
    {
        var property = (DependencyProperty)typeof(RadialMenuControl).GetField("RingTransitionProgressProperty",
            System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)!.GetValue(null)!;
        fixture.Control.BeginAnimation(property, null);
        fixture.Control.SetValue(property, progress);
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
            typeof(RadialMenuControl).GetMethod("OnRender",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.Invoke(fixture.Control, [dc]);
        return visual;
    }

    private static (double SurfaceWidth, double LabelRadius, Rect ParentLabel) ExpansionFrame(
        MenuFixture fixture, int ringIndex, string label, double progress)
    {
        var visual = RenderExpansionFrame(fixture, progress);
        double surfaceWidth = 0, labelRadius = 0;
        var parentLabel = Rect.Empty;
        var center = fixture.Layout.LocalCenter;
        void Visit(Drawing drawing, Matrix transform, bool clipped)
        {
            if (drawing is DrawingGroup group)
            {
                var matrix = (group.Transform?.Value ?? Matrix.Identity) * transform;
                foreach (var child in group.Children) Visit(child, matrix, clipped || group.ClipGeometry is not null);
            }
            else if (drawing is GeometryDrawing surface &&
                     Math.Abs(surface.Geometry.Bounds.Width - fixture.Layout.Rings[ringIndex].OuterRadius * 2) < .01)
            {
                var bounds = surface.Geometry.Bounds;
                bounds.Transform(transform);
                surfaceWidth = bounds.Width;
            }
            else if (drawing is GlyphRunDrawing glyph && clipped)
            {
                var text = new string(glyph.GlyphRun.Characters.ToArray());
                var bounds = glyph.Bounds;
                bounds.Transform(transform);
                if (text == "1") parentLabel = bounds;
                if (text == label)
                    labelRadius = Math.Sqrt(Math.Pow(bounds.Left + bounds.Width / 2 - center.X, 2) +
                                            Math.Pow(bounds.Top + bounds.Height / 2 - center.Y, 2));
            }
        }
        Visit(visual.Drawing, Matrix.Identity, false);
        Assert.True(surfaceWidth > 0);
        Assert.True(labelRadius > 0);
        Assert.False(parentLabel.IsEmpty);
        return (surfaceWidth, labelRadius, parentLabel);
    }

    [Fact]
    public void RapidHoverKeepsFolderGlyphsAndHitTargetsStable() => OnSta(() =>
    {
        var roots = Enumerable.Range(1, 4).Select(i => new FolderMenuNode($"目录{i}", $"folder{i}", [])).ToArray();
        using var fixture = new MenuFixture(roots: roots);
        List<Rect> FolderBounds()
        {
            var bounds = new List<Rect>();
            void Visit(Drawing drawing, Matrix transform, bool clipped)
            {
                if (drawing is DrawingGroup group)
                {
                    var matrix = (group.Transform?.Value ?? Matrix.Identity) * transform;
                    foreach (var child in group.Children) Visit(child, matrix, clipped || group.ClipGeometry is not null);
                }
                else if (drawing is GlyphRunDrawing glyph && clipped)
                {
                    var rect = glyph.Bounds;
                    rect.Transform(transform);
                    bounds.Add(rect);
                }
            }
            Visit(VisualTreeHelper.GetDrawing(fixture.Control), Matrix.Identity, false);
            return bounds;
        }
        var original = FolderBounds();
        Assert.NotEmpty(original);
        var baseline = Frame(fixture.Control);
        foreach (var angle in new[] { -90d, 0d, 90d, 180d, -90d })
        {
            var point = RadialGeometry.Polar(fixture.Layout.LocalCenter, 88, angle);
            Assert.True(fixture.Control.HasMenuHit(point));
            fixture.Control.ProcessPointer(point);
            Pump(35);
            Assert.Equal(original, FolderBounds());
        }
        Pump(250);
        var settled = Frame(fixture.Control);
        Assert.NotEqual(baseline, settled);
        Pump(80);
        Assert.Equal(original, FolderBounds());
        Assert.Equal(settled, Frame(fixture.Control));
    });

    [Theory]
    [InlineData(960, 540, false)]
    [InlineData(50, 1000, false)]
    [InlineData(960, 540, true)]
    [InlineData(50, 1000, true)]
    public void AsyncChildrenDuringRapidScrollingDoNotDropQueuedRotations(double x, double y, bool cachedLoad) => OnSta(() =>
    {
        if (!SystemParameters.ClientAreaAnimation) return;
        var loads = Enumerable.Range(0, 8).Select(_ => new TaskCompletionSource<IReadOnlyList<FolderMenuNode>>(
            TaskCreationOptions.RunContinuationsAsynchronously)).ToArray();
        var roots = loads.Select((load, index) => new FolderMenuNode($"目录 {index}", $"folder{index}",
            new AsyncFolderMenuNodeList(() => load.Task))).ToArray();
        if (cachedLoad) loads[2].SetResult([new FolderMenuNode("缓存子目录", "folder2/child", [])]);
        using var fixture = new MenuFixture(layout: RadialLayout.Create(new RadialPoint(x, y), new RadialRect(0, 0, 1920, 1080)), roots: roots);
        fixture.Control.HoverDemo(0);
        Assert.True(fixture.Control.RotateDemo(1));
        Assert.True(fixture.Control.RotateDemo(1));
        Assert.True(fixture.Control.RotateDemo(1));
        loads[1].SetResult([new FolderMenuNode("刚读取的子目录", "folder1/child", [])]);
        var finalLoad = new DispatcherTimer(DispatcherPriority.Input) { Interval = TimeSpan.FromMilliseconds(550) };
        finalLoad.Tick += (_, _) =>
        {
            finalLoad.Stop();
            loads[3].SetResult([new FolderMenuNode("最后停留的目录内容", "folder3/child", [])]);
        };
        finalLoad.Start();
        Pump(1400);
        Assert.Equal(3, fixture.Session.GetRingView(0).Offset);
        Assert.Equal("folder3/child", fixture.Session.GetRingView(1).Slots[0]!.RelativePath);
        Assert.False(fixture.Control.IsTransitioning);
    });

    [Theory]
    [InlineData(960, 540)]
    [InlineData(50, 1000)]
    public void RapidScrollingKeepsIntermediateNativeFramesBetweenQueuedSteps(double x, double y) => OnSta(() =>
    {
        if (!SystemParameters.ClientAreaAnimation) return;
        var roots = Enumerable.Range(0, 8).Select(index => new FolderMenuNode($"项目资料_{index}_最终确认版本", $"folder{index}", [])).ToArray();
        using var fixture = new MenuFixture(layout: RadialLayout.Create(new RadialPoint(x, y), new RadialRect(0, 0, 1920, 1080)), roots: roots);
        fixture.Control.HoverDemo(0);
        Pump(250);
        for (var step = 0; step < 3; step++) Assert.True(fixture.Control.RotateDemo(1));
        Pump(40);
        var first = Frame(fixture.Control);
        Pump(90);
        Assert.NotEqual(first, Frame(fixture.Control));
        Pump(160);
        var queued = Frame(fixture.Control);
        Pump(70);
        Assert.NotEqual(queued, Frame(fixture.Control));
        Pump(900);
        Assert.Equal(3, fixture.Session.GetRingView(0).Offset);
        Assert.False(fixture.Control.IsTransitioning);
        var settled = Frame(fixture.Control);
        Pump(80);
        Assert.Equal(settled, Frame(fixture.Control));
    });
    [Theory]
    [InlineData(1)]
    [InlineData(-1)]
    public void FullCircleOutgoingNameFadesBeforeTheRotationEnds(int direction) => OnSta(() =>
    {
        var roots = Enumerable.Range(0, 8).Select(index => new FolderMenuNode("WW", $"folder{index}", [])).ToArray();
        using var fixture = new MenuFixture(roots: roots);
        fixture.Control.HoverDemo(0);
        var previous = fixture.Session.GetRingView(0);
        Assert.True(fixture.Control.RotateDemo(direction));
        var current = fixture.Session.GetRingView(0);
        var outgoingSlot = direction > 0 ? 0 : 3;
        var alternateSlots = previous.Slots.ToArray();
        alternateSlots[outgoingSlot] = new FolderMenuNode("II", previous.Slots[outgoingSlot]!.RelativePath, []);
        var alternate = previous with { Slots = alternateSlots };
        var draw = typeof(RadialMenuControl).GetMethod("DrawWheelRingTransition",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
        byte[] Render(RadialRingView before, double progress)
        {
            var visual = new DrawingVisual();
            using (var dc = visual.RenderOpen())
                draw.Invoke(fixture.Control, [dc, fixture.Layout, 0, before, current, progress]);
            var bitmap = new RenderTargetBitmap((int)fixture.Control.ActualWidth, (int)fixture.Control.ActualHeight,
                96, 96, PixelFormats.Pbgra32);
            bitmap.Render(visual);
            var pixels = new byte[bitmap.PixelWidth * bitmap.PixelHeight * 4];
            bitmap.CopyPixels(pixels, bitmap.PixelWidth * 4, 0);
            return pixels;
        }
        long OutgoingNameDifference(double progress)
        {
            var a = Render(previous, progress); var b = Render(alternate, progress);
            return a.Zip(b, (x, y) => (long)Math.Abs(x - y)).Sum();
        }
        var start = OutgoingNameDifference(0);
        var middle = OutgoingNameDifference(.5);
        var late = OutgoingNameDifference(.95);
        Assert.True(start > 10000, $"The outgoing name must be visible initially: {start}");
        Assert.True(middle > late, $"Outgoing glyph influence must decrease: {middle} -> {late}");
        Assert.True(late < start * .05, $"The new name must be revealed before the final frame: {start} -> {late}");
    });

    [Fact]
    public void Intent_RequiresSameFolderBodyAndCurrentGeneration()
    {
        var intent = new RadialEntryIntent();
        Assert.False(intent.VisitArc("a", 0));
        intent.VisitBody("a", 3);
        Assert.False(intent.VisitArc("b", 3));
        Assert.False(intent.VisitArc("a", 4));
        Assert.True(intent.VisitArc("A", 3));
        Assert.False(intent.TryEnter("a", 4));
        Assert.True(intent.TryEnter("a", 3));
        Assert.True(intent.IsLocked);
        Assert.False(intent.VisitArc("a", 3));
        intent.VisitBody("b", 4);
        Assert.True(intent.VisitArc("b", 4));
        intent.Cancel(true);
        Assert.False(intent.TryEnter("b", 4));
    }

    [Theory]
    [InlineData(960, 540)]
    [InlineData(50, 1000)]
    [InlineData(1870, 80)]
    public void ThirdRing_ReadCrossEnterAndStationaryPointerHaveDistinctOutcomes(double x, double y) => OnSta(() =>
    {
        using var fixture = new MenuFixture(layout: RadialLayout.Create(new RadialPoint(x, y), new RadialRect(0, 0, 1920, 1080)));
        var (control, session, body, arc) = fixture;
        var entry = RadialInteractionPolicy.EntryArc(fixture.Layout, new RadialHit(2, 0, false), 1);
        var entryAngle = (entry.Start + entry.End) / 2;
        body = RadialGeometry.Polar(fixture.Layout.LocalCenter, 228, entryAngle);
        arc = RadialGeometry.Polar(fixture.Layout.LocalCenter, entry.Radius, entryAngle);
        control.ProcessPointer(arc);
        Pump(150);
        Assert.Equal(2, session.Path.Count); // direct arrival cannot enter
        control.ProcessPointer(body);
        Pump(1000);
        Assert.Equal(2, session.Path.Count); // reading never drills
        control.ProcessPointer(arc);
        Assert.True(control.EntryPending);
        var leaveArc = new DispatcherTimer(DispatcherPriority.Input) { Interval = TimeSpan.FromMilliseconds(15) };
        leaveArc.Tick += (_, _) => { leaveArc.Stop(); control.ProcessPointer(body); };
        leaveArc.Start();
        Pump(160);
        Assert.Equal(2, session.Path.Count); // quick crossing cancelled
        control.ProcessPointer(arc);
        Pump(180);
        Assert.Equal(3, session.Path.Count);
        Pump(UiMotion.RingOpen + 70);
        Assert.False(control.IsTransitioning);
        control.ProcessPointer(arc);
        Pump(180);
        Assert.Equal(3, session.Path.Count); // new directory needs a new body visit
        Assert.True(control.EntryLocked);
        control.ProcessPointer(body);
        control.ProcessPointer(arc);
        Pump(180);
        Assert.Equal(4, session.Path.Count);
        control.ReleaseSession();
    });

    [Fact]
    public void BlueArcCannotMoveOrCreateFilesButBodyCan() => OnSta(() =>
    {
        using var fixture = new MenuFixture();
        var (control, session, body, arc) = fixture;
        var moves = new List<string>();
        var creates = new List<string>();
        control.Confirmed += moves.Add;
        control.CreateFolderRequested += creates.Add;
        control.ProcessPointer(body);
        control.ProcessPrimaryClick(arc);
        control.ProcessPrimaryClick(arc, true);
        control.ProcessPrimaryClick(new Point(20, 15));
        Assert.Empty(moves);
        Assert.Empty(creates);
        control.ProcessPrimaryClick(body);
        control.ProcessPrimaryClick(body, true);
        Assert.Equal(@"1\2\3", Assert.Single(moves));
        Assert.Equal(@"1\2\3", Assert.Single(creates));
        Assert.Equal(2, session.Path.Count);
    });

    [Fact]
    public void PendingEntryIsCancelledByDragRefreshAndRelease() => OnSta(() =>
    {
        using var fixture = new MenuFixture();
        var (control, session, body, arc) = fixture;
        control.ProcessPointer(body);
        control.ProcessPointer(arc);
        control.ProcessPointer(arc, buttonDown: true);
        Pump(160);
        Assert.Equal(2, session.Path.Count);
        control.ProcessPointer(body);
        control.ProcessPointer(arc);
        control.Configure(fixture.Layout, session);
        Pump(160);
        Assert.Equal(2, session.Path.Count);
        control.ProcessPointer(body);
        control.ProcessPointer(arc);
        control.ReleaseSession();
        Pump(160);
        Assert.Equal(2, session.Path.Count);
    });

    [Theory]
    [InlineData(1920, 1080, 0, 0)]
    [InlineData(1920, 1080, 1919, 1079)]
    [InlineData(1366, 728, 5, 720)]
    [InlineData(683, 364, 680, 360)]
    [InlineData(500, 700, 1, 1)]
    [InlineData(400, 400, 399, 399)]
    public void Placement_StaysInsideMonitorAndKeepsExactAnchor(double width, double height, double x, double y)
    {
        var area = new RadialRect(-1920, -400, width, height);
        var plan = RadialLayout.Create(new RadialPoint(area.X + x, area.Y + y), area);
        Assert.InRange(plan.WindowBounds.Left, area.Left, area.Right);
        Assert.InRange(plan.WindowBounds.Right, area.Left, area.Right);
        Assert.InRange(plan.WindowBounds.Top, area.Top, area.Bottom);
        Assert.InRange(plan.WindowBounds.Bottom, area.Top, area.Bottom);
        Assert.Equal(new RadialPoint(area.X + x, area.Y + y), plan.ScreenCenter);
        Assert.Equal(plan.ScreenCenter.X, plan.WindowBounds.X + plan.LocalCenter.X, 6);
        Assert.Equal(plan.ScreenCenter.Y, plan.WindowBounds.Y + plan.LocalCenter.Y, 6);
        Assert.Equal(90, plan.SweepAngle);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public void PartialArc_DistributesVisibleSiblingsAcrossTheAvailableFan(int count)
    {
        var plan = RadialLayout.Create(new RadialPoint(12, 18), new RadialRect(0, 0, 683, 364));
        for (var slot = 0; slot < count; slot++)
        {
            var p = RadialGeometry.Polar(plan.LocalCenter, 228, plan.StartAngle + (slot + .5) * plan.SweepAngle / count);
            Assert.Equal(new RadialHit(2, slot, false), RadialLayout.HitTest(plan, new RadialPoint(p.X, p.Y), 4, 2, count));
        }
        Assert.False(RadialInteractionPolicy.RequiresEntryArc(plan, new RadialHit(0, 0, false)));
        Assert.True(RadialInteractionPolicy.RequiresEntryArc(plan, new RadialHit(2, 0, false)));
    }

    [Theory]
    [InlineData(0, 90)]
    [InlineData(90, 90)]
    [InlineData(180, 90)]
    [InlineData(270, 90)]
    [InlineData(0, 180)]
    [InlineData(-135, 360)]
    public void CellContentFitsInsideRoundedSectors(double start, double sweep) => OnSta(() =>
    {
        var center = new RadialPoint(300, 300);
        foreach (var count in new[] { 1, 2, 3, 4 })
        foreach (var inner in new[] { 58d, 128, 198 })
        for (var slot = 0; slot < count; slot++)
        {
            var slotSweep = sweep / count;
            var gap = sweep == 360 && count == 1 ? 0 : 3 / (inner + 30) * 180 / Math.PI;
            var angle = start + slot * slotSweep;
            var shape = RadialGeometry.Wedge(center, inner, inner + 60, angle + gap, angle + slotSweep - gap);
            var content = RadialCellContent.Fit(shape, RadialGeometry.Polar(center, inner + 30, angle + slotSweep / 2), center);
            Assert.False(content.SingleLine.IsEmpty);
            Assert.Equal(IntersectionDetail.FullyContains, shape.FillContainsWithDetail(new RectangleGeometry(content.SingleLine)));
            if (!content.TwoLines.IsEmpty)
                Assert.Equal(IntersectionDetail.FullyContains, shape.FillContainsWithDetail(new RectangleGeometry(content.TwoLines)));
        }
    });

    [Fact]
    public void LongLabels_KeepSuffixAndWholeGraphemeClusters()
    {
        static double Measure(string text) => new StringInfo(text).LengthInTextElements;
        var name = "推广方案👩‍💻资料_é客户最终确认版2026";
        var label = RadialLabelFormatter.TwoLines(name, 7, Measure);
        Assert.StartsWith("推广方案👩‍💻资料\n", label);
        Assert.EndsWith("2026", label);
        Assert.All(label.Split('\n'), line => Assert.True(Measure(line) <= 7));
        Assert.Equal("完整名称", RadialLabelFormatter.TwoLines("完整名称", 7, Measure));
        Assert.Equal("年度\n项目", RadialLabelFormatter.TwoLines("年度项目", 3, Measure));
        Assert.Equal("03 个人\n文件", RadialLabelFormatter.TwoLines("03 个人文件", 5, Measure));
    }

    [Theory]
    [InlineData(58, 118)]
    [InlineData(128, 188)]
    [InlineData(198, 258)]
    public void RoundedSectors_ExcludeTheirSharpCorners(double inner, double outer) => OnSta(() =>
    {
        var center = new RadialPoint(300, 300);
        var wedge = RadialGeometry.Wedge(center, inner, outer, -133, -47);
        Assert.True(wedge.IsFrozen);
        Assert.True(wedge.FillContains(RadialGeometry.Polar(center, (inner + outer) / 2, -90)));
        Assert.False(wedge.FillContains(RadialGeometry.Polar(center, outer - .1, -132.9)));
        Assert.False(wedge.FillContains(RadialGeometry.Polar(center, inner + .1, -132.9)));
    });

    [Fact]
    public void SingleFolderAnnulusHasNoSeamAndKeepsItsHole() => OnSta(() =>
    {
        var center = new RadialPoint(300, 300);
        var shape = RadialGeometry.Wedge(center, 198, 258, -270, 90);
        Assert.False(shape.FillContains(new Point(300, 300)));
        for (var angle = 0; angle < 360; angle++)
        {
            Assert.True(shape.FillContains(RadialGeometry.Polar(center, 228, angle)));
            Assert.False(shape.FillContains(RadialGeometry.Polar(center, 190, angle)));
            Assert.False(shape.FillContains(RadialGeometry.Polar(center, 266, angle)));
        }
    });

    [Theory]
    [InlineData(58)]
    [InlineData(128)]
    [InlineData(198)]
    public void SingleFolderFitsAnUnabridgedCentredName(double inner) => OnSta(() =>
    {
        var center = RadialLayout.Create(new RadialPoint(960, 540), new RadialRect(0, 0, 1920, 1080)).LocalCenter;
        var shape = RadialGeometry.Wedge(center, inner, inner + 60, -270, 90);
        var content = RadialCellContent.Fit(shape, RadialGeometry.Polar(center, inner + 30, -90), center);
        var label = RadialLabelFormatter.Layout("年度项目", content, text => new FormattedText(text,
            CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, RadialTypography.Typeface,
            RadialTypography.FontSize, Brushes.Black, 1.5).WidthIncludingTrailingWhitespace);
        Assert.Equal("年度项目", label.Text);
        Assert.False(label.IsTruncated);
        Assert.Equal(content.Center.Y, label.Bounds.Top + label.Bounds.Height / 2, 6);
    });

    private static byte[] Frame(RadialMenuControl control)
    {
        control.UpdateLayout();
        var bitmap = new RenderTargetBitmap((int)control.ActualWidth, (int)control.ActualHeight,
            96, 96, PixelFormats.Pbgra32);
        bitmap.Render(control);
        var bytes = new byte[bitmap.PixelWidth * bitmap.PixelHeight * 4];
        bitmap.CopyPixels(bytes, bitmap.PixelWidth * 4, 0);
        return System.Security.Cryptography.SHA256.HashData(bytes);
    }

    private static void Pump(int milliseconds)
    {
        var frame = new DispatcherFrame();
        var timer = new DispatcherTimer(DispatcherPriority.Background)
        { Interval = TimeSpan.FromMilliseconds(milliseconds) };
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

    private sealed class MenuFixture : IDisposable
    {
        public readonly RadialLayoutPlan Layout;
        public readonly RadialMenuSession Session;
        public readonly RadialMenuControl Control = new();
        private readonly Window _window;

        public MenuFixture(AsyncFolderMenuNodeList? deferredThirdChildren = null, RadialLayoutPlan? layout = null,
            IReadOnlyList<FolderMenuNode>? roots = null)
        {
            Layout = layout ?? RadialLayout.Create(new RadialPoint(960, 540), new RadialRect(0, 0, 1920, 1080));
            FolderMenuNode Node(int n, string parent = "")
            {
                var path = parent.Length == 0 ? n.ToString() : parent + "\\" + n;
                if (n == 3 && deferredThirdChildren is not null)
                    return new FolderMenuNode(n.ToString(), path, deferredThirdChildren);
                return new FolderMenuNode(n.ToString(), path, n == 7 ? [] : [Node(n + 1, path)]);
            }
            Session = new RadialMenuSession(roots ?? [Node(1)]);
            Session.Hover(0, 0); Session.Hover(1, 0);
            Control.Configure(Layout, Session);
            _window = new Window
            {
                Content = Control,
                Width = Layout.WindowBounds.Width,
                Height = Layout.WindowBounds.Height,
                Left = -32000,
                Top = -32000,
                ShowActivated = false,
                ShowInTaskbar = false,
                WindowStyle = WindowStyle.None
            };
            _window.Show();
            Control.UpdateLayout();
            Pump(30);
        }

        public void Deconstruct(out RadialMenuControl control, out RadialMenuSession session,
            out Point body, out Point arc)
        {
            control = Control; session = Session;
            body = RadialGeometry.Polar(Layout.LocalCenter, 228, -90);
            arc = RadialGeometry.Polar(Layout.LocalCenter, 256, -116);
        }

        public void Dispose() { Control.ReleaseSession(); _window.Close(); }
    }
}
