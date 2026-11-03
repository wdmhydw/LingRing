using System.Globalization;
using System.Text;
using System.Windows;
using System.Windows.Media;
using Point = System.Windows.Point;

namespace GongSiOrganizer.RadialPrototype.Radial;

internal sealed record ArcLabelGlyph(string Text, int Column, double ColumnX, Point Anchor,
    double Rotation, DrawingGroup Drawing, Geometry InkBox);

internal static class RadialArcLabel
{
    private const double Gap = 1.25;
    private const double ColumnGap = RadialTypography.FontSize + 3.5;
    private const double ToRadians = Math.PI / 180;
    private sealed record Unit(string Text, FormattedText TextLayout, Rect Ink, double Rotation, int Characters)
    {
        // Keep a small orientation-independent margin inside rounded cells.
        public double Radius => Math.Sqrt(Ink.Width * Ink.Width + Ink.Height * Ink.Height) / 2 + 1;
        public double ReadingHeight => Rotation == 0 ? Math.Max(RadialTypography.FontSize, Ink.Height) : Ink.Width;
        public double ReadingWidth => Rotation == 0 ? Ink.Width : Ink.Height;
    }
    private sealed record Column(double Radius, double Top, double Sweep, int Direction, double X, bool HorizontalFlow);
    private sealed record Placement(Unit Unit, int Column, double ColumnX, Point Anchor, double Rotation, Geometry Box);

