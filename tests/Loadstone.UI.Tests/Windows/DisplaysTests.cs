using Loadstone.UI.Windows;
using Shouldly;
using Xunit;

namespace Loadstone.UI.Tests.Windows;

public class DisplaysTests
{
    [Fact]
    public void Snapshot_ReturnsThePrimaryDisplayFirst_WithSaneGeometry()
    {
        var displays = Displays.Snapshot();
        displays.Count.ShouldBeGreaterThan(0);

        var primary = displays[0];
        primary.Frame.Contains(new Loadstone.Core.Geometry.Point(0, 0)).ShouldBeTrue("the primary display's frame starts at the origin");
        primary.Dpi.ShouldBeGreaterThanOrEqualTo(96);

        foreach (var display in displays)
        {
            display.WorkArea.Width.ShouldBeLessThanOrEqualTo(display.Frame.Width);
            display.WorkArea.Height.ShouldBeLessThanOrEqualTo(display.Frame.Height);
            display.WorkArea.Left.ShouldBeGreaterThanOrEqualTo(display.Frame.Left);
            display.WorkArea.Top.ShouldBeGreaterThanOrEqualTo(display.Frame.Top);
        }
    }
}
