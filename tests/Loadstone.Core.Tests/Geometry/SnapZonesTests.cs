using Loadstone.Core.Geometry;
using Shouldly;
using Xunit;

namespace Loadstone.Core.Tests.Geometry;

public class SnapZonesTests
{
    // A negative origin on purpose: at (0,0) the fixture cannot tell x apart from x - Frame.Left, so an
    // origin-relative bug in fromLeft/fromTop would pass. The work area is 40 px shorter than the frame
    // so the taskbar strip the bottom band has to reach past exists in the fixture too.
    private static readonly Display Landscape = new(
        new Rect(-2000, -1000, 1201, 901),
        new Rect(-2000, -1000, 1201, 861),
        96);

    // The same monitor rectangle at 150 and 200 percent. Only the DPI differs, so any change in where
    // the zones start is the scaling and nothing else.
    private static readonly Display LandscapeAt144 = Landscape with { Dpi = 144 };
    private static readonly Display LandscapeAt192 = Landscape with { Dpi = 192 };

    private static readonly Display Portrait = new(
        new Rect(3000, -50, 901, 1201),
        new Rect(3000, -50, 901, 1161),
        96);

    // The cursor fromLeft px in from the frame's left edge and fromTop px down from its top edge.
    private static Point Cursor(Display display, int fromLeft, int fromTop) =>
        new(display.Frame.Left + fromLeft, display.Frame.Top + fromTop);

    [Theory]
    [InlineData(10, 10, Tile.TopLeft)]
    [InlineData(140, 140, Tile.TopLeft)]
    [InlineData(1190, 10, Tile.TopRight)]
    [InlineData(10, 890, Tile.BottomLeft)]
    [InlineData(1190, 890, Tile.BottomRight)]
    [InlineData(141, 10, Tile.Maximize)]
    [InlineData(600, 0, Tile.Maximize)]
    [InlineData(5, 500, Tile.LeftHalf)]
    [InlineData(0, 500, Tile.LeftHalf)]
    [InlineData(1195, 500, Tile.RightHalf)]
    [InlineData(1200, 500, Tile.RightHalf)]
    [InlineData(300, 895, Tile.LeftThird)]
    [InlineData(600, 895, Tile.CenterThird)]
    [InlineData(1000, 895, Tile.RightThird)]
    public void TileAt_Landscape_MapsBandsAndCorners(int fromLeft, int fromTop, Tile expected)
    {
        SnapZones.TileAt(Cursor(Landscape, fromLeft, fromTop), Landscape).ShouldBe(expected);
    }

    [Theory]
    [InlineData(600, 450)]
    [InlineData(17, 500)]
    [InlineData(600, 883)]
    public void TileAt_Landscape_InsideTheFrameAwayFromEdges_IsNull(int fromLeft, int fromTop)
    {
        SnapZones.TileAt(Cursor(Landscape, fromLeft, fromTop), Landscape).ShouldBeNull();
    }

    [Fact]
    public void TileAt_PointFromAnotherDisplaysCoordinates_IsNull()
    {
        // (5,5) is the top-left corner only of a display whose frame starts at the origin. Against this
        // frame it sits hundreds of pixels past the right edge, and a cursor on the neighbouring screen
        // must not arm a snap here.
        SnapZones.TileAt(new Point(5, 5), Landscape).ShouldBeNull();
        SnapZones.TileAt(new Point(-1995, -995), Landscape).ShouldBe(Tile.TopLeft);
    }

    [Fact]
    public void TileAt_OvershootPastAnOuterEdge_StillCounts()
    {
        SnapZones.TileAt(Cursor(Landscape, -16, 500), Landscape).ShouldBe(Tile.LeftHalf);
        SnapZones.TileAt(Cursor(Landscape, -17, 500), Landscape).ShouldBeNull();
        SnapZones.TileAt(Cursor(Landscape, 600, -16), Landscape).ShouldBe(Tile.Maximize);
        SnapZones.TileAt(Cursor(Landscape, 1216, 500), Landscape).ShouldBe(Tile.RightHalf);
    }