    internal static ExperimentalLabel Create(string name, Geometry shape, RadialLabelArc space, double dpi)
    {
        var branches = Branches(space.Start, space.End);
        var averageRadius = (space.Inner + space.Outer) / 2;
        var middleAngle = (space.Start + space.End) / 2;
        var localSector = space.End - space.Start < 180;
        if (localSector && branches.Count > 1)
        {
            // Top and bottom are wide, shallow cells. Use their real width for
            // normal horizontal text, including Chinese, Latin and numbers.
            var anchor = RadialGeometry.Polar(space.Center, averageRadius, middleAngle);
            var content = RadialCellContent.Fit(shape, anchor, space.Center);
            return RadialExperimentalLabel.Create(name, shape, content.Center, RadialLabelDirection.Horizontal, dpi);
        }
        // Choose a rule for the complete cell, not per glyph as it nears an
        // axis. The upper sides read clockwise, with character heads outward.
        var upper = localSector && Math.Sin(middleAngle * ToRadians) < -.000001;
        var formats = new Dictionary<string, FormattedText>();
        FormattedText Format(string text)
        {
            if (formats.TryGetValue(text, out var formatted)) return formatted;
            return formats[text] = new FormattedText(text, CultureInfo.CurrentUICulture,
                System.Windows.FlowDirection.LeftToRight, RadialTypography.Typeface, RadialTypography.FontSize,
                new SolidColorBrush(System.Windows.Media.Color.FromRgb(37, 47, 58)), dpi);
        }
        Unit MakeUnit(string text, bool sideways, int characters)
        {
            var formatted = Format(text);
            var ink = formatted.BuildGeometry(new Point()).Bounds;
            if (ink.IsEmpty) ink = new Rect(0, 0, RadialTypography.FontSize * .35, RadialTypography.InkHeight);
            return new(text, formatted, ink, sideways ? -90 : 0, characters);
        }

        var runWidth = Math.Max(RadialTypography.FontSize,
            Math.Min(space.Outer - space.Inner - 14,
                averageRadius * branches.Min(branch => Math.Min(110, branch.End - branch.Start)) * ToRadians - 16));
        var units = new List<Unit>();
        var run = "";
        void FlushRun()
        {
            if (run.Length == 0) return;
            units.Add(MakeUnit(run, !upper, RadialExperimentalLabel.TextElements(run).Count)); run = "";
        }
        foreach (var element in RadialExperimentalLabel.TextElements(name))
        {
            if (IsSideways(element))
            {
                var number = run.Length < 6 && run.All(char.IsDigit) && Rune.IsDigit(Rune.GetRuneAt(element, 0));
                if (run.Length > 0 && !number && Format(run + element).WidthIncludingTrailingWhitespace > runWidth) FlushRun();
                run += element;
            }
            else if (run.Length > 0 && element is "_" or "-" or ".")
            {
                if (Format(run + element).WidthIncludingTrailingWhitespace <= runWidth) run += element;
                else { FlushRun(); units.Add(MakeUnit(element, false, 1)); }
                FlushRun();
            }
            else
            {
                FlushRun(); units.Add(MakeUnit(element, false, 1));
            }
        }
        FlushRun();
        var ellipsis = MakeUnit(upper ? "…" : "⋮", false, 0);
        var maxColumns = Math.Clamp((int)((space.Outer - space.Inner - 10) / ColumnGap), 1, 4);
        List<Placement> best = [];
        var bestCharacters = 0;
        var totalCharacters = units.Sum(unit => unit.Characters);

        for (var trackCount = 1; trackCount <= maxColumns; trackCount++)
        {
            var tracks = Enumerable.Range(0, trackCount).SelectMany(track =>
            {
                var radius = averageRadius + (track - (trackCount - 1) / 2d) * ColumnGap;
                return branches.Select(branch =>
                {
                    var middle = (branch.Start + branch.End) / 2;
                    var sweep = Math.Min(110, branch.End - branch.Start);
                    var direction = upper || Math.Cos(middle * ToRadians) >= 0 ? 1 : -1;
                    return new Column(radius, middle - direction * sweep / 2, sweep, direction,
                        space.Center.X + radius * Math.Cos(middle * ToRadians), upper);
                });
            });
            var columns = (upper ? tracks.OrderByDescending(column => column.Radius) : tracks.OrderBy(column => column.X)).ToArray();

            (List<Placement> Placed, int Consumed) Place(IReadOnlyList<Unit> source) => Pack(source, columns, shape, space.Center);
            var (placed, consumed) = Place(units);
            if (consumed < units.Count)
            {
                // Preserve both the identifying prefix and distinguishing
                // suffix. Break only between complete graphemes/Latin runs.
                placed = [];
                for (var keep = Math.Min(units.Count - 1, consumed); keep >= 0; keep--)
                {
                    var head = (int)Math.Ceiling(keep * .6);
                    var clipped = units.Take(head).Concat([ellipsis]).Concat(units.Skip(units.Count - (keep - head))).ToArray();
                    var attempt = Place(clipped);
                    if (attempt.Consumed != clipped.Length) continue;
                    placed = attempt.Placed; break;
                }
            }
            var characters = placed.Sum(item => item.Unit.Characters);
            if (characters > bestCharacters || best.Count == 0 && placed.Count > 0)
            { best = placed; bestCharacters = characters; }
            if (bestCharacters == totalCharacters) break;
        }

        var drawing = new DrawingGroup();
        var glyphs = new List<ArcLabelGlyph>();
        using (var dc = drawing.Open())
            foreach (var item in best)
            {
                var unit = item.Unit;
                var glyph = new DrawingGroup();
                using (var glyphDc = glyph.Open())
                {
                    glyphDc.PushTransform(new RotateTransform(item.Rotation, item.Anchor.X, item.Anchor.Y));
                    glyphDc.DrawText(unit.TextLayout, new Point(item.Anchor.X - unit.Ink.X - unit.Ink.Width / 2,
                        item.Anchor.Y - unit.Ink.Y - unit.Ink.Height / 2));
                    glyphDc.Pop();
                }
                glyph.Freeze(); dc.DrawDrawing(glyph);
                glyphs.Add(new(unit.Text, item.Column, item.ColumnX, item.Anchor, item.Rotation, glyph, item.Box));
            }
        drawing.Freeze();
        return new(drawing, bestCharacters < totalCharacters, bestCharacters, RadialLabelDirection.Arc,
            drawing.Bounds, glyphs.Select(glyph => glyph.InkBox).ToArray(), glyphs);
    }

