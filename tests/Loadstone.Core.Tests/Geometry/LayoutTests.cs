using Loadstone.Core.Geometry;
using Shouldly;
using Xunit;

namespace Loadstone.Core.Tests.Geometry;

public class LayoutTests
{
    // Odd sizes and a negative origin on purpose: 1201 by 901 halves unevenly, and a work area at (0,0)
    // cannot tell "centred in the work area" from "centred in the screen that happens to start at zero".
    private static readonly Rect WorkArea = new(-1920, 37, 1201, 901);

    // A 2560x1440 at 100 percent to the left of a 4K at 150 percent: the same fraction of a screen is a
    // different number of physical pixels on each, which is the whole hazard Mapped exists to absorb.
    private static readonly Display Standard = new(new Rect(-2560, 0, 2560, 1440), new Rect(-2560, 0, 2560, 1392), 96);
    private static readonly Display Scaled = new(new Rect(0, 0, 3840, 2160), new Rect(0, 0, 3840, 2088), 144);

    [Fact]
    public void Centered_KeepsSizeAndCenters()
    {
        Layout.Centered(new Rect(10, 10, 401, 301), WorkArea).ShouldBe(new Rect(-1520, 337, 401, 301));
    }

    [Fact]
    public void Centered_OddRemainder_LeavesTheSparePixelRightAndBelow()
    {
        // 1201 - 400 is 801, so the gaps are 400 on the left and 401 on the right; 901 - 300 is 601, so
        // 300 above and 301 below.
        Layout.Centered(new Rect(10, 10, 400, 300), WorkArea).ShouldBe(new Rect(-1520, 337, 400, 300));
    }

    [Fact]
    public void Centered_ClampsToWorkArea()
    {
        Layout.Centered(new Rect(0, 0, 5000, 200), WorkArea).ShouldBe(new Rect(-1920, 387, 1201, 200));
    }

    [Fact]
    public void CenteredIn_WindowThatFits_MatchesCentered()
    {
        // The two agree whenever nothing needs clamping; the pair below is where they part company.
        Layout.CenteredIn(new Rect(10, 10, 401, 301), WorkArea).ShouldBe(new Rect(-1520, 337, 401, 301));
    }

    [Fact]
    public void CenteredIn_OversizedWindow_KeepsItsSizeAndOverhangs()
    {
        // A fixed-size window (no WS_THICKFRAME) cannot be resized into the work area, so it keeps its
        // 1600x1000 and overhangs: 199 px on the left and 200 on the right, 49 above and 50 below, the
        // spare pixel of an odd gap going right and down (spec 5.2, "Win32Window"). Centered is the one
        // that clamps, and clamping here would hand SetWindowPos a size the window refuses.
        Layout.CenteredIn(new Rect(0, 0, 1600, 1000), WorkArea).ShouldBe(new Rect(-2119, -12, 1600, 1000));
        Layout.Centered(new Rect(0, 0, 1600, 1000), WorkArea).ShouldBe(new Rect(-1920, 37, 1201, 901));
    }

    [Fact]
    public void Mapped_KeepsAHalfAHalf()
    {
        // The left half of the work area, written out rather than taken from Tile.Frame so this test
        // still fails if Layout stops halving and Tile keeps doing it.
        var leftHalf = new Rect(-1920, 37, 600, 901);
        Layout.Mapped(leftHalf, WorkArea, new Rect(2000, -100, 1000, 700)).ShouldBe(new Rect(2000, -100, 500, 700));
    }

    [Fact]
    public void Mapped_KeepsRelativeOrigin()
    {
        var source = new Rect(0, 0, 1000, 1000);
        var destination = new Rect(0, 2000, 500, 500);
        Layout.Mapped(new Rect(100, 200, 300, 400), source, destination).ShouldBe(new Rect(50, 2100, 150, 200));
    }

    [Fact]
    public void Mapped_AcrossADpiChange_KeepsTheSameHalf()
    {
        // Next Display from the 100 percent screen onto the 150 percent one: the left half has to arrive
        // as that screen's left half, 1920x2088 physical pixels, not as the 1280x1392 it occupied before.
        // Mapped works in fractions of the work area, so the scale change never appears in the arithmetic.
        var leftHalf = new Rect(-2560, 0, 1280, 1392);
        var mappedLeft = Layout.Mapped(leftHalf, Standard.WorkArea, Scaled.WorkArea);
        mappedLeft.ShouldBe(new Rect(0, 0, 1920, 2088));

        var rightHalf = new Rect(-1280, 0, 1280, 1392);
        Layout.Mapped(rightHalf, Standard.WorkArea, Scaled.WorkArea).ShouldBe(new Rect(1920, 0, 1920, 2088));

        // And back: the trip is lossless on halves, so Previous Display undoes Next Display.
        Layout.Mapped(mappedLeft, Scaled.WorkArea, Standard.WorkArea).ShouldBe(leftHalf);
    }

    [Fact]
    public void Mapped_WindowOverhangingTheSourceWorkArea_KeepsTheOverhang()
    {
        // A 1080-tall window on a 1040-tall work area has its bottom 40 px under the taskbar, and a
        // window nudged 100 px past the left edge is where its owner put it. Both overhangs are the
        // user's, so they travel with the window: a clamp here would silently move and resize perfectly
        // ordinary windows every time Next Display was pressed.
        var source = new Rect(0, 0, 1920, 1040);
        var destination = new Rect(1920, 0, 1920, 1040);

        Layout.Mapped(new Rect(100, 100, 800, 980), source, destination).ShouldBe(new Rect(2020, 100, 800, 980));
        Layout.Mapped(new Rect(-100, 100, 800, 900), source, destination).ShouldBe(new Rect(1820, 100, 800, 900));
    }

    [Fact]
    public void Mapped_WindowLargerThanTheSource_StaysProportionallyLarger()
    {
        // Over three work areas wide and tall on the source, the same on the destination. Fitting it
        // would be a resize nobody asked for, and Mapped's one job is to preserve the proportions.
        Layout.Mapped(new Rect(0, 0, 4000, 3000), WorkArea, new Rect(2000, -100, 1000, 700))
            .ShouldBe(new Rect(3599, -129, 3331, 2331));
    }

    [Fact]
    public void Mapped_FrameOffEveryDisplay_MapsItJustAsFaithfully()
    {
        // A window left at (-9000,-9000) by a disconnected monitor is on no display, so its offsets mean
        // nothing measured against any work area and mapping them lands it somewhere just as invisible.
        // Mapped is honest about that rather than clamping every caller's window to hide it: only
        // WindowDirector knows the frame is unrelated to the source, and it centres such a window on the
        // neighbour instead of coming here (see WindowDirectorTests).
        Layout.Mapped(new Rect(-9000, -9000, 800, 600), WorkArea, new Rect(2000, -100, 1000, 700))
            .ShouldBe(new Rect(-3895, -7121, 666, 466));
    }

    [Fact]
    public void Mapped_DegenerateSource_YieldsDestination()
    {
        var destination = new Rect(1, 2, 3, 4);
        Layout.Mapped(new Rect(0, 0, 10, 10), new Rect(0, 0, 0, 10), destination).ShouldBe(destination);
    }
}
