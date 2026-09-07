using Loadstone.Core.Geometry;
using Shouldly;
using Xunit;

namespace Loadstone.Core.Tests.Geometry;

public class SnapZonesTests
{
    private static readonly Rect Landscape = new(0, 0, 1201, 901);
    private static readonly Rect Portrait = new(3000, -50, 901, 1201);

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
    public void TileAt_Landscape_MapsBandsAndCorners(int x, int y, Tile expected)
    {
        SnapZones.TileAt(new Point(x, y), Landscape, 1.0).ShouldBe(expected);
    }

    [Theory]
    [InlineData(600, 450)]
    [InlineData(17, 500)]
    [InlineData(600, 883)]
    public void TileAt_Landscape_InsideTheFrameAwayFromEdges_IsNull(int x, int y)
    {
        SnapZones.TileAt(new Point(x, y), Landscape, 1.0).ShouldBeNull();
    }

    [Fact]
    public void TileAt_OvershootPastAnOuterEdge_StillCounts()
    {
        SnapZones.TileAt(new Point(-16, 500), Landscape, 1.0).ShouldBe(Tile.LeftHalf);
        SnapZones.TileAt(new Point(-17, 500), Landscape, 1.0).ShouldBeNull();
        SnapZones.TileAt(new Point(600, -16), Landscape, 1.0).ShouldBe(Tile.Maximize);
        SnapZones.TileAt(new Point(1216, 500), Landscape, 1.0).ShouldBe(Tile.RightHalf);
    }

    [Fact]
    public void TileAt_CornerBeatsTopAndSideBands()
    {
        SnapZones.TileAt(new Point(0, 0), Landscape, 1.0).ShouldBe(Tile.TopLeft);
        SnapZones.TileAt(new Point(1200, 900), Landscape, 1.0).ShouldBe(Tile.BottomRight);
    }

    [Fact]
    public void TileAt_ScalesBandsWithDpi()
    {
        // At 200 percent the edge band is 32 px and the corner square 280 px.
        SnapZones.TileAt(new Point(30, 500), Landscape, 2.0).ShouldBe(Tile.LeftHalf);
        SnapZones.TileAt(new Point(33, 500), Landscape, 2.0).ShouldBeNull();
        SnapZones.TileAt(new Point(279, 279), Landscape, 2.0).ShouldBe(Tile.TopLeft);
        SnapZones.TileAt(new Point(281, 10), Landscape, 2.0).ShouldBe(Tile.Maximize);
    }

    [Theory]
    [InlineData(3000, 100, Tile.TopLeft)]
    [InlineData(3000, 550, Tile.LeftThird)]
    [InlineData(3000, 1100, Tile.BottomLeft)]
    [InlineData(3900, 100, Tile.TopRight)]
    [InlineData(3900, 550, Tile.RightThird)]
    [InlineData(3900, 1100, Tile.BottomRight)]
    [InlineData(3300, 1150, Tile.LeftHalf)]
    [InlineData(3600, 1150, Tile.RightHalf)]
    [InlineData(3450, -50, Tile.Maximize)]
    public void TileAt_Portrait_SplitsSidesAlongTheHeightAndBottomIntoHalves(int x, int y, Tile expected)
    {
        SnapZones.TileAt(new Point(x, y), Portrait, 1.0).ShouldBe(expected);
    }
}
