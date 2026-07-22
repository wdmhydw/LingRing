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

public readonly record struct RadialRing(double InnerRadius, double OuterRadius);

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
    double StartAngle,
    double SweepAngle,
    double CenterRadius,
    IReadOnlyList<RadialRing> Rings,
    bool IsInvalidTarget)
{
    public double OuterRadius => Rings.Count == 0 ? CenterRadius : Rings[^1].OuterRadius;
}

public static class RadialLayout
{
    public const int SlotsPerRing = 4;
    public const double DefaultCenterRadius = 62;
    public const double InvalidCenterRadius = 50;
    public const double RingWidth = 62;
    public const double RingGap = 3;
    public const double OuterMargin = 12;

    public static RadialLayoutPlan Create(
        RadialPoint clickPoint,
        RadialRect workArea,
        bool invalidTarget = false)
    {
        var rings = invalidTarget
            ? Array.Empty<RadialRing>()
            : CreateRings();
        var centerRadius = invalidTarget ? InvalidCenterRadius : DefaultCenterRadius;
        var outerRadius = invalidTarget ? centerRadius : rings[^1].OuterRadius;
        var (start, sweep) = invalidTarget
            ? (0d, 0d)
            : ChooseArc(clickPoint, workArea, outerRadius + OuterMargin);

        var idealBounds = invalidTarget
            ? new RadialRect(
                clickPoint.X - centerRadius - OuterMargin,
                clickPoint.Y - centerRadius - OuterMargin,
                (centerRadius + OuterMargin) * 2,
                (centerRadius + OuterMargin) * 2)
            : GetSectorBounds(clickPoint, outerRadius + OuterMargin, start, sweep)
                .UnionCircle(clickPoint, centerRadius + OuterMargin);

        var clippedBounds = idealBounds.Intersect(workArea);
        var localCenter = new RadialPoint(
            clickPoint.X - clippedBounds.X,
            clickPoint.Y - clippedBounds.Y);

        return new RadialLayoutPlan(
            clickPoint,
            clippedBounds,
            localCenter,
            start,
            sweep,
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
        var relative = RelativeAngle(angle, plan.StartAngle);
        if (plan.SweepAngle < 360 && relative > plan.SweepAngle)
            return RadialHit.None;

        var slotSweep = plan.SweepAngle / slotCount;
        var slot = Math.Min(slotCount - 1, (int)Math.Floor(relative / slotSweep));
        return new RadialHit(ringIndex, slot, false);
    }

    private static RadialRing[] CreateRings()
    {
        var rings = new RadialRing[3];
        var inner = DefaultCenterRadius + RingGap;
        for (var index = 0; index < rings.Length; index++)
        {
            rings[index] = new RadialRing(inner, inner + RingWidth);
            inner += RingWidth + RingGap;
        }

        return rings;
    }

    private static (double Start, double Sweep) ChooseArc(
        RadialPoint point,
        RadialRect workArea,
        double needed)
    {
        var nearLeft = point.X - workArea.Left < needed;
        var nearRight = workArea.Right - point.X < needed;
        var nearTop = point.Y - workArea.Top < needed;
        var nearBottom = workArea.Bottom - point.Y < needed;

        if (nearLeft && nearTop) return (0, 90);
        if (nearRight && nearTop) return (90, 90);
        if (nearRight && nearBottom) return (180, 90);
        if (nearLeft && nearBottom) return (270, 90);
        if (nearTop) return (0, 180);
        if (nearRight) return (90, 180);
        if (nearBottom) return (180, 180);
        if (nearLeft) return (270, 180);
        return (-90, 360);
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

    private static RadialRect UnionCircle(
        this RadialRect rect,
        RadialPoint center,
        double radius)
    {
        var left = Math.Min(rect.Left, center.X - radius);
        var top = Math.Min(rect.Top, center.Y - radius);
        var right = Math.Max(rect.Right, center.X + radius);
        var bottom = Math.Max(rect.Bottom, center.Y + radius);
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
