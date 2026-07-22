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

        if (layout.SweepAngle >= 359.5)
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
                layout.StartAngle - angularSeparation,
                -1),
            new RadialScrollMarker(
                radius,
                layout.StartAngle + layout.SweepAngle + angularSeparation,
                1));
    }
}
