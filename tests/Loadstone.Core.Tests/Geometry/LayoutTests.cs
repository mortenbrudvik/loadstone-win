using Loadstone.Core.Geometry;
using Shouldly;
using Xunit;

namespace Loadstone.Core.Tests.Geometry;

public class LayoutTests
{
    private static readonly Rect WorkArea = new(0, 0, 1201, 901);

    [Fact]
    public void Centered_KeepsSizeAndCenters()
    {
        Layout.Centered(new Rect(10, 10, 401, 301), WorkArea).ShouldBe(new Rect(400, 300, 401, 301));
    }

    [Fact]
    public void Centered_ClampsToWorkArea()
    {
        Layout.Centered(new Rect(0, 0, 5000, 200), WorkArea).ShouldBe(new Rect(0, 350, 1201, 200));
    }

    [Fact]
    public void Mapped_KeepsAHalfAHalf()
    {
        var source = new Rect(0, 0, 1201, 901);
        var destination = new Rect(2000, -100, 1000, 700);
        var leftHalf = Tile.LeftHalf.Frame(source);
        Layout.Mapped(leftHalf, source, destination).ShouldBe(new Rect(2000, -100, 500, 700));
    }

    [Fact]
    public void Mapped_KeepsRelativeOrigin()
    {
        var source = new Rect(0, 0, 1000, 1000);
        var destination = new Rect(0, 2000, 500, 500);
        Layout.Mapped(new Rect(100, 200, 300, 400), source, destination).ShouldBe(new Rect(50, 2100, 150, 200));
    }

    [Fact]
    public void Mapped_DegenerateSource_YieldsDestination()
    {
        var destination = new Rect(1, 2, 3, 4);
        Layout.Mapped(new Rect(0, 0, 10, 10), new Rect(0, 0, 0, 10), destination).ShouldBe(destination);
    }
}
