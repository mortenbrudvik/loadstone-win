using Loadstone.Core.Drag;
using Loadstone.Core.Geometry;
using Shouldly;
using Xunit;

namespace Loadstone.Core.Tests.Drag;

public class DragSnapTrackerTests
{
    private static readonly Display Primary = new(new Rect(0, 0, 1201, 901), new Rect(0, 0, 1201, 861), 96);
    private static readonly Display Right = new(new Rect(1201, 0, 1000, 700), new Rect(1201, 0, 1000, 660), 96);
    private static readonly Display[] Displays = [Primary, Right];
    private static readonly Rect Baseline = new(300, 300, 401, 301);
    private static readonly Point Middle = new(600, 450);
    private static readonly Point LeftEdge = new(3, 450);

    private sealed class Window;

    private static DragSnapTracker<Window> Begun(Window window)
    {
        var tracker = new DragSnapTracker<Window>();
        tracker.Begin(window, Baseline, Displays);
        return tracker;
    }

    [Fact]
    public void Update_BeforeTheWindowMoves_ShowsNoPreview_EvenInAZone()
    {
        var tracker = Begun(new Window());
        tracker.Update(LeftEdge, Baseline).ShouldBeNull();
    }

    [Fact]
    public void Update_OnceTheOriginChanges_Arms_AndReportsTheZoneUnderTheCursor()
    {
        var tracker = Begun(new Window());
        tracker.Update(Middle, Baseline with { X = 310 }).ShouldBeNull();
        tracker.Update(LeftEdge, Baseline with { X = 3 }).ShouldBe(new SnapTarget(Tile.LeftHalf, Primary));
        tracker.Update(Middle, Baseline with { X = 500 }).ShouldBeNull();
    }

    [Fact]
    public void Update_NullFrame_ChangesNothing()
    {
        var tracker = Begun(new Window());
        tracker.Update(LeftEdge, null).ShouldBeNull();
        tracker.Update(LeftEdge, Baseline with { X = 3 }).ShouldBe(new SnapTarget(Tile.LeftHalf, Primary));
    }

    [Fact]
    public void Update_SizeChange_DisarmsForTheRestOfTheDrag()
    {
        var tracker = Begun(new Window());
        tracker.Update(LeftEdge, Baseline with { Width = 500 }).ShouldBeNull();
        tracker.Update(LeftEdge, Baseline with { X = 3 }).ShouldBeNull();
        tracker.End(LeftEdge, Baseline with { X = 3 }).ShouldBeNull();
        tracker.IsDragging.ShouldBeFalse();
    }

    [Fact]
    public void Update_ResolvesTheZoneOnTheDisplayUnderTheCursor()
    {
        var tracker = Begun(new Window());
        var cursorOnRightDisplay = new Point(2195, 300);
        tracker.Update(cursorOnRightDisplay, Baseline with { X = 1800 }).ShouldBe(new SnapTarget(Tile.RightHalf, Right));
    }

    [Fact]
    public void End_InAZone_AfterArming_CommitsTheWindowAndTarget()
    {
        var window = new Window();
        var tracker = Begun(window);
        tracker.Update(LeftEdge, Baseline with { X = 3 });
        var result = tracker.End(LeftEdge, Baseline with { X = 3 });
        result.ShouldNotBeNull();
        result.Value.Window.ShouldBeSameAs(window);
        result.Value.Target.ShouldBe(new SnapTarget(Tile.LeftHalf, Primary));
        tracker.IsDragging.ShouldBeFalse();
        tracker.Window.ShouldBeNull();
    }

    [Fact]
    public void End_OutlineDrag_FrameMovesOnlyAtTheEnd_StillCommits()
    {
        var window = new Window();
        var tracker = Begun(window);
        tracker.Update(LeftEdge, Baseline).ShouldBeNull();
        tracker.End(LeftEdge, Baseline with { X = 3 })!.Value.Target.Tile.ShouldBe(Tile.LeftHalf);
    }

    [Fact]
    public void End_FrameEqualToBaseline_CommitsNothing()
    {
        // Escape cancels the OS move loop and puts the window back; a drag with no net movement looks the same.
        var tracker = Begun(new Window());
        tracker.Update(LeftEdge, Baseline with { X = 3 });
        tracker.End(LeftEdge, Baseline).ShouldBeNull();
    }

    [Fact]
    public void End_OutsideEveryZone_CommitsNothing()
    {
        var tracker = Begun(new Window());
        tracker.Update(LeftEdge, Baseline with { X = 3 });
        tracker.End(Middle, Baseline with { X = 3 }).ShouldBeNull();
    }

    [Fact]
    public void End_NullFrame_CommitsNothing()
    {
        var tracker = Begun(new Window());
        tracker.Update(LeftEdge, Baseline with { X = 3 });
        tracker.End(LeftEdge, null).ShouldBeNull();
    }

    [Fact]
    public void End_WhenIdle_IsNull()
    {
        new DragSnapTracker<Window>().End(LeftEdge, Baseline).ShouldBeNull();
    }

    [Fact]
    public void Begin_ReplacesAnEarlierDrag()
    {
        var first = new Window();
        var second = new Window();
        var tracker = Begun(first);
        tracker.Update(LeftEdge, Baseline with { X = 3 });
        tracker.Begin(second, Baseline, Displays);
        tracker.Window.ShouldBeSameAs(second);
        tracker.Update(LeftEdge, Baseline).ShouldBeNull();
    }

    [Fact]
    public void Reset_ReturnsToIdle()
    {
        var tracker = Begun(new Window());
        tracker.Reset();
        tracker.IsDragging.ShouldBeFalse();
        tracker.Update(LeftEdge, Baseline with { X = 3 }).ShouldBeNull();
    }
}
