namespace GongSiOrganizer.RadialPrototype.Radial;

internal enum RadialLabelDirection { Horizontal, Arc, Adaptive }

internal sealed record RadialLabelMetric(int Ring, int Slot, int VisibleCharacters, int TotalCharacters, bool IsTruncated, string Direction);

internal sealed record RadialDisplayOptions(
    int MaximumItems = 4,
    RadialLabelDirection LabelDirection = RadialLabelDirection.Horizontal,
    double RingWidth = 60,
    bool AdaptCountToSpace = false,
    bool EnhancedLabels = false)
{
    public int[] Capacities(RadialLayoutPlan layout) => layout.Rings.Select(ring =>
    {
        var limit = Math.Clamp(MaximumItems, 2, RadialLayout.MaximumSlotsPerRing);
        if (!AdaptCountToSpace) return limit;
        // Reserve about 56 DIP of arc per target. The configured number is a
        // ceiling; a short corner arc should not become eight tiny targets.
        var arcLength = (ring.InnerRadius + ring.OuterRadius) / 2 * ring.SweepAngle * Math.PI / 180;
        return Math.Clamp((int)Math.Floor(arcLength / 56), 2, limit);
    }).ToArray();
}