    [Fact]
    public void TileAt_CornerBeatsTopAndSideBands()
    {
        SnapZones.TileAt(Cursor(Landscape, 0, 0), Landscape).ShouldBe(Tile.TopLeft);
        SnapZones.TileAt(Cursor(Landscape, 1200, 900), Landscape).ShouldBe(Tile.BottomRight);
    }

    [Fact]
    public void TileAt_InTheTaskbarStrip_StillCounts()
    {
        // The bands are measured against the full monitor frame; only the tile that comes out of them is
        // laid into the work area. Measuring them against the work area would start the bottom band above
        // the taskbar, where the cursor never is at the end of a drag to the bottom edge.
        Landscape.WorkArea.Bottom.ShouldBeLessThan(Landscape.Frame.Bottom);
        SnapZones.TileAt(Cursor(Landscape, 600, 900), Landscape).ShouldBe(Tile.CenterThird);
    }

    [Fact]
    public void TileAt_HigherDpiDisplay_GetsProportionallyThickerBands()
    {
        // 16 px bands and 140 px corners are physical pixels at 96 DPI, so on the same rectangle at 144
        // DPI they must be 24 px and 210 px. Without the scaling the zones on a 150 percent screen are
        // two thirds of the size they feel on a 100 percent one, and the drag misses them.
        SnapZones.TileAt(Cursor(Landscape, 20, 450), Landscape).ShouldBeNull();
        SnapZones.TileAt(Cursor(LandscapeAt144, 20, 450), LandscapeAt144).ShouldBe(Tile.LeftHalf);
        SnapZones.TileAt(Cursor(LandscapeAt144, 24, 450), LandscapeAt144).ShouldBe(Tile.LeftHalf);
        SnapZones.TileAt(Cursor(LandscapeAt144, 25, 450), LandscapeAt144).ShouldBeNull();

        SnapZones.TileAt(Cursor(Landscape, 200, 200), Landscape).ShouldBeNull();
        SnapZones.TileAt(Cursor(LandscapeAt144, 200, 200), LandscapeAt144).ShouldBe(Tile.TopLeft);
        SnapZones.TileAt(Cursor(LandscapeAt144, 211, 211), LandscapeAt144).ShouldBeNull();
    }

    [Fact]
    public void TileAt_ScalesBandsWithDpi()
    {
        // At 200 percent the edge band is 32 px and the corner square 280 px.
        SnapZones.TileAt(Cursor(LandscapeAt192, 30, 500), LandscapeAt192).ShouldBe(Tile.LeftHalf);
        SnapZones.TileAt(Cursor(LandscapeAt192, 33, 500), LandscapeAt192).ShouldBeNull();
        SnapZones.TileAt(Cursor(LandscapeAt192, 279, 279), LandscapeAt192).ShouldBe(Tile.TopLeft);
        SnapZones.TileAt(Cursor(LandscapeAt192, 281, 10), LandscapeAt192).ShouldBe(Tile.Maximize);
    }

    [Theory]
    [InlineData(0, 150, Tile.TopLeft)]
    [InlineData(0, 600, Tile.LeftThird)]
    [InlineData(0, 1150, Tile.BottomLeft)]
    [InlineData(900, 150, Tile.TopRight)]
    [InlineData(900, 600, Tile.RightThird)]
    [InlineData(900, 1150, Tile.BottomRight)]
    [InlineData(300, 1200, Tile.LeftHalf)]
    [InlineData(600, 1200, Tile.RightHalf)]
    [InlineData(450, 0, Tile.Maximize)]
    public void TileAt_Portrait_SplitsSidesAlongTheHeightAndBottomIntoHalves(int fromLeft, int fromTop, Tile expected)
    {
        SnapZones.TileAt(Cursor(Portrait, fromLeft, fromTop), Portrait).ShouldBe(expected);
    }

    [Theory]
    [InlineData(450, 600)]
    [InlineData(20, 600)]
    [InlineData(450, 1180)]
    public void TileAt_Portrait_InsideTheFrameAwayFromEdges_IsNull(int fromLeft, int fromTop)
    {
        // The landscape counterpart of this existed and the portrait one did not, so the portrait branch
        // could have answered a tile for the whole middle of the screen without a test noticing.
        SnapZones.TileAt(Cursor(Portrait, fromLeft, fromTop), Portrait).ShouldBeNull();
    }
}
