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
    private sealed record Column(double Radius, double Top, double Sweep, int Direction, double X);
    private sealed record Placement(Unit Unit, int Column, double ColumnX, Point Anchor, double Rotation, Geometry Box);

    internal static ExperimentalLabel Create(string name, Geometry shape, RadialLabelArc space, double dpi)
    {
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

        var branches = Branches(space.Start, space.End);
        var averageRadius = (space.Inner + space.Outer) / 2;
        var crown = branches.Count > 1 && space.End - space.Start < 180;
        var runWidth = Math.Max(RadialTypography.FontSize,
            Math.Min(space.Outer - space.Inner - 14,
                crown ? double.PositiveInfinity :
                    averageRadius * branches.Min(branch => Math.Min(110, branch.End - branch.Start)) * ToRadians - 16));
        var units = new List<Unit>();
        var run = "";
        void FlushRun()
        {
            if (run.Length == 0) return;
            units.Add(MakeUnit(run, true, RadialExperimentalLabel.TextElements(run).Count)); run = "";
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
        var ellipsis = MakeUnit("⋮", false, 0);
        var maxColumns = crown
            ? Math.Clamp((int)(Math.Min(shape.Bounds.Width - 16, 2 * averageRadius - 20) / ColumnGap), 1, 16)
            : Math.Clamp((int)((space.Outer - space.Inner - 10) / ColumnGap), 1, 4);
        List<Placement> best = [];
        var bestCharacters = 0;
        var totalCharacters = units.Sum(unit => unit.Characters);

        for (var trackCount = 1; trackCount <= maxColumns; trackCount++)
        {
            var columns = Enumerable.Range(0, trackCount).SelectMany(track =>
            {
                var radius = averageRadius + (track - (trackCount - 1) / 2d) * ColumnGap;
                return branches.Select(branch =>
                {
                    var middle = (branch.Start + branch.End) / 2;
                    var sweep = Math.Min(110, branch.End - branch.Start);
                    var direction = Math.Cos(middle * ToRadians) >= 0 ? 1 : -1;
                    return new Column(radius, middle - direction * sweep / 2, sweep, direction,
                        space.Center.X + radius * Math.Cos(middle * ToRadians));
                });
            }).OrderBy(column => column.X).ToArray();

            (List<Placement> Placed, int Consumed) Place(IReadOnlyList<Unit> source) => crown
                ? PackCrown(source, trackCount, shape, space) : Pack(source, columns, shape, space.Center);
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
            double Advance(Unit unit) => 2 * Math.Atan2((unit.ReadingHeight + Gap) / 2,
                Math.Max(1, column.Radius - (unit.ReadingWidth + 1) / 2));
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
                    // The Chinese vertical axis follows the downward tangent:
                    // each character's bottom meets the next character's head.
                    // Latin runs stay sideways relative to that same axis.
                    var columnRotation = Math.IEEERemainder(angle + (column.Direction < 0 ? 180 : 0), 360);
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

    // At the crown of the ring a concentric arc is almost horizontal. Use
    // short vertical columns whose centers follow that arc, so the reader can
    // still follow top-to-bottom columns in an unambiguous left-to-right order.
    private static (List<Placement> Placed, int Consumed) PackCrown(IReadOnlyList<Unit> units,
        int columnCount, Geometry shape, RadialLabelArc space)
    {
        var radius = (space.Inner + space.Outer) / 2;
        var middleX = (shape.Bounds.Left + shape.Bounds.Right) / 2;
        var side = Math.Sin((space.Start + space.End) / 2 * ToRadians) < 0 ? -1 : 1;
        var available = space.Outer - space.Inner - 10;
        var placed = new List<Placement>();
        var used = 0;
        for (var column = 0; column < columnCount && used < units.Count; column++)
        {
            var x = middleX + (column - (columnCount - 1) / 2d) * ColumnGap;
            var dx = x - space.Center.X;
            if (Math.Abs(dx) >= radius) continue;
            var middleY = space.Center.Y + side * Math.Sqrt(radius * radius - dx * dx);
            var count = 0;
            var extent = 0d;
            double Advance(Unit unit) => unit.ReadingHeight + Gap;
            while (used + count < units.Count && extent + Advance(units[used + count]) <= available)
                extent += Advance(units[used + count++]);
            while (count > 0)
            {
                var batch = new List<Placement>();
                var y = middleY - extent / 2;
                for (var index = 0; index < count; index++)
                {
                    var unit = units[used + index];
                    var advance = Advance(unit);
                    var anchor = new Point(x, y + advance / 2); y += advance;
                    var box = InkBox(unit, anchor, unit.Rotation);
                    if (shape.FillContainsWithDetail(new EllipseGeometry(anchor, unit.Radius, unit.Radius)) != IntersectionDetail.FullyContains ||
                        placed.Concat(batch).Any(previous => Overlaps(previous.Box, box))) break;
                    batch.Add(new(unit, column, x, anchor, unit.Rotation, box));
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
