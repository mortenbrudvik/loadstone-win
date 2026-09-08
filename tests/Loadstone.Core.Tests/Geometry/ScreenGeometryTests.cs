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
    public void Neighbor_MixedDpiDisplays_OrdersByFrameNotScale()
    {
        // A 200 percent laptop panel wedged between a 100 percent monitor on the far left and a stacked
        // pair on the right, one of which is at 150 percent. Reading order is frame x then frame y in
        // physical pixels; a display's DPI must not enter it, or Next Display walks the desk in a
        // different order once the user scales one screen.
        var far = new Display(new Rect(-5000, -200, 1920, 1080), new Rect(-5000, -200, 1920, 1032), 96);
        var laptop = new Display(new Rect(-3080, -200, 3072, 1920), new Rect(-3080, -200, 3072, 1872), 192);
        var upper = new Display(new Rect(-8, -1440, 2560, 1440), new Rect(-8, -1440, 2560, 1392), 96);
        var lower = new Display(new Rect(-8, 0, 2560, 1440), new Rect(-8, 0, 2560, 1392), 144);
        Display[] displays = [lower, laptop, upper, far];

        ScreenGeometry.Neighbor(far, +1, displays).ShouldBe(laptop);
        ScreenGeometry.Neighbor(laptop, +1, displays).ShouldBe(upper);
        ScreenGeometry.Neighbor(upper, +1, displays).ShouldBe(lower);
        ScreenGeometry.Neighbor(lower, +1, displays).ShouldBe(far);
        ScreenGeometry.Neighbor(lower, -1, displays).ShouldBe(upper);
        ScreenGeometry.Neighbor(far, -1, displays).ShouldBe(lower);
    }

    [Fact]
    public void Scale_IsDpiOver96()
    {
        Primary.Scale.ShouldBe(1.5);
        LeftOf.Scale.ShouldBe(1.0);
        new Display(new Rect(0, 0, 3840, 2160), new Rect(0, 0, 3840, 2088), 192).Scale.ShouldBe(2.0);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-96)]
    public void Display_DpiNotPositive_Throws(int dpi)
    {
        // A DPI of zero makes Scale zero, which rounds SnapZones' 16 px bands and 140 px corners away to
        // nothing: the display would have no drag-snap zones at all, with no error anywhere to say why.
        Should.Throw<ArgumentOutOfRangeException>(
            () => new Display(new Rect(-1920, 37, 1201, 901), new Rect(-1920, 37, 1201, 901), dpi));
    }
}
