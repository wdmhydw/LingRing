namespace GongSiOrganizer.RadialPrototype.Radial;

public readonly record struct RadialPoint(double X, double Y)
{
    public static RadialPoint operator -(RadialPoint left, RadialPoint right) =>
        new(left.X - right.X, left.Y - right.Y);
}

public readonly record struct RadialRect(double X, double Y, double Width, double Height)
{
    public double Left => X;
    public double Top => Y;
    public double Right => X + Width;
    public double Bottom => Y + Height;

    public RadialRect Intersect(RadialRect other)
    {
        var left = Math.Max(Left, other.Left);
        var top = Math.Max(Top, other.Top);
        var right = Math.Min(Right, other.Right);
        var bottom = Math.Min(Bottom, other.Bottom);
        return right <= left || bottom <= top
            ? new RadialRect(left, top, 1, 1)
            : new RadialRect(left, top, right - left, bottom - top);
    }
}

public readonly record struct RadialRing(double InnerRadius, double OuterRadius,
    double StartAngle = -135, double SweepAngle = 360);

public readonly record struct RadialHit(int RingIndex, int SlotIndex, bool IsCenter)
{
    public static RadialHit None => new(-1, -1, false);
    public static RadialHit Center => new(-1, -1, true);
    public bool HasValue => IsCenter || RingIndex >= 0;
}

public sealed record RadialLayoutPlan(
    RadialPoint ScreenCenter,
    RadialRect WindowBounds,
    RadialPoint LocalCenter,
    double CenterRadius,
    IReadOnlyList<RadialRing> Rings,
    bool IsInvalidTarget)
{
    public double OuterRadius => Rings.Count == 0 ? CenterRadius : Rings[^1].OuterRadius;
    public double StartAngle => Rings.Count == 0 ? 0 : Rings[^1].StartAngle;
    public double SweepAngle => Rings.Count == 0 ? 0 : Rings[^1].SweepAngle;
}

public static class RadialLayout
{
    public const int SlotsPerRing = 4;
    public const double DefaultCenterRadius = 50;
    public const double InvalidCenterRadius = 50;
    public const double RingWidth = 60;
    public const double RingGap = 10;
    public const double OuterMargin = 12;

    public static RadialLayoutPlan Create(
        RadialPoint clickPoint,
        RadialRect workArea,
        bool invalidTarget = false)
    {
        var room = Math.Min(Math.Max(clickPoint.X - workArea.Left, workArea.Right - clickPoint.X),
            Math.Max(clickPoint.Y - workArea.Top, workArea.Bottom - clickPoint.Y)) - OuterMargin;
        var rings = invalidTarget
            ? Array.Empty<RadialRing>()
            : CreateRings(Math.Clamp(room, 126, 258), clickPoint, workArea);
        var centerRadius = invalidTarget ? InvalidCenterRadius : DefaultCenterRadius;
        var idealBounds = new RadialRect(clickPoint.X - centerRadius - OuterMargin,
            clickPoint.Y - centerRadius - OuterMargin, (centerRadius + OuterMargin) * 2,
            (centerRadius + OuterMargin) * 2);
        // Every ring owns its usable arc. Inner rings can use space that the
        // future outer ring cannot; opening another level never reflows them.
        foreach (var ring in rings)
            idealBounds = Union(idealBounds, GetSectorBounds(clickPoint,
                ring.OuterRadius + OuterMargin, ring.StartAngle, ring.SweepAngle));

        var clippedBounds = idealBounds.Intersect(workArea);
        var localCenter = new RadialPoint(
            clickPoint.X - clippedBounds.X,
            clickPoint.Y - clippedBounds.Y);

        return new RadialLayoutPlan(
            clickPoint,
            clippedBounds,
            localCenter,
            centerRadius,
            rings,
            invalidTarget);
    }

    public static RadialHit HitTest(RadialLayoutPlan plan, RadialPoint localPoint)
        => HitTest(
            plan,
            localPoint,
            SlotsPerRing,
            SlotsPerRing,
            SlotsPerRing);

    public static RadialHit HitTest(
        RadialLayoutPlan plan,
        RadialPoint localPoint,
        int firstRingSlots,
        int secondRingSlots,
        int thirdRingSlots)
    {
        var dx = localPoint.X - plan.LocalCenter.X;
        var dy = localPoint.Y - plan.LocalCenter.Y;
        var radius = Math.Sqrt(dx * dx + dy * dy);
        if (radius <= plan.CenterRadius)
            return RadialHit.Center;
        if (plan.IsInvalidTarget || plan.Rings.Count == 0)
            return RadialHit.None;

        var ringIndex = -1;
        for (var index = 0; index < plan.Rings.Count; index++)
        {
            var ring = plan.Rings[index];
            if (radius >= ring.InnerRadius && radius <= ring.OuterRadius)
            {
                ringIndex = index;
                break;
            }
        }

        if (ringIndex < 0)
            return RadialHit.None;

        var slotCount = ringIndex switch
        {
            0 => firstRingSlots,
            1 => secondRingSlots,
            2 => thirdRingSlots,
            _ => 0
        };
        slotCount = Math.Clamp(slotCount, 0, SlotsPerRing);
        if (slotCount == 0)
            return RadialHit.None;

        var angle = NormalizeAngle(Math.Atan2(dy, dx) * 180 / Math.PI);
        var activeRing = plan.Rings[ringIndex];
        var relative = RelativeAngle(angle, SlotStart(activeRing, slotCount));
        if (activeRing.SweepAngle < 360 && relative > activeRing.SweepAngle)
            return RadialHit.None;

        var slotSweep = SlotSweep(activeRing, slotCount);
        var slot = Math.Min(SlotsPerRing - 1, (int)Math.Floor(relative / slotSweep));
        if (slot >= slotCount) return RadialHit.None;
        return new RadialHit(ringIndex, slot, false);
    }

