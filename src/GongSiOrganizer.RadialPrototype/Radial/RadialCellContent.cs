using System.Windows;
using System.Windows.Media;
using Point = System.Windows.Point;

namespace GongSiOrganizer.RadialPrototype.Radial;

internal static class RadialTypography
{
    public const double FontSize = 12.5;
    public const double LineHeight = 18;
    public const double InkHeight = 14.5;
    public static readonly Typeface Typeface = new(new System.Windows.Media.FontFamily("Microsoft YaHei UI"),
        FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal);
}

// Geometry only supplies text space. It never selects a font or decoration.
internal readonly record struct RadialCellContent(Point Center, Rect SingleLine, Rect TwoLines)
{
    public static RadialCellContent Fit(Geometry shape, Point center, RadialPoint wheelCenter)
    {
        Rect FitBox(Point anchor, double height)
        {
            double low = 0, high = 160;
            for (var step = 0; step < 12; step++)
            {
                var width = (low + high) / 2;
                var rect = new Rect(anchor.X - width / 2, anchor.Y - height / 2, width, height);
                if (shape.FillContainsWithDetail(new RectangleGeometry(rect)) == IntersectionDetail.FullyContains)
                    low = width;
                else high = width;
            }
            var safeWidth = Math.Max(0, low - 2);
            return safeWidth < RadialTypography.FontSize ? Rect.Empty :
                new Rect(anchor.X - safeWidth / 2, anchor.Y - height / 2, safeWidth, height);
        }
        // Leading is blank space, not ink. Reserving two whole line boxes in
        // a diagonal wedge discarded usable width even for two-character names.
        RadialCellContent At(Point anchor) => new(anchor, FitBox(anchor, RadialTypography.InkHeight),
            FitBox(anchor, RadialTypography.InkHeight + RadialTypography.LineHeight));
        static double Capacity(RadialCellContent content) => Math.Max(
            content.SingleLine.IsEmpty ? 0 : content.SingleLine.Width,
            content.TwoLines.IsEmpty ? 0 : content.TwoLines.Width * 2);
        var best = At(center);
        // Narrow corner cells widen toward the outer edge. Use that space
        // before abbreviating even a two-character name. This anchor depends
        // only on geometry, so changing names never moves or restyles a label.
        var direction = center - new Point(wheelCenter.X, wheelCenter.Y);
        if (direction.Length < 1) return best;
        direction.Normalize();
        for (var offset = 2; offset <= 12 && Capacity(best) < 2 * RadialTypography.FontSize + 2; offset += 2)
        {
            var candidate = At(center + direction * offset);
            if (Capacity(candidate) > Capacity(best)) best = candidate;
        }
        return best;
    }
}

internal readonly record struct RadialCellLabel(string Text, Rect Bounds, bool IsTruncated);
