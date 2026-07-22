using GongSiOrganizer.RadialPrototype.Radial;
using Xunit;

namespace GongSiOrganizer.Core.Tests.Radial;

public sealed class RadialLayoutTests
{
    private static readonly RadialRect WorkArea = new(0, 0, 1920, 1080);

    [Fact]
    public void Create_InOpenSpace_UsesFullCircleAndKeepsExactScreenCenter()
    {
        var click = new RadialPoint(960, 540);

        var plan = RadialLayout.Create(click, WorkArea);

        Assert.Equal(360, plan.SweepAngle);
        Assert.Equal(62, plan.CenterRadius);
        Assert.Equal(257, plan.OuterRadius);
        Assert.Equal(538, plan.WindowBounds.Width, 6);
        Assert.Equal(click, plan.ScreenCenter);
        Assert.Equal(click.X, plan.WindowBounds.X + plan.LocalCenter.X, 6);
        Assert.Equal(click.Y, plan.WindowBounds.Y + plan.LocalCenter.Y, 6);
        Assert.All(plan.Rings, ring => Assert.True(ring.OuterRadius > ring.InnerRadius));
    }

    [Fact]
    public void Create_AtTopLeft_UsesQuarterCircleWithoutMovingCenter()
    {
        var click = new RadialPoint(12, 18);

        var plan = RadialLayout.Create(click, WorkArea);

        Assert.Equal(0, plan.StartAngle);
        Assert.Equal(90, plan.SweepAngle);
        Assert.Equal(click.X, plan.WindowBounds.X + plan.LocalCenter.X, 6);
        Assert.Equal(click.Y, plan.WindowBounds.Y + plan.LocalCenter.Y, 6);
    }

    [Fact]
    public void Create_NearLeftEdge_UsesRightFacingHalfCircle()
    {
        var plan = RadialLayout.Create(new RadialPoint(10, 540), WorkArea);

        Assert.Equal(270, plan.StartAngle);
        Assert.Equal(180, plan.SweepAngle);
    }

    [Fact]
    public void HitTest_MapsEveryRingToAConcentricSlot()
    {
        var plan = RadialLayout.Create(new RadialPoint(960, 540), WorkArea);

        for (var ringIndex = 0; ringIndex < plan.Rings.Count; ringIndex++)
        {
            var ring = plan.Rings[ringIndex];
            var radius = (ring.InnerRadius + ring.OuterRadius) / 2;
            var angle = plan.StartAngle + plan.SweepAngle / RadialLayout.SlotsPerRing / 2;
            var point = Polar(plan.LocalCenter, radius, angle);

            Assert.Equal(new RadialHit(ringIndex, 0, false), RadialLayout.HitTest(plan, point));
        }
    }

    [Fact]
    public void InvalidTarget_HasOnlyDismissibleCenter()
    {
        var plan = RadialLayout.Create(new RadialPoint(300, 300), WorkArea, true);

        Assert.True(plan.IsInvalidTarget);
        Assert.Equal(50, plan.CenterRadius);
        Assert.Empty(plan.Rings);
        Assert.Equal(RadialHit.Center, RadialLayout.HitTest(plan, plan.LocalCenter));
    }

    [Fact]
    public void HitTest_UsesTheActualVisibleSiblingCountPerRing()
    {
        var plan = RadialLayout.Create(new RadialPoint(12, 18), WorkArea);
        var secondRing = plan.Rings[1];
        var radius = (secondRing.InnerRadius + secondRing.OuterRadius) / 2;
        var point = Polar(plan.LocalCenter, radius, plan.StartAngle + plan.SweepAngle * 0.75);

        var hit = RadialLayout.HitTest(plan, point, 4, 2, 0);

        Assert.Equal(new RadialHit(1, 1, false), hit);
        var thirdRing = plan.Rings[2];
        var emptyPoint = Polar(
            plan.LocalCenter,
            (thirdRing.InnerRadius + thirdRing.OuterRadius) / 2,
            plan.StartAngle + plan.SweepAngle / 2);
        Assert.Equal(RadialHit.None, RadialLayout.HitTest(plan, emptyPoint, 4, 2, 0));
    }

    private static RadialPoint Polar(RadialPoint center, double radius, double angle)
    {
        var radians = angle * Math.PI / 180;
        return new RadialPoint(
            center.X + radius * Math.Cos(radians),
            center.Y + radius * Math.Sin(radians));
    }
}
