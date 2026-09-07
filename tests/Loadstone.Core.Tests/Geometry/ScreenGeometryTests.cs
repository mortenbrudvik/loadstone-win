using Loadstone.Core.Geometry;
using Shouldly;
using Xunit;

namespace Loadstone.Core.Tests.Geometry;

public class ScreenGeometryTests
{
    // Primary 4K at 150%, a 100% display to its left with a negative origin, a third stacked under the primary.
    private static readonly Display Primary = new(new Rect(0, 0, 3840, 2160), new Rect(0, 0, 3840, 2088), 144);
    private static readonly Display LeftOf = new(new Rect(-2560, 0, 2560, 1440), new Rect(-2560, 0, 2560, 1392), 96);
    private static readonly Display Below = new(new Rect(0, 2160, 1920, 1080), new Rect(0, 2160, 1920, 1040), 96);
    private static readonly Display[] Displays = [Primary, LeftOf, Below];

    [Fact]
    public void DisplayContaining_Point_ExactContainmentWins()
    {
        ScreenGeometry.DisplayContaining(new Point(-1, 10), Displays).ShouldBe(LeftOf);
        ScreenGeometry.DisplayContaining(new Point(0, 10), Displays).ShouldBe(Primary);
        ScreenGeometry.DisplayContaining(new Point(100, 2160), Displays).ShouldBe(Below);
    }

    [Fact]
    public void DisplayContaining_Point_OnOuterEdge_UsesOnePixelTolerance()
    {
        ScreenGeometry.DisplayContaining(new Point(3840, 100), Displays).ShouldBe(Primary);
        ScreenGeometry.DisplayContaining(new Point(100, -1), Displays).ShouldBe(Primary);
    }

    [Fact]
    public void DisplayContaining_Point_OnNoDisplay_IsNull()
    {
        ScreenGeometry.DisplayContaining(new Point(9000, 9000), Displays).ShouldBeNull();
        ScreenGeometry.DisplayContaining(new Point(0, 0), []).ShouldBeNull();
    }

    [Fact]
    public void DisplayContaining_Rect_UsesTheCenter()
    {
        // Straddles the shared edge; the center is on the left display.
        ScreenGeometry.DisplayContaining(new Rect(-700, 100, 1000, 500), Displays).ShouldBe(LeftOf);
        ScreenGeometry.DisplayContaining(new Rect(-300, 100, 1000, 500), Displays).ShouldBe(Primary);
    }

    [Fact]
    public void Neighbor_WalksReadingOrderAndWraps()
    {
        // Reading order: LeftOf (x=-2560), Primary (x=0, y=0), Below (x=0, y=2160).
        ScreenGeometry.Neighbor(LeftOf, +1, Displays).ShouldBe(Primary);
        ScreenGeometry.Neighbor(Primary, +1, Displays).ShouldBe(Below);
        ScreenGeometry.Neighbor(Below, +1, Displays).ShouldBe(LeftOf);
        ScreenGeometry.Neighbor(LeftOf, -1, Displays).ShouldBe(Below);
        ScreenGeometry.Neighbor(Primary, -1, Displays).ShouldBe(LeftOf);
    }

    [Fact]
    public void Neighbor_SingleDisplay_IsItself()
    {
        ScreenGeometry.Neighbor(Primary, +1, [Primary]).ShouldBe(Primary);
        ScreenGeometry.Neighbor(Primary, -1, [Primary]).ShouldBe(Primary);
    }

    [Fact]
    public void Neighbor_UnknownDisplay_IsNull()
    {
        ScreenGeometry.Neighbor(Below, +1, [Primary, LeftOf]).ShouldBeNull();
    }

    [Fact]
    public void Scale_IsDpiOver96()
    {
        Primary.Scale.ShouldBe(1.5);
        LeftOf.Scale.ShouldBe(1.0);
    }
}
