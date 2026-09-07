using Loadstone.Core.Geometry;
using Shouldly;
using Xunit;

namespace Loadstone.Core.Tests.Geometry;

public class RectTests
{
    [Fact]
    public void Edges_AreExclusiveOnRightAndBottom()
    {
        var r = new Rect(10, 20, 30, 40);
        r.Left.ShouldBe(10);
        r.Top.ShouldBe(20);
        r.Right.ShouldBe(40);
        r.Bottom.ShouldBe(60);
        r.Contains(new Point(39, 59)).ShouldBeTrue();
        r.Contains(new Point(40, 59)).ShouldBeFalse();
        r.Contains(new Point(39, 60)).ShouldBeFalse();
        r.Contains(new Point(10, 20)).ShouldBeTrue();
    }

    [Fact]
    public void FromEdges_RoundTrips()
    {
        Rect.FromEdges(-5, 7, 10, 9).ShouldBe(new Rect(-5, 7, 15, 2));
    }

    [Fact]
    public void Center_UsesFloorDivision()
    {
        new Rect(0, 0, 1201, 901).Center.ShouldBe(new Point(600, 450));
    }

    [Fact]
    public void Inflate_GrowsAndShrinks()
    {
        new Rect(0, 0, 10, 10).Inflate(2, 3).ShouldBe(new Rect(-2, -3, 14, 16));
        new Rect(0, 0, 10, 10).Inflate(-1, -1).ShouldBe(new Rect(1, 1, 8, 8));
    }

    [Fact]
    public void HasSameSize_IgnoresPosition()
    {
        new Rect(0, 0, 5, 6).HasSameSize(new Rect(100, 200, 5, 6)).ShouldBeTrue();
        new Rect(0, 0, 5, 6).HasSameSize(new Rect(0, 0, 5, 7)).ShouldBeFalse();
    }
}
