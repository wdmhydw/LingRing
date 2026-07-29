using GongSiOrganizer.RadialPrototype.Radial;
using Xunit;

namespace GongSiOrganizer.Core.Tests.Radial;

public sealed class RadialInteractionPolicyTests
{
    private static readonly RadialRect WorkArea = new(0, 0, 1920, 1080);

    [Fact]
    public void ResolveScrollableRing_DoesNotFallBackFromShortChildToParent()
    {
        var views = new[]
        {
            View(8),
            View(7),
            View(3)
        };

        var ring = RadialInteractionPolicy.ResolveScrollableRing(
            new RadialHit(2, 0, false),
            views);

        Assert.Equal(-1, ring);
    }

    [Fact]
    public void ResolveScrollableRing_UsesOnlyTheRingUnderThePointer()
    {
        var views = new[]
        {
            View(8),
            View(7),
            View(6)
        };

        Assert.Equal(
            1,
            RadialInteractionPolicy.ResolveScrollableRing(
                new RadialHit(1, 2, false),
                views));
        Assert.Equal(
            -1,
                RadialInteractionPolicy.ResolveScrollableRing(RadialHit.Center, views));
    }

    [Theory]
    [InlineData(false, false, 0)]
    [InlineData(false, true, 0)]
    [InlineData(true, false, 1)]
    [InlineData(true, true, 2)]
    public void ResolvePrimaryClick_DistinguishesBlankConfirmAndCreate(
        bool hasDestination,
        bool controlPressed,
        int expected)
    {
        Assert.Equal(
            (RadialPrimaryClickAction)expected,
            RadialInteractionPolicy.ResolvePrimaryClick(hasDestination, controlPressed));
    }

    [Fact]
    public void HoverIntent_UsesLongerDelayAfterWheelInput()
    {
        Assert.Equal(
            TimeSpan.FromMilliseconds(450),
            RadialInteractionPolicy.NormalHoverDelay);
        Assert.Equal(
            TimeSpan.FromMilliseconds(850),
            RadialInteractionPolicy.WheelHoverDelay);
        Assert.True(
            RadialInteractionPolicy.WheelHoverDelay >
            RadialInteractionPolicy.NormalHoverDelay);
    }

    [Fact]
    public void QuarterCircleMarkers_UseHoveredRingAndPointTowardTheArcEnds()
    {
        var layout = RadialLayout.Create(new RadialPoint(12, 18), WorkArea);

        var markers = RadialInteractionPolicy.GetScrollMarkers(layout, 1);
        var ring = layout.Rings[1];
        var expectedRadius = (ring.InnerRadius + ring.OuterRadius) / 2;

        Assert.Equal(expectedRadius, markers.First.Radius, 6);
        Assert.Equal(expectedRadius, markers.Second.Radius, 6);
        Assert.True(markers.First.Angle < layout.StartAngle);
        Assert.Equal(-1, markers.First.Direction);
        Assert.True(markers.Second.Angle > layout.StartAngle + layout.SweepAngle);
        Assert.Equal(1, markers.Second.Direction);
    }

    [Fact]
    public void FullCircleMarkers_StayOnHoveredRingWithOppositeDirections()
    {
        var layout = RadialLayout.Create(new RadialPoint(960, 540), WorkArea);

        var markers = RadialInteractionPolicy.GetScrollMarkers(layout, 0);
        var ring = layout.Rings[0];
        var expectedRadius = (ring.InnerRadius + ring.OuterRadius) / 2;

        Assert.Equal(expectedRadius, markers.First.Radius, 6);
        Assert.Equal(expectedRadius, markers.Second.Radius, 6);
        Assert.Equal(1, markers.First.Direction);
        Assert.Equal(-1, markers.Second.Direction);
    }

    private static RadialRingView View(int totalCount) =>
        new([null, null, null, null], totalCount, 0, null);
}
