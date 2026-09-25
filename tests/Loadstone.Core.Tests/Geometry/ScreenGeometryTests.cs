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
    public void Adjacent_FindsTheDisplayDirectlyOnThatSide()
    {
        ScreenGeometry.Adjacent(Primary, Side.Left, Displays).ShouldBe(LeftOf);
        ScreenGeometry.Adjacent(LeftOf, Side.Right, Displays).ShouldBe(Primary);
    }

    [Fact]
    public void Adjacent_NeverWraps_AndNeverAnswersWithADisplayAboveOrBelow()
    {
        // Reading order answers Below for the display after Primary and wraps from LeftOf round to
        // Below, which is why Neighbor cannot say where a half carries on to. LeftOf lies wholly to the
        // left of Below but shares none of its rows, so it is not beside it either.
        ScreenGeometry.Adjacent(Primary, Side.Right, Displays).ShouldBeNull();
        ScreenGeometry.Adjacent(LeftOf, Side.Left, Displays).ShouldBeNull();
        ScreenGeometry.Adjacent(Below, Side.Left, Displays).ShouldBeNull();
    }

    [Fact]
    public void Adjacent_ADisplayTouchingOnlyAtACorner_IsNotAdjacent()
    {
        // The corner display ends at x = 0, where here starts, and begins at y = 901, the row after
        // here's last: it touches one corner and shares no row. Right and Bottom are exclusive, so
        // an inclusive count, or a test for zero shared rows instead of more than zero, would call
        // it adjacent and send a half on to a display that is really below.
        var here = new Display(new Rect(0, 0, 1201, 901), new Rect(0, 0, 1201, 861), 96);
        var corner = new Display(new Rect(-1001, 901, 1001, 601), new Rect(-1001, 901, 1001, 561), 96);

        ScreenGeometry.Adjacent(here, Side.Left, [here, corner]).ShouldBeNull();
        ScreenGeometry.Adjacent(corner, Side.Right, [here, corner]).ShouldBeNull();
    }

    [Fact]
    public void Adjacent_SingleDisplay_IsNull()
    {
        ScreenGeometry.Adjacent(Primary, Side.Left, [Primary]).ShouldBeNull();
        ScreenGeometry.Adjacent(Primary, Side.Right, [Primary]).ShouldBeNull();
    }

    [Fact]
    public void Adjacent_PicksTheNearestDisplay_EvenWhenAFartherOneSharesMoreRows()
    {
        // Three in a row. The short middle display shares 601 rows with each outer one, which share all
        // 901 with each other, and it is still the one beside both: the far one sits behind it.
        var right = new Display(new Rect(0, 0, 1201, 901), new Rect(0, 0, 1201, 861), 96);
        var middle = new Display(new Rect(-1001, 0, 1001, 601), new Rect(-1001, 0, 1001, 561), 96);
        var far = new Display(new Rect(-2202, 0, 1201, 901), new Rect(-2202, 0, 1201, 861), 96);
        Display[] displays = [far, right, middle];

        ScreenGeometry.Adjacent(right, Side.Left, displays).ShouldBe(middle);
        ScreenGeometry.Adjacent(far, Side.Right, displays).ShouldBe(middle);
    }

    [Fact]
    public void Adjacent_AStackedPair_PicksTheOneSharingMoreRows()
    {
        // The mixed-DPI desk from Neighbor_MixedDpiDisplays_OrdersByFrameNotScale. The laptop's right
        // edge touches both of the stacked pair, but it shares 1440 rows with the lower one and only
        // 200 with the upper, which reading order would have picked.
        var far = new Display(new Rect(-5000, -200, 1920, 1080), new Rect(-5000, -200, 1920, 1032), 96);
        var laptop = new Display(new Rect(-3080, -200, 3072, 1920), new Rect(-3080, -200, 3072, 1872), 192);
        var upper = new Display(new Rect(-8, -1440, 2560, 1440), new Rect(-8, -1440, 2560, 1392), 96);
        var lower = new Display(new Rect(-8, 0, 2560, 1440), new Rect(-8, 0, 2560, 1392), 144);
        Display[] displays = [lower, laptop, upper, far];

        ScreenGeometry.Adjacent(laptop, Side.Right, displays).ShouldBe(lower);
        ScreenGeometry.Adjacent(upper, Side.Left, displays).ShouldBe(laptop);
        ScreenGeometry.Adjacent(laptop, Side.Left, displays).ShouldBe(far);
    }

    [Fact]
    public void Adjacent_EqualSharedRows_PicksTheUpperDisplay_WhateverTheListOrder()
    {
        // 451 rows each. Without a tie-break the answer would follow the order Windows happens to list
        // the monitors in, which can change between boots.
        var middle = new Display(new Rect(0, 0, 1201, 902), new Rect(0, 0, 1201, 862), 96);
        var upper = new Display(new Rect(1201, -550, 1001, 1001), new Rect(1201, -550, 1001, 961), 96);
        var lower = new Display(new Rect(1201, 451, 1001, 1001), new Rect(1201, 451, 1001, 961), 96);

        ScreenGeometry.Adjacent(middle, Side.Right, [middle, lower, upper]).ShouldBe(upper);
        ScreenGeometry.Adjacent(middle, Side.Right, [middle, upper, lower]).ShouldBe(upper);
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