    // Each branch is monotonic in screen Y. A column never crosses the top
    // or bottom of a circle and starts reading upward halfway through.
    private static List<(double Start, double End)> Branches(double start, double end)
    {
        var result = new List<(double Start, double End)>();
        var next = start;
        for (var turn = Math.Ceiling((start - 90) / 180); 90 + turn * 180 < end - .001; turn++)
        {
            var cut = 90 + turn * 180;
            if (cut <= next + .001) continue;
            result.Add((next, cut)); next = cut;
        }
        result.Add((next, end));
        return result;
    }

    private static bool IsSideways(string element)
    {
        var rune = Rune.GetRuneAt(element, 0);
        return Rune.IsDigit(rune) || Rune.IsLetter(rune) &&
            (rune.Value <= 0x024f || rune.Value is >= 0x1e00 and <= 0x1eff or >= 0xff21 and <= 0xff5a);
    }

    private static (List<Placement> Placed, int Consumed) Pack(IReadOnlyList<Unit> units,
        IReadOnlyList<Column> columns, Geometry shape, RadialPoint center)
    {
        var placed = new List<Placement>();
        var used = 0;
        for (var columnIndex = 0; columnIndex < columns.Count && used < units.Count; columnIndex++)
        {
            var column = columns[columnIndex];
            var available = column.Sweep * ToRadians - 6 / column.Radius;
            // Reserve the angular span at the glyph's inner edge. Measuring
            // only its centerline lets neighboring corners collide on small
            // radii even though their head and bottom centers are separated.
            double Advance(Unit unit) => 2 * Math.Atan2(((column.HorizontalFlow ? unit.Ink.Width : unit.ReadingHeight) + Gap) / 2,
                Math.Max(1, column.Radius - ((column.HorizontalFlow ? unit.Ink.Height : unit.ReadingWidth) + 1) / 2));
            var count = 0;
            var extent = 0d;
            while (used + count < units.Count && extent + Advance(units[used + count]) <= available)
                extent += Advance(units[used + count++]);
            // Center complete runs along the branch, reducing the number only
            // when rounded ends or another column leave insufficient room.
            while (count > 0)
            {
                var batch = new List<Placement>();
                var cursor = (column.Sweep * ToRadians - extent) / 2;
                for (var index = 0; index < count; index++)
                {
                    var unit = units[used + index];
                    var advance = Advance(unit);
                    var angle = column.Top + column.Direction * (cursor + advance / 2) / ToRadians;
                    cursor += advance;
                    var anchor = RadialGeometry.Polar(center, column.Radius, angle);
                    // Upper rows use their horizontal baseline along the arc;
                    // lower columns keep their vertical head-to-bottom flow.
                    var columnRotation = Math.IEEERemainder(angle + (column.HorizontalFlow ? 90 : column.Direction < 0 ? 180 : 0), 360);
                    var rotation = columnRotation + unit.Rotation;
                    var envelope = new EllipseGeometry(anchor, unit.Radius, unit.Radius);
                    var box = InkBox(unit, anchor, rotation);
                    if (shape.FillContainsWithDetail(envelope) != IntersectionDetail.FullyContains ||
                        placed.Concat(batch).Any(previous => Overlaps(previous.Box, box))) break;
                    batch.Add(new(unit, columnIndex, column.X, anchor, rotation, box));
                }
                if (batch.Count == count) { placed.AddRange(batch); used += count; break; }
                extent -= Advance(units[used + --count]);
            }
        }
        return (placed, used);
    }

    private static Geometry InkBox(Unit unit, Point anchor, double rotation)
    {
        var width = unit.Ink.Width + 1;
        var height = unit.Ink.Height + 1;
        var box = new RectangleGeometry(new Rect(anchor.X - width / 2, anchor.Y - height / 2, width, height))
        {
            Transform = new RotateTransform(rotation, anchor.X, anchor.Y)
        };
        box.Freeze();
        return box;
    }

    private static bool Overlaps(Geometry first, Geometry second) =>
        first.Bounds.IntersectsWith(second.Bounds) && first.FillContainsWithDetail(second) != IntersectionDetail.Empty;
}
