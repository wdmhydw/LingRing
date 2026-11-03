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

    [Theory]
    [InlineData(-30, 58)]
    [InlineData(30, 58)]
    [InlineData(-150, 58)]
    [InlineData(150, 58)]
    [InlineData(-90, 58)]
    [InlineData(90, 58)]
    [InlineData(0, 58)]
    [InlineData(180, 58)]
    [InlineData(360, 58)]
    [InlineData(-90, 360)]
    public void RegionalReadingKeepsTextOrderAndAvoidsOverlappingGlyphs(double middle, double sweep) => OnSta(() =>
    {
        const string text = "甲乙丙丁戊己庚辛壬癸子丑寅卯辰巳午未申酉戌亥";
        var center = new RadialPoint(420, 420);
        var shape = RadialGeometry.Wedge(center, 220, 302, middle - sweep / 2, middle + sweep / 2);
        var label = RadialArcLabel.Create(text, shape,
            new RadialLabelArc(center, 220, 302, middle - sweep / 2, middle + sweep / 2), 1);
        Assert.False(label.IsTruncated);
        Assert.Equal(RadialLabelDirection.Arc, label.Direction);
        var glyphs = Assert.IsAssignableFrom<IReadOnlyList<ArcLabelGlyph>>(label.ArcGlyphs);
        Assert.Equal(text, string.Concat(glyphs.Select(glyph => glyph.Text)));
        Assert.All(glyphs, glyph => Assert.True(Assert.Single(HeadDirections(glyph.Drawing)).Y < 0));
        var columns = glyphs.GroupBy(glyph => glyph.Column).ToArray();
        var upper = sweep < 180 && Math.Sin(middle * Math.PI / 180) < -.000001;
        if (upper)
        {
            var radii = columns.Select(column => (column.First().Anchor - new System.Windows.Point(center.X, center.Y)).Length).ToArray();
            Assert.Equal(radii.OrderDescending(), radii);
        }
        else Assert.Equal(columns.Select(column => column.First().ColumnX).Order(), columns.Select(column => column.First().ColumnX));
        foreach (var column in columns)
        {
            var letters = column.ToArray();
            var crown = sweep < 180 && Math.Abs(Math.Cos(middle * Math.PI / 180)) < .000001;
            for (var i = 1; i < letters.Length; i++) Assert.True(crown
                ? letters[i].Anchor.X > letters[i - 1].Anchor.X
                : upper && Math.Cos(middle * Math.PI / 180) < 0
                    ? letters[i].Anchor.Y < letters[i - 1].Anchor.Y : letters[i].Anchor.Y > letters[i - 1].Anchor.Y);
        }
        for (var i = 0; i < glyphs.Count; i++)
        for (var j = i + 1; j < glyphs.Count; j++)
            Assert.Equal(IntersectionDetail.Empty, glyphs[i].InkBox.FillContainsWithDetail(glyphs[j].InkBox));
    });

    [Theory]
    [InlineData(30)]
    [InlineData(120)]
    [InlineData(150)]
    [InlineData(-210)]
    [InlineData(480)]
    public void LowerEnglishAndNumbersFaceTheCenterAndKeepTheirTextOrder(double middle) => OnSta(() =>
    {
        var center = new RadialPoint(420, 420);
        var label = RadialArcLabel.Create("客户ABC2026资料e\u0301版本", RadialGeometry.Wedge(center, 220, 302, middle - 14, middle + 14),
            new RadialLabelArc(center, 220, 302, middle - 14, middle + 14), 1);
        Assert.False(label.IsTruncated);
        var glyphs = label.ArcGlyphs!;
        foreach (var text in new[] { "ABC2026", "e\u0301" })
        {
            var glyph = Assert.Single(glyphs, glyph => glyph.Text == text);
            var inward = new System.Windows.Point(center.X, center.Y) - glyph.Anchor; inward.Normalize();
            var head = Assert.Single(HeadDirections(glyph.Drawing));
            Assert.Equal(inward.X, head.X, 6); Assert.Equal(inward.Y, head.Y, 6);
        }
        Assert.Contains(glyphs, glyph => glyph.Text == "客" && Assert.Single(HeadDirections(glyph.Drawing)).Y < 0);
        Assert.Equal("客户ABC2026资料e\u0301版本", string.Concat(glyphs.Select(glyph => glyph.Text)));
    });

    [Theory]
    [InlineData(-90)]
    [InlineData(90)]
    public void CrownRowsKeepYearsTogetherWhileIndividualDigitsFollowTheArc(double middle) => OnSta(() =>
    {
        var center = new RadialPoint(420, 420);
        const string text = "项目年度资料2026归档";
        var label = RadialArcLabel.Create(text, RadialGeometry.Wedge(center, 58, 140, middle - 29, middle + 29),
            new RadialLabelArc(center, 58, 140, middle - 29, middle + 29), 1);
        Assert.False(label.IsTruncated);
        var glyphs = label.ArcGlyphs!;
        Assert.Equal(text, string.Concat(glyphs.Select(glyph => glyph.Text)));
        var digits = glyphs.Where(glyph => char.IsDigit(glyph.Text[0])).ToArray();
        Assert.Equal("2026", string.Concat(digits.Select(glyph => glyph.Text)));
        Assert.Single(digits.Select(glyph => glyph.Column).Distinct());
        Assert.True(digits.Select(glyph => glyph.Rotation).Distinct().Count() > 1);
        Assert.All(HeadDirections(label.Drawing), head => Assert.True(head.Y < 0));
    });

    [Theory]
    [InlineData("上字底接下字头", -90)]
    [InlineData("上字底接下字头", -45)]
    [InlineData("上字底接下字头", 45)]
    [InlineData("上字底接下字头", 90)]
    [InlineData("ABC2026", -90)]
    [InlineData("ABC2026", -45)]
    [InlineData("ABC2026", 45)]
    [InlineData("ABC2026", 90)]
    public void ActualDrawingPreservesHeadToBottomConnectionsDuringWheelRotation(string text, double offset) => OnSta(() =>
    {
        var center = new RadialPoint(420, 420);
        var shape = RadialGeometry.Wedge(center, 220, 302, 1, 59);
        var label = RadialArcLabel.Create(text, shape, new RadialLabelArc(center, 220, 302, 1, 59), 1);
        Assert.False(label.IsTruncated);
        var frame = new DrawingGroup();
        var rotation = new RotateTransform(offset, center.X, center.Y);
        using (var dc = frame.Open())
        {
            dc.PushTransform(rotation);
            dc.DrawDrawing(label.Drawing);
            dc.Pop();
        }
        var original = HeadDirections(label.Drawing);
        var directions = HeadDirections(frame);
        Assert.NotEmpty(directions);
        Assert.Equal(original.Count, directions.Count);
        for (var i = 0; i < directions.Count; i++)
        {
            var expected = rotation.Value.Transform(original[i]);
            Assert.Equal(expected.X, directions[i].X, 6); Assert.Equal(expected.Y, directions[i].Y, 6);
        }
        if (text != "ABC2026")
            for (var i = 1; i < label.ArcGlyphs!.Count; i++)
            {
                var bottom = rotation.Transform(label.ArcGlyphs[i - 1].Anchor) - directions[i - 1] * (RadialTypography.FontSize / 2);
                var top = rotation.Transform(label.ArcGlyphs[i].Anchor) + directions[i] * (RadialTypography.FontSize / 2);
                Assert.InRange((top - bottom).Length, .5, 2.5);
            }
        var rotatedShape = shape.Clone(); rotatedShape.Transform = rotation;
        foreach (var glyph in label.ArcGlyphs!)
        {
            var box = new GeometryGroup { Children = { glyph.InkBox }, Transform = rotation };
            Assert.Equal(IntersectionDetail.FullyContains, rotatedShape.FillContainsWithDetail(box));
        }
    });

    [Theory]
    [InlineData(180)]
    [InlineData(150)]
    [InlineData(0)]
    [InlineData(30)]
    public void ConsecutiveChineseCharactersConnectBottomToHeadAlongTheArc(double middle) => OnSta(() =>
    {
        var center = new RadialPoint(420, 420);
        const string text = "数据明细与分析报告";
        var label = RadialArcLabel.Create(text, RadialGeometry.Wedge(center, 220, 302, middle - 29, middle + 29),
            new RadialLabelArc(center, 220, 302, middle - 29, middle + 29), 1);
        Assert.False(label.IsTruncated);
        var glyphs = label.ArcGlyphs!;
        Assert.Single(glyphs.Select(glyph => glyph.Column).Distinct());
        for (var i = 1; i < glyphs.Count; i++)
        {
            var previous = glyphs[i - 1]; var next = glyphs[i];
            // Inspect the rendered glyph transform, rather than trusting the
            // layout's rotation metadata. Sideways staircase text fails this.
            var previousHead = Assert.Single(HeadDirections(previous.Drawing));
            var nextHead = Assert.Single(HeadDirections(next.Drawing));
            var bottom = previous.Anchor - previousHead * (RadialTypography.FontSize / 2);
            var top = next.Anchor + nextHead * (RadialTypography.FontSize / 2);
            Assert.InRange((top - bottom).Length, .5, 2.5);
            var advance = next.Anchor - previous.Anchor; advance.Normalize();
            var reading = -(previousHead + nextHead); reading.Normalize();
            Assert.True(Vector.Multiply(advance, reading) > .999);
            Assert.True(previousHead.Y < 0 && nextHead.Y < 0);
        }
    });

    [Theory]
    [InlineData(-150)]
    [InlineData(150)]
    [InlineData(-30)]
    [InlineData(30)]
    public void TightInnerArcsKeepAnEightCharacterNameWithoutCollidingCorners(double middle) => OnSta(() =>
    {
        var center = new RadialPoint(420, 420);
        const string text = "品牌视觉设计规范";
        var shape = RadialGeometry.Wedge(center, 58, 140, middle - 29, middle + 29);
        var label = RadialArcLabel.Create(text, shape, new RadialLabelArc(center, 58, 140, middle - 29, middle + 29), 1);
        Assert.False(label.IsTruncated);
        Assert.Equal(text, string.Concat(label.ArcGlyphs!.Select(glyph => glyph.Text)));
        var boxes = label.InkBoxes!;
        Assert.All(boxes, box => Assert.Equal(IntersectionDetail.FullyContains, shape.FillContainsWithDetail(box)));
        for (var i = 0; i < boxes.Count; i++)
        for (var j = i + 1; j < boxes.Count; j++)
            Assert.Equal(IntersectionDetail.Empty, boxes[i].FillContainsWithDetail(boxes[j]));
    });

    [Theory]
    [InlineData(-30, true)]
    [InlineData(-150, false)]
    [InlineData(330, true)]
    [InlineData(210, false)]
    public void UpperArcRowsReadInTheirRequestedDirectionFromOutsideIn(double middle, bool downward) => OnSta(() =>
    {
        var center = new RadialPoint(420, 420);
        const string text = "甲乙丙丁戊己庚辛壬癸子丑寅卯辰巳午未申酉戌亥天地玄黄宇宙洪荒";
        var shape = RadialGeometry.Wedge(center, 220, 302, middle - 29, middle + 29);
        var label = RadialArcLabel.Create(text, shape, new RadialLabelArc(center, 220, 302, middle - 29, middle + 29), 1);
        Assert.False(label.IsTruncated);
        var glyphs = label.ArcGlyphs!;
        Assert.Equal(text, string.Concat(glyphs.Select(glyph => glyph.Text)));
        var rows = glyphs.GroupBy(glyph => glyph.Column).Select(row => row.ToArray()).ToArray();
        Assert.True(rows.Length >= 2);
        double Radius(ArcLabelGlyph glyph) => (glyph.Anchor - new System.Windows.Point(center.X, center.Y)).Length;
        for (var row = 0; row < rows.Length; row++)
        {
            if (row > 0) Assert.True(Radius(rows[row - 1][0]) > Radius(rows[row][0]));
            for (var i = 0; i < rows[row].Length; i++)
            {
                var glyph = rows[row][i];
                var outward = glyph.Anchor - new System.Windows.Point(center.X, center.Y); outward.Normalize();
                var head = Assert.Single(HeadDirections(glyph.Drawing));
                Assert.True(Vector.Multiply(outward, head) > .999);
                if (i > 0) Assert.True(downward
                    ? glyph.Anchor.Y > rows[row][i - 1].Anchor.Y
                    : glyph.Anchor.Y < rows[row][i - 1].Anchor.Y);
                Assert.Equal(IntersectionDetail.FullyContains, shape.FillContainsWithDetail(glyph.InkBox));
            }
        }
    });

    [Theory]
    [InlineData(-90, "AI_Design_2026_FinalV2")]
    [InlineData(90, "AI_Design_2026_FinalV2")]
    [InlineData(270, "AI_Design_2026_FinalV2")]
    [InlineData(450, "AI_Design_2026_FinalV2")]
    [InlineData(-90, "市场推广方案与产品介绍_正式发布")]
    [InlineData(90, "市场推广方案与产品介绍_正式发布")]
    public void TopAndBottomReadLeftToRightAlongConcentricArcs(double middle, string text) => OnSta(() =>
    {
        var center = new RadialPoint(420, 420);
        var shape = RadialGeometry.Wedge(center, 220, 302, middle - 29, middle + 29);
        var label = RadialArcLabel.Create(text, shape, new RadialLabelArc(center, 220, 302, middle - 29, middle + 29), 1);
        Assert.Equal(RadialLabelDirection.Arc, label.Direction);
        Assert.False(label.IsTruncated);
        Assert.Equal(RadialExperimentalLabel.TextElements(text).Count, label.VisibleCharacters);
        var glyphs = Assert.IsAssignableFrom<IReadOnlyList<ArcLabelGlyph>>(label.ArcGlyphs);
        Assert.Equal(text, string.Concat(glyphs.Select(glyph => glyph.Text)));
        Assert.Equal(RadialExperimentalLabel.TextElements(text).Count, glyphs.Count);
        foreach (var row in glyphs.GroupBy(glyph => glyph.Column))
        {
            var letters = row.ToArray();
            double Radius(ArcLabelGlyph glyph) => (glyph.Anchor - new System.Windows.Point(center.X, center.Y)).Length;
            Assert.All(letters, glyph => Assert.Equal(Radius(letters[0]), Radius(glyph), 6));
            for (var i = 0; i < letters.Length; i++)
            {
                if (i > 0) Assert.True(letters[i].Anchor.X > letters[i - 1].Anchor.X);
                var radial = letters[i].Anchor - new System.Windows.Point(center.X, center.Y); radial.Normalize();
                var head = Assert.Single(HeadDirections(letters[i].Drawing));
                Assert.True(head.Y < 0);
                Assert.True(Vector.Multiply(head, Math.Sin(middle * Math.PI / 180) < 0 ? radial : -radial) > .999);
            }
            // The baselines really bow with the ring instead of merely
            // rotating a flat line of text at its center.
            Assert.True(letters.Max(glyph => glyph.Anchor.Y) - letters.Min(glyph => glyph.Anchor.Y) > 3);
        }
        Assert.True(label.Bounds.Width > 100);
        Assert.All(label.InkBoxes!, box => Assert.Equal(IntersectionDetail.FullyContains, shape.FillContainsWithDetail(box)));
    });

    [Theory]
    [InlineData(-90)]
    [InlineData(90)]
    public void WiderTopAndBottomCellsUseWiderLinesForLongNames(double middle) => OnSta(() =>
    {
        var center = new RadialPoint(420, 420);
        const string text = "市场推广方案与产品介绍以及相关宣传材料和正式发布记录";
        ExperimentalLabel At(double inner) => RadialArcLabel.Create(text,
            RadialGeometry.Wedge(center, inner, inner + 82, middle - 29, middle + 29),
            new RadialLabelArc(center, inner, inner + 82, middle - 29, middle + 29), 1);
        var narrow = At(58); var wide = At(220);
        Assert.False(wide.IsTruncated);
        Assert.True(wide.Bounds.Width > narrow.Bounds.Width * 1.5);
        Assert.True(wide.ArcGlyphs!.Select(glyph => glyph.Column).Distinct().Count() <= narrow.ArcGlyphs!.Select(glyph => glyph.Column).Distinct().Count());
        Assert.True(wide.VisibleCharacters >= narrow.VisibleCharacters);
        var rows = wide.ArcGlyphs!.GroupBy(glyph => glyph.Column).ToArray();
        Assert.True(rows.Length >= 2);
        var radii = rows.Select(row => (row.First().Anchor - new System.Windows.Point(center.X, center.Y)).Length).ToArray();
        Assert.Equal(middle < 0 ? radii.OrderDescending() : radii.Order(), radii);
    });

    private static IReadOnlyList<Vector> HeadDirections(Drawing drawing)
    {
        var result = new List<Vector>();
        void Inspect(Drawing node, Matrix parent)
        {
            if (node is DrawingGroup group)
            {
                var matrix = group.Transform?.Value ?? Matrix.Identity; matrix.Append(parent);
                foreach (var child in group.Children) Inspect(child, matrix);
            }
            else if (node is GlyphRunDrawing) result.Add(parent.Transform(new Vector(0, -1)));
        }
        Inspect(drawing, Matrix.Identity);
        return result;
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
