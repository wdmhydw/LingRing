using System.Globalization;
using System.Windows;
using System.Windows.Media;
using Point = System.Windows.Point;

namespace GongSiOrganizer.RadialPrototype.Radial;

internal static class RadialArcLabel
{
    private const double Tracking = .5;
    internal static ExperimentalLabel Create(string name, Geometry shape, RadialLabelArc space, double dpi)
    {
        var glyphs = new Dictionary<string, FormattedText>();
        FormattedText Glyph(string text)
        {
            if (glyphs.TryGetValue(text, out var glyph)) return glyph;
            return glyphs[text] = new FormattedText(text, CultureInfo.CurrentUICulture,
                System.Windows.FlowDirection.LeftToRight, RadialTypography.Typeface, RadialTypography.FontSize,
                new SolidColorBrush(System.Windows.Media.Color.FromRgb(37, 47, 58)), dpi);
        }
        double Measure(string text)
        {
            var parts = RadialExperimentalLabel.TextElements(text);
            return parts.Sum(part => Glyph(part).WidthIncludingTrailingWhitespace) + Math.Max(0, parts.Count - 1) * Tracking;
        }
        var elements = RadialExperimentalLabel.TextElements(name);
        var middle = (space.Start + space.End) / 2;
        // Reverse the path on the lower half, so bottom labels read left to
        // right instead of upside down. Each grapheme follows the local tangent.
        var direction = Math.Sin(middle * Math.PI / 180) > .001 ? -1 : 1;
        var averageRadius = (space.Inner + space.Outer) / 2;
        var maxLines = Math.Clamp((int)((space.Outer - space.Inner - 10) / RadialTypography.LineHeight), 1, 4);
        var empty = new DrawingGroup(); empty.Freeze();
        var best = new ExperimentalLabel(empty, true, 0, RadialLabelDirection.Arc, Rect.Empty);
        for (var lineCount = 1; lineCount <= maxLines; lineCount++)
        {
            var radii = Enumerable.Range(0, lineCount)
                .Select(line => averageRadius + direction * ((lineCount - 1) / 2d - line) * RadialTypography.LineHeight).ToArray();
            // Padding covers rounded ends plus the rotated glyph's ink height.
            var widths = radii.Select(radius => Math.Max(1, radius * Math.Min(110, space.End - space.Start) * Math.PI / 180 - 24)).ToArray();
            for (var fit = 0; fit < 5; fit++)
            {
                var lines = RadialExperimentalLabel.Wrap(elements, widths, Measure);
                var visible = RadialExperimentalLabel.CountVisible(lines);
                if (visible <= best.VisibleCharacters) break;
                var drawing = new DrawingGroup();
                var boxes = new List<Geometry>();
                var contained = true;
                using (var dc = drawing.Open())
                {
                    for (var line = 0; line < lines.Length && contained; line++)
                    {
                        var parts = RadialExperimentalLabel.TextElements(lines[line]);
                        var offset = -Measure(lines[line]) / 2;
                        foreach (var part in parts)
                        {
                            var text = Glyph(part);
                            var advance = text.WidthIncludingTrailingWhitespace;
                            var angle = middle + direction * (offset + advance / 2) / radii[line] * 180 / Math.PI;
                            offset += advance + Tracking;
                            var anchor = RadialGeometry.Polar(space.Center, radii[line], angle);
                            var ink = text.BuildGeometry(new Point()).Bounds;
                            if (ink.IsEmpty) continue;
                            var turn = new RotateTransform(angle + direction * 90, anchor.X, anchor.Y);
                            var box = new RectangleGeometry(new Rect(anchor.X - ink.Width / 2 - .5,
                                anchor.Y - ink.Height / 2 - .5, ink.Width + 1, ink.Height + 1)) { Transform = turn };
                            if (shape.FillContainsWithDetail(box) != IntersectionDetail.FullyContains) { contained = false; break; }
                            box.Freeze(); boxes.Add(box);
                            dc.PushTransform(turn);
                            dc.DrawText(text, new Point(anchor.X - ink.X - ink.Width / 2, anchor.Y - ink.Y - ink.Height / 2));
                            dc.Pop();
                        }
                    }
                }
                if (contained)
                {
                    drawing.Freeze();
                    best = new(drawing, visible < elements.Count, visible, RadialLabelDirection.Arc, drawing.Bounds, boxes);
                    break;
                }
                // A rounded cell may have less space at an inner row. Shorten
                // the text and retry; never draw outside the actual hit shape.
                for (var i = 0; i < widths.Length; i++) widths[i] = Math.Max(1, widths[i] - 10);
            }
            if (!best.IsTruncated) break;
        }
        return best;
    }
}
