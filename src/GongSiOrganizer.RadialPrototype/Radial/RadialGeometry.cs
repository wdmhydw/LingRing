using System.Windows.Media;
using Point = System.Windows.Point;
using Size = System.Windows.Size;

namespace GongSiOrganizer.RadialPrototype.Radial;

internal static class RadialGeometry
{
    public static Point Polar(RadialPoint center, double radius, double angle)
    {
        var a = angle * Math.PI / 180;
        return new Point(center.X + radius * Math.Cos(a), center.Y + radius * Math.Sin(a));
    }

    public static StreamGeometry Wedge(RadialPoint center, double inner, double outer,
        double start, double end, double requestedCorner = 11)
    {
        if (end - start >= 359.999)
        {
            // A single directory occupies a closed annulus, with no radial
            // seam, corner fillets or filled centre.
            var annulus = new StreamGeometry { FillRule = FillRule.EvenOdd };
            using (var context = annulus.Open())
                foreach (var radius in new[] { outer, inner })
                {
                    context.BeginFigure(Polar(center, radius, start), true, true);
                    context.ArcTo(Polar(center, radius, start + 180), new Size(radius, radius), 0,
                        false, SweepDirection.Clockwise, true, true);
                    context.ArcTo(Polar(center, radius, start + 360), new Size(radius, radius), 0,
                        false, SweepDirection.Clockwise, true, true);
                }
            annulus.Freeze();
            return annulus;
        }
        var halfSin = Math.Sin((end - start) * Math.PI / 360);
        var corner = Math.Max(.1, Math.Min(requestedCorner, Math.Min((outer - inner) / 2 - 1,
            Math.Min(.9 * inner * halfSin / Math.Max(.001, 1 - halfSin),
                .9 * outer * halfSin / (1 + halfSin)))));
        var oa = Math.Asin(corner / (outer - corner)) * 180 / Math.PI;
        var ia = Math.Asin(corner / (inner + corner)) * 180 / Math.PI;
        var oe = Math.Sqrt(Math.Pow(outer - corner, 2) - corner * corner);
        var ie = Math.Sqrt(Math.Pow(inner + corner, 2) - corner * corner);
        var geometry = new StreamGeometry();
        using (var context = geometry.Open())
        {
            void Arc(double radius, double pointRadius, double angle, bool clockwise = true, bool large = false) =>
                context.ArcTo(Polar(center, pointRadius, angle), new Size(radius, radius), 0,
                    large, clockwise ? SweepDirection.Clockwise : SweepDirection.Counterclockwise, true, true);
            context.BeginFigure(Polar(center, outer, start + oa), true, true);
            Arc(outer, outer, end - oa, large: end - start - 2 * oa > 180);
            Arc(corner, oe, end);
            context.LineTo(Polar(center, ie, end), true, true);
            Arc(corner, inner, end - ia);
            Arc(inner, inner, start + ia, false, end - start - 2 * ia > 180);
            Arc(corner, ie, start);
            context.LineTo(Polar(center, oe, start), true, true);
            Arc(corner, outer, start + oa);
        }
        geometry.Freeze();
        return geometry;
    }

    public static StreamGeometry Arc(RadialPoint center, double radius, double start, double end)
    {
        var geometry = new StreamGeometry();
        using (var context = geometry.Open())
        {
            context.BeginFigure(Polar(center, radius, start), false, false);
            context.ArcTo(Polar(center, radius, end), new Size(radius, radius), 0,
                end - start > 180, SweepDirection.Clockwise, true, true);
        }
        geometry.Freeze();
        return geometry;
    }
}
