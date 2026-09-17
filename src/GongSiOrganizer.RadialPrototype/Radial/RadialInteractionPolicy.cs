namespace GongSiOrganizer.RadialPrototype.Radial;

internal readonly record struct RadialScrollMarker(
    double Radius,
    double Angle,
    int Direction);

internal enum RadialPrimaryClickAction
{
    Dismiss,
    Confirm,
    CreateFolder
}

internal static class RadialInteractionPolicy
{
    private const double MarkerSeparationPixels = 7;

    internal static TimeSpan NormalHoverDelay { get; } =
        TimeSpan.FromMilliseconds(450);

    internal static TimeSpan WheelHoverDelay { get; } =
        TimeSpan.FromMilliseconds(850);

    internal static TimeSpan EntryArcDelay { get; } = TimeSpan.FromMilliseconds(110);

    internal static bool RequiresEntryArc(RadialLayoutPlan layout, RadialHit hit) =>
        hit.RingIndex == 2;

    internal static (double Radius, double Start, double End) EntryArc(
        RadialLayoutPlan layout, RadialHit hit, int visibleSlots = 4)
    {
        var ring = layout.Rings[hit.RingIndex];
        var sweep = RadialLayout.SlotSweep(ring, visibleSlots);
        var slotStart = RadialLayout.SlotStart(ring, visibleSlots) + hit.SlotIndex * sweep;
        var gap = 3 / ((ring.InnerRadius + ring.OuterRadius) / 2) * 180 / Math.PI;
        var roundedEdge = gap + Math.Asin(11 / (ring.OuterRadius - 11)) * 180 / Math.PI + 1.2;
        var start = Math.Max(slotStart + roundedEdge, slotStart + sweep / 2 - 12 - sweep * 28 / 90);
        var end = Math.Min(start + sweep * 28 / 90, slotStart + sweep - roundedEdge);
        return (ring.OuterRadius - 2, start, Math.Max(start + 1, end));
    }

    internal static bool IsEntryArc(RadialLayoutPlan layout, RadialHit hit, RadialPoint point, int visibleSlots = 4) =>
        RequiresEntryArc(layout, hit) && DistanceToEntryArc(layout, hit, point, visibleSlots) <= 10;

    private static double DistanceToEntryArc(RadialLayoutPlan layout, RadialHit hit, RadialPoint point, int visibleSlots)
    {
        var arc = EntryArc(layout, hit, visibleSlots);
        var dx = point.X - layout.LocalCenter.X;
        var dy = point.Y - layout.LocalCenter.Y;
        var radius = Math.Sqrt(dx * dx + dy * dy);
        var angle = Math.Atan2(dy, dx) * 180 / Math.PI;
        if (RadialLayout.RelativeAngle(angle, arc.Start) <= arc.End - arc.Start)
            return Math.Abs(radius - arc.Radius);
        var start = RadialGeometry.Polar(layout.LocalCenter, arc.Radius, arc.Start);
        var end = RadialGeometry.Polar(layout.LocalCenter, arc.Radius, arc.End);
        return Math.Min(Math.Sqrt(Math.Pow(point.X - start.X, 2) + Math.Pow(point.Y - start.Y, 2)),
            Math.Sqrt(Math.Pow(point.X - end.X, 2) + Math.Pow(point.Y - end.Y, 2)));
    }

    internal static bool IsReadingBody(RadialLayoutPlan layout, RadialHit hit, RadialPoint point, int visibleSlots = 4)
    {
        if (hit.RingIndex < 0) return false;
        // Two DIP hysteresis around the actual round-ended hit stroke. Moving
        // past either endpoint also re-arms; a trip toward the centre is unnecessary.
        return DistanceToEntryArc(layout, hit, point, visibleSlots) > 12;
    }

    internal static int ResolveScrollableRing(
        RadialHit hit,
        IReadOnlyList<RadialRingView> views)
    {
        if (hit.RingIndex < 0 || hit.RingIndex >= views.Count)
            return -1;

        return views[hit.RingIndex].TotalCount > RadialLayout.SlotsPerRing
            ? hit.RingIndex
            : -1;
    }

    internal static RadialPrimaryClickAction ResolvePrimaryClick(
        bool hasDestination,
        bool controlPressed)
    {
        if (!hasDestination) return RadialPrimaryClickAction.Dismiss;
        return controlPressed
            ? RadialPrimaryClickAction.CreateFolder
            : RadialPrimaryClickAction.Confirm;
    }

    internal static (RadialScrollMarker First, RadialScrollMarker Second)
        GetScrollMarkers(RadialLayoutPlan layout, int ringIndex)
    {
        if (ringIndex < 0 || ringIndex >= layout.Rings.Count)
            throw new ArgumentOutOfRangeException(nameof(ringIndex));

        var ring = layout.Rings[ringIndex];
        var radius = (ring.InnerRadius + ring.OuterRadius) / 2;
        var angularSeparation = MarkerSeparationPixels / radius * 180 / Math.PI;

        if (ring.SweepAngle >= 359.999)
        {
            return (
                new RadialScrollMarker(radius, -angularSeparation, 1),
                new RadialScrollMarker(radius, angularSeparation, -1));
        }

        // Keep both hints outside the directory cells but on the hovered
        // ring's centerline.  The chevrons point toward the two exit ends.
        return (
            new RadialScrollMarker(
                radius,
                ring.StartAngle - angularSeparation,
                -1),
            new RadialScrollMarker(
                radius,
                ring.StartAngle + ring.SweepAngle + angularSeparation,
                1));
    }

}
