using GongSiOrganizer.RadialPrototype.Radial;
using Xunit;

namespace GongSiOrganizer.Core.Tests.Radial;

public sealed class RadialLayoutTests
{
    private static readonly RadialRect WorkArea = new(0, 0, 1920, 1080);

    [Fact]
    public void Corner_UsesTheFreeSpaceLeftAndBelowTheFirstRing()
    {
        var plan = RadialLayout.Create(new RadialPoint(50, 1000), WorkArea);
        Assert.Equal(new RadialHit(0, 0, false), RadialLayout.HitTest(plan, Polar(plan.LocalCenter, 88, 255), 4, 0, 0));
        Assert.Equal(new RadialHit(0, 3, false), RadialLayout.HitTest(plan, Polar(plan.LocalCenter, 88, 385), 4, 0, 0));
        Assert.Equal(RadialHit.None, RadialLayout.HitTest(plan, Polar(plan.LocalCenter, 228, 385), 4, 4, 4));
    }

    [Fact]
    public void InnerRingRemainsFullWhenOnlyTheOuterRingTouchesAnEdge()
    {
        var plan = RadialLayout.Create(new RadialPoint(200, 540), WorkArea);
        Assert.Equal(360, plan.Rings[0].SweepAngle);
        Assert.Equal(360, plan.Rings[1].SweepAngle);
        Assert.InRange(plan.Rings[2].SweepAngle, 180, 359);
    }

    [Theory]
    [InlineData(50, 80)]
    [InlineData(1870, 80)]
    [InlineData(50, 1000)]
    [InlineData(1870, 1000)]
    [InlineData(50, 540)]
    [InlineData(960, 80)]
    [InlineData(129.9999, 540)]
    public void EachRingUsesItsOwnMaximumContinuousArcWithoutLeavingTheWorkArea(double x, double y)
    {
        var click = new RadialPoint(x, y);
        var plan = RadialLayout.Create(click, WorkArea);
        var safe = new RadialRect(Math.Min(x, 12), Math.Min(y, 12),
            Math.Max(x, 1908) - Math.Min(x, 12), Math.Max(y, 1068) - Math.Min(y, 12));
        bool Inside(RadialPoint p, double epsilon = 0) => p.X >= safe.Left - epsilon &&
            p.X <= safe.Right + epsilon && p.Y >= safe.Top - epsilon && p.Y <= safe.Bottom + epsilon;
        Assert.Equal(click, plan.ScreenCenter);
        for (var index = 0; index < plan.Rings.Count; index++)
        {
            var ring = plan.Rings[index];
            for (var step = 0; step <= 720; step++)
                Assert.True(Inside(Polar(click, ring.OuterRadius, ring.StartAngle + ring.SweepAngle * step / 720), 1e-7));
            if (ring.SweepAngle < 360)
            {
                Assert.False(Inside(Polar(click, ring.OuterRadius, ring.StartAngle - .01)));
                Assert.False(Inside(Polar(click, ring.OuterRadius, ring.StartAngle + ring.SweepAngle + .01)));
            }
            if (index > 0) Assert.True(ring.SweepAngle <= plan.Rings[index - 1].SweepAngle + 1e-7);
            foreach (var count in new[] { 1, 2, 3, 4 })
            for (var slot = 0; slot < count; slot++)
            {
                var angle = RadialLayout.SlotStart(ring, count) + (slot + .5) * RadialLayout.SlotSweep(ring, count);
                var point = Polar(plan.LocalCenter, (ring.InnerRadius + ring.OuterRadius) / 2, angle);
                Assert.Equal(new RadialHit(index, slot, false), RadialLayout.HitTest(plan, point, count, count, count));
            }
        }
    }

    [Fact]
    public void Create_InOpenSpace_UsesFullCircleAndKeepsExactScreenCenter()
    {
        var click = new RadialPoint(960, 540);

        var plan = RadialLayout.Create(click, WorkArea);

        Assert.Equal(360, plan.SweepAngle);
        Assert.Equal(50, plan.CenterRadius);
        Assert.Equal(258, plan.OuterRadius);
        Assert.Equal(click, plan.ScreenCenter);
        Assert.Equal(click.X, plan.WindowBounds.X + plan.LocalCenter.X, 6);
        Assert.Equal(click.Y, plan.WindowBounds.Y + plan.LocalCenter.Y, 6);
        Assert.All(plan.Rings, ring => Assert.True(ring.OuterRadius > ring.InnerRadius));
    }

    [Fact]
    public void Create_AtTopLeft_UsesQuarterCircleAndKeepsClickCentre()
    {
        var click = new RadialPoint(12, 12);

        var plan = RadialLayout.Create(click, WorkArea);

        Assert.Equal(0, plan.StartAngle);
        Assert.Equal(90, plan.SweepAngle);
        Assert.True(plan.WindowBounds.Left >= WorkArea.Left);
        Assert.True(plan.WindowBounds.Top >= WorkArea.Top);
        Assert.Equal(click, plan.ScreenCenter);
        Assert.Equal(click.X, plan.WindowBounds.X + plan.LocalCenter.X, 6);
        Assert.Equal(click.Y, plan.WindowBounds.Y + plan.LocalCenter.Y, 6);
    }

    [Fact]
    public void Create_NearLeftEdge_UsesInwardHalfCircle()
    {
        var plan = RadialLayout.Create(new RadialPoint(10, 540), WorkArea);

        Assert.Equal(270, plan.StartAngle);
        Assert.Equal(180, plan.SweepAngle);
        Assert.Equal(10, plan.ScreenCenter.X);
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

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public void FullCircle_UsesEveryAngleForTheActualDirectoryCount(int count)
    {
        var plan = RadialLayout.Create(new RadialPoint(960, 540), WorkArea);
        var start = -90 - 180d / count;
        for (var slot = 0; slot < count; slot++)
        foreach (var fraction in new[] { .02, .25, .5, .75, .98 })
        {
            var p = Polar(plan.LocalCenter, 228, start + (slot + fraction) * 360 / count);
            Assert.Equal(new RadialHit(2, slot, false), RadialLayout.HitTest(plan, p, 1, 2, count));
        }
    }

    [Fact]
    public void HitTest_UsesTheActualVisibleSiblingCountPerRing()
    {
        var plan = RadialLayout.Create(new RadialPoint(12, 18), WorkArea);
        var secondRing = plan.Rings[1];
        var radius = (secondRing.InnerRadius + secondRing.OuterRadius) / 2;
        var point = Polar(plan.LocalCenter, radius, plan.StartAngle + 67.5);

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
