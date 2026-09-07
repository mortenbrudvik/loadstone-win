using Loadstone.Core.Geometry;
using Shouldly;
using Xunit;

namespace Loadstone.Core.Tests.Geometry;

public class TileFrameTests
{
    // 1201 by 901 divides by neither 2 nor 3, and the origin is negative and odd on purpose.
    private static readonly Rect WorkArea = new(-1920, 37, 1201, 901);

    [Theory]
    [InlineData(Tile.LeftHalf, -1920, 37, 600, 901)]
    [InlineData(Tile.RightHalf, -1320, 37, 601, 901)]
    [InlineData(Tile.TopHalf, -1920, 37, 1201, 450)]
    [InlineData(Tile.BottomHalf, -1920, 487, 1201, 451)]
    [InlineData(Tile.TopLeft, -1920, 37, 600, 450)]
    [InlineData(Tile.TopRight, -1320, 37, 601, 450)]
    [InlineData(Tile.BottomLeft, -1920, 487, 600, 451)]
    [InlineData(Tile.BottomRight, -1320, 487, 601, 451)]
    [InlineData(Tile.LeftThird, -1920, 37, 400, 901)]
    [InlineData(Tile.CenterThird, -1520, 37, 400, 901)]
    [InlineData(Tile.RightThird, -1120, 37, 401, 901)]
    [InlineData(Tile.LeftTwoThirds, -1920, 37, 800, 901)]
    [InlineData(Tile.RightTwoThirds, -1520, 37, 801, 901)]
    [InlineData(Tile.Maximize, -1920, 37, 1201, 901)]
    public void Frame_LaysTileIntoWorkArea(Tile tile, int x, int y, int width, int height)
    {
        tile.Frame(WorkArea).ShouldBe(new Rect(x, y, width, height));
    }

    [Theory]
    [InlineData(Tile.LeftHalf, Tile.RightHalf)]
    [InlineData(Tile.TopHalf, Tile.BottomHalf)]
    [InlineData(Tile.LeftTwoThirds, Tile.RightThird)]
    [InlineData(Tile.LeftThird, Tile.RightTwoThirds)]
    public void Frame_ComplementaryTilesMeetExactly(Tile first, Tile second)
    {
        var a = first.Frame(WorkArea);
        var b = second.Frame(WorkArea);
        var horizontal = a.Top == b.Top;
        if (horizontal)
        {
            a.Right.ShouldBe(b.Left);
            (a.Width + b.Width).ShouldBe(WorkArea.Width);
        }
        else
        {
            a.Bottom.ShouldBe(b.Top);
            (a.Height + b.Height).ShouldBe(WorkArea.Height);
        }
    }

    [Fact]
    public void Frame_ThirdsTileTheWholeWidth()
    {
        var left = Tile.LeftThird.Frame(WorkArea);
        var center = Tile.CenterThird.Frame(WorkArea);
        var right = Tile.RightThird.Frame(WorkArea);
        left.Right.ShouldBe(center.Left);
        center.Right.ShouldBe(right.Left);
        right.Right.ShouldBe(WorkArea.Right);
    }

    [Fact]
    public void Title_CoversEveryTile()
    {
        foreach (var tile in Enum.GetValues<Tile>())
        {
            tile.Title().ShouldNotBeNullOrWhiteSpace();
        }

        Tile.TopLeft.Title().ShouldBe("Top Left Corner");
        Tile.LeftTwoThirds.Title().ShouldBe("Left Two Thirds");
    }
}
