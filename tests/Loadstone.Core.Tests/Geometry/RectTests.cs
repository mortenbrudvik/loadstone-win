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
        new Rect(-1920, 37, 1201, 901).Center.ShouldBe(new Point(-1320, 487));
    }

    [Fact]
    public void Inflate_GrowsAndShrinks()
    {
        new Rect(0, 0, 10, 10).Inflate(2, 3).ShouldBe(new Rect(-2, -3, 14, 16));
        new Rect(0, 0, 10, 10).Inflate(-1, -1).ShouldBe(new Rect(1, 1, 8, 8));
    }

    [Theory]
    [InlineData(0, 10)]
    [InlineData(10, 0)]
    [InlineData(0, 0)]
    [InlineData(-1, 10)]
    [InlineData(10, -1)]
    public void IsEmpty_ZeroOrNegativeSide_IsTrue(int width, int height)
    {
        new Rect(-1920, 37, width, height).IsEmpty.ShouldBeTrue();
    }

    [Fact]
    public void IsEmpty_OnePixel_IsFalse()
    {
        new Rect(-1920, 37, 1, 1).IsEmpty.ShouldBeFalse();
        new Rect(-1920, 37, 1201, 901).IsEmpty.ShouldBeFalse();
    }

    [Fact]
    public void Inflate_ShrinkPastTheMiddle_InvertsTheRect()
    {
        // Shrinking a 10x10 by 6 puts Right left of Left, and there is no validating constructor to
        // catch it: Contains then answers false for every point, including the ones that were inside,
        // rather than failing loudly. IsEmpty is the check a caller has to make after an inflate whose
        // deltas are not known small.
        var inverted = new Rect(0, 0, 10, 10).Inflate(-6, -6);
        inverted.ShouldBe(new Rect(6, 6, -2, -2));
        inverted.Right.ShouldBe(4);
        inverted.Bottom.ShouldBe(4);
        inverted.IsEmpty.ShouldBeTrue();
        inverted.Contains(new Point(5, 5)).ShouldBeFalse();
        inverted.Contains(new Point(6, 6)).ShouldBeFalse();
        inverted.Contains(new Point(0, 0)).ShouldBeFalse();

        // Exactly half is the last non-inverted step, and it is already empty.
        var collapsed = new Rect(0, 0, 10, 10).Inflate(-5, -5);
        collapsed.ShouldBe(new Rect(5, 5, 0, 0));
        collapsed.IsEmpty.ShouldBeTrue();
        collapsed.Contains(new Point(5, 5)).ShouldBeFalse();
    }

    [Fact]
    public void HasSameSize_IgnoresPosition()
    {
        new Rect(0, 0, 5, 6).HasSameSize(new Rect(100, 200, 5, 6)).ShouldBeTrue();
        new Rect(0, 0, 5, 6).HasSameSize(new Rect(0, 0, 5, 7)).ShouldBeFalse();
    }
}