    internal static double SlotSweep(RadialRing ring, int visibleSlots) =>
        ring.SweepAngle / Math.Clamp(visibleSlots, 1, SlotsPerRing);

    internal static double SlotStart(RadialRing ring, int visibleSlots) =>
        ring.SweepAngle >= 359.999 ? -90 - SlotSweep(ring, visibleSlots) / 2 : ring.StartAngle;

    internal static double CellGap(RadialRing ring, int visibleSlots) =>
        ring.SweepAngle >= 359.999 && visibleSlots == 1 ? 0 :
            3 / ((ring.InnerRadius + ring.OuterRadius) / 2) * 180 / Math.PI;

    private static RadialRing[] CreateRings(double outerRadius, RadialPoint point, RadialRect area)
    {
        var rings = new RadialRing[3];
        var inner = DefaultCenterRadius + 8;
        var width = (outerRadius - inner - 2 * RingGap) / 3;
        for (var index = 0; index < rings.Length; index++)
        {
            var (start, sweep) = ChooseArc(point, area, inner + width);
            rings[index] = new RadialRing(inner, inner + width, start, sweep);
            inner += width + RingGap;
        }

        return rings;
    }

    private static (double Start, double Sweep) ChooseArc(
        RadialPoint point,
        RadialRect workArea,
        double radius)
    {
        var left = Math.Max(0, point.X - workArea.Left - OuterMargin);
        var right = Math.Max(0, workArea.Right - point.X - OuterMargin);
        var top = Math.Max(0, point.Y - workArea.Top - OuterMargin);
        var bottom = Math.Max(0, workArea.Bottom - point.Y - OuterMargin);
        var cuts = new List<double> { 0, 360 };
        void CutX(double x)
        {
            if (Math.Abs(x) >= radius) return;
            var a = Math.Acos(x / radius) * 180 / Math.PI;
            cuts.Add(a); cuts.Add(360 - a);
        }
        void CutY(double y)
        {
            if (Math.Abs(y) >= radius) return;
            var a = Math.Asin(y / radius) * 180 / Math.PI;
            cuts.Add(NormalizeAngle(a)); cuts.Add(NormalizeAngle(180 - a));
        }
        CutX(-left); CutX(right); CutY(-top); CutY(bottom);
        var angles = cuts.Distinct().Order().ToArray();
        var runs = new List<(double Start, double End)>();
        for (var i = 0; i < angles.Length - 1; i++)
        {
            var mid = (angles[i] + angles[i + 1]) * Math.PI / 360;
            var x = radius * Math.Cos(mid); var y = radius * Math.Sin(mid);
            if (x < -left - 1e-8 || x > right + 1e-8 || y < -top - 1e-8 || y > bottom + 1e-8) continue;
            if (runs.Count > 0 && Math.Abs(runs[^1].End - angles[i]) < 1e-8)
                runs[^1] = (runs[^1].Start, angles[i + 1]);
            else runs.Add((angles[i], angles[i + 1]));
        }
        if (runs.Count > 1 && runs[0].Start == 0 && runs[^1].End == 360)
        {
            runs[^1] = (runs[^1].Start, runs[0].End + 360);
            runs.RemoveAt(0);
        }
        if (runs.Count == 0)
        {
            // A desktop smaller than the minimum control cannot fit a ring.
            // Preserve the inward fallback and let the window clip to the screen.
            return (right >= left ? (bottom >= top ? 0 : 270) : (bottom >= top ? 90 : 180), 90);
        }
        var best = runs.MaxBy(run => run.End - run.Start);
        return best.End - best.Start >= 359.999 ? (-135, 360) : (best.Start, best.End - best.Start);
    }

    private static RadialRect GetSectorBounds(
        RadialPoint center,
        double radius,
        double start,
        double sweep)
    {
        var minX = center.X;
        var minY = center.Y;
        var maxX = center.X;
        var maxY = center.Y;
        var steps = Math.Max(4, (int)Math.Ceiling(sweep / 2));
        for (var index = 0; index <= steps; index++)
        {
            var angle = (start + sweep * index / steps) * Math.PI / 180;
            var x = center.X + radius * Math.Cos(angle);
            var y = center.Y + radius * Math.Sin(angle);
            minX = Math.Min(minX, x);
            minY = Math.Min(minY, y);
            maxX = Math.Max(maxX, x);
            maxY = Math.Max(maxY, y);
        }

        return new RadialRect(minX, minY, maxX - minX, maxY - minY);
    }

    private static RadialRect Union(RadialRect rect, RadialRect other)
    {
        var left = Math.Min(rect.Left, other.Left);
        var top = Math.Min(rect.Top, other.Top);
        var right = Math.Max(rect.Right, other.Right);
        var bottom = Math.Max(rect.Bottom, other.Bottom);
        return new RadialRect(left, top, right - left, bottom - top);
    }

    internal static double NormalizeAngle(double angle)
    {
        angle %= 360;
        return angle < 0 ? angle + 360 : angle;
    }

    internal static double RelativeAngle(double angle, double start) =>
        NormalizeAngle(angle - NormalizeAngle(start));
}
