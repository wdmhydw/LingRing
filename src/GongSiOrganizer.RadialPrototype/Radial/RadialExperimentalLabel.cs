using System.Globalization;
using System.Windows;
using System.Windows.Media;
using Point = System.Windows.Point;
using Brush = System.Windows.Media.Brush;
using Color = System.Windows.Media.Color;
using FlowDirection = System.Windows.FlowDirection;

namespace GongSiOrganizer.RadialPrototype.Radial;

internal sealed record ExperimentalLabel(DrawingGroup Drawing, bool IsTruncated, int VisibleCharacters,
    RadialLabelDirection Direction, Rect Bounds, IReadOnlyList<Geometry>? InkBoxes = null);

internal readonly record struct RadialLabelArc(RadialPoint Center, double Inner, double Outer, double Start, double End);

internal static class RadialExperimentalLabel
{
    private static readonly Brush Ink = new SolidColorBrush(Color.FromRgb(37, 47, 58));
    static RadialExperimentalLabel() => Ink.Freeze();

    public static ExperimentalLabel Create(string name, Geometry shape, Point center,
        RadialLabelDirection direction, double pixelsPerDip, RadialLabelArc? arc = null)
    {
        var elements = TextElements(name);
        var measurements = new Dictionary<string, double>();
        FormattedText Format(string text) => new(text, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
            RadialTypography.Typeface, RadialTypography.FontSize, Ink, pixelsPerDip);
        double Measure(string text)
        {
            if (!measurements.TryGetValue(text, out var width))
                measurements[text] = width = Format(text).BuildGeometry(new Point()).Bounds.Width;
            return width;
        }
        var best = new Candidate([], Rect.Empty, 0, true);
        if (direction != RadialLabelDirection.Arc)
        {
            for (var lines = 1; lines <= 4; lines++)
            {
                var height = RadialTypography.InkHeight + (lines - 1) * RadialTypography.LineHeight;
                var width = FitWidth(shape, center, height);
                if (width < RadialTypography.FontSize) continue;
                var text = Wrap(elements, lines, width, Measure);
                var visible = CountVisible(text);
                if (visible <= best.Visible) continue;
                var usedHeight = RadialTypography.InkHeight + (text.Length - 1) * RadialTypography.LineHeight;
                best = new(text, new Rect(center.X - width / 2, center.Y - usedHeight / 2, width, usedHeight),
                    visible, visible < elements.Count);
            }
        }
        if (direction != RadialLabelDirection.Horizontal && arc is { } arcSpace)
        {
            var curved = RadialArcLabel.Create(name, shape, arcSpace, pixelsPerDip);
            // Prefer horizontal reading for a tie. Curvature must reveal more
            // of the actual name to justify changing its reading direction.
            if (direction == RadialLabelDirection.Arc || curved.VisibleCharacters > best.Visible) return curved;
        }
        var drawing = new DrawingGroup();
        using (var dc = drawing.Open())
        {
            if (!best.Bounds.IsEmpty)
                for (var i = 0; i < best.Text.Length; i++)
                {
                    var text = Format(best.Text[i]);
                    var ink = text.BuildGeometry(new Point()).Bounds;
                    if (ink.IsEmpty) continue;
                    var x = center.X;
                    var y = best.Bounds.Top + RadialTypography.InkHeight / 2 + i * RadialTypography.LineHeight;
                    dc.DrawText(text, new Point(x - ink.X - ink.Width / 2, y - ink.Y - ink.Height / 2));
                }
        }
        drawing.Freeze();
        return new(drawing, best.Truncated, best.Visible, RadialLabelDirection.Horizontal, best.Bounds);
    }

    private static double FitWidth(Geometry shape, Point center, double height)
    {
        double low = 0, high = 280;
        for (var step = 0; step < 10; step++)
        {
            var extent = (low + high) / 2;
            var box = new Rect(center.X - extent / 2, center.Y - height / 2, extent, height);
            if (shape.FillContainsWithDetail(new RectangleGeometry(box)) == IntersectionDetail.FullyContains) low = extent;
            else high = extent;
        }
        return Math.Max(0, low - 3);
    }

    private static string[] Wrap(IReadOnlyList<string> elements, int lines, double width, Func<string, double> measure) =>
        Wrap(elements, Enumerable.Repeat(width, lines).ToArray(), measure);

    internal static string[] Wrap(IReadOnlyList<string> elements, IReadOnlyList<double> widths, Func<string, double> measure)
    {
        var lines = widths.Count;
        var result = new List<string>();
        var used = 0;
        for (var line = 0; line < lines && used < elements.Count; line++)
        {
            var width = widths[line];
            var current = "";
            if (line == lines - 1 && measure(string.Concat(elements.Skip(used))) > width)
            {
                if (lines == 1)
                {
                    for (var count = elements.Count - 1; count >= 0; count--)
                    {
                        var clipped = string.Concat(KeepEnds(elements, count + 1));
                        if (measure(clipped) <= width) { current = clipped; break; }
                    }
                }
                else
                {
                    current = "…";
                    for (var i = elements.Count - 1; i >= used && measure("…" + elements[i] + current[1..]) <= width; i--)
                        current = "…" + elements[i] + current[1..];
                }
                result.Add(current);
                break;
            }
            while (used < elements.Count && measure(current + elements[used]) <= width) current += elements[used++];
            if (current.Length == 0) break;
            result.Add(current);
        }
        return result.ToArray();
    }

    internal static List<string> TextElements(string text)
    {
        var result = new List<string>();
        var iterator = StringInfo.GetTextElementEnumerator(text);
        while (iterator.MoveNext()) result.Add(iterator.GetTextElement());
        return result;
    }

    private static string[] KeepEnds(IReadOnlyList<string> elements, int capacity)
    {
        if (elements.Count <= capacity) return elements.ToArray();
        if (capacity <= 1) return ["…"];
        var head = (int)Math.Ceiling((capacity - 1) * .6);
        var tail = capacity - head - 1;
        return elements.Take(head).Concat(["…"]).Concat(elements.Skip(elements.Count - tail)).ToArray();
    }

    internal static int CountVisible(IEnumerable<string> text) => TextElements(string.Concat(text).Replace("…", "")).Count;
    private sealed record Candidate(string[] Text, Rect Bounds, int Visible, bool Truncated);
}
