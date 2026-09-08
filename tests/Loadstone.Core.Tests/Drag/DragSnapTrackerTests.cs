using Loadstone.Core.Drag;
using Loadstone.Core.Geometry;
using Shouldly;
using Xunit;

namespace Loadstone.Core.Tests.Drag;

public class DragSnapTrackerTests
{
    private static readonly Display Primary = new(new Rect(0, 0, 1201, 901), new Rect(0, 0, 1201, 861), 96);
    private static readonly Display Right = new(new Rect(1201, 0, 1000, 700), new Rect(1201, 0, 1000, 660), 96);

    /// <summary>The right-hand monitor at 150 percent, so a drag onto it crosses a DPI boundary.</summary>
    private static readonly Display RightAt150 = new(new Rect(1201, 0, 1000, 700), new Rect(1201, 0, 1000, 660), 144);

    private static readonly Display[] Displays = [Primary, Right];
    private static readonly Display[] MixedDpi = [Primary, RightAt150];
    private static readonly Rect Baseline = new(300, 300, 401, 301);
    private static readonly Point Middle = new(600, 450);
    private static readonly Point LeftEdge = new(3, 450);

    /// <summary>Inside the right-hand monitor's right band, clear of both corner squares at 150 percent too.</summary>
    private static readonly Point RightEdgeOfRightDisplay = new(2195, 300);

    /// <summary>
    /// The frame Windows leaves behind when it rescales <see cref="Baseline"/> onto a 150 percent
    /// monitor: 401 x 301 taken through the 144/96 ratio, each edge rounded on its own, so 601 x 451
    /// rather than the 602 x 452 a single rounding would give. Both are inside the tracker's
    /// tolerance, which is the point - the rescale test must not depend on rounding one way.
    /// </summary>
    private static readonly Rect RescaledOnRight = new(1500, 200, 601, 451);

    /// <summary>A small window on the primary display, used for the resize that drags its own centre across the boundary.</summary>
    private static readonly Rect SmallOnPrimary = new(900, 300, 200, 200);

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
        // The general rule: a size change is the user resizing and kills the drag, because dragging a
        // window's edge across a screen edge must never snap it. The single exception - the size
        // Windows itself produces on WM_DPICHANGED - is pinned by the DPI-ratio tests below, and the
        // rule must be read the other way round: a size change is a resize until the ratio says it is
        // not, never a resize only while the DPI happens to match.
        var tracker = Begun(new Window());
        tracker.Update(LeftEdge, Baseline with { Width = 500 }).ShouldBeNull();
        tracker.Update(LeftEdge, Baseline with { X = 3 }).ShouldBeNull();
        tracker.End(LeftEdge, Baseline with { X = 3 }).ShouldBeNull();
        tracker.IsDragging.ShouldBeFalse();
    }

    [Fact]
    public void Update_ResizeWhoseOwnCentreCrossesADpiBoundary_Disarms_AndCommitsNothing()
    {
        // A resize can carry the frame's centre onto another monitor by itself: grab this window's
        // right edge and pull it to x = 2190 and the centre lands on the 150 percent display. "The DPI
        // under the centre changed" therefore cannot mean "Windows rescaled it" - 200 px through the
        // ratio would be 300, not 1290. The origin never moves, so nothing previews either; the user
        // just sees a resize, and the drop must leave it alone instead of snapping RightHalf.
        var widened = SmallOnPrimary with { Width = 1290 };
        ScreenGeometry.DisplayContaining(SmallOnPrimary, MixedDpi).ShouldBe(Primary);
        ScreenGeometry.DisplayContaining(widened, MixedDpi).ShouldBe(RightAt150);

        var tracker = new DragSnapTracker<Window>();
        tracker.Begin(new Window(), SmallOnPrimary, MixedDpi);
        tracker.Update(RightEdgeOfRightDisplay, widened).ShouldBeNull();
        tracker.End(RightEdgeOfRightDisplay, widened).ShouldBeNull();
        tracker.IsDragging.ShouldBeFalse();

        // And with no sample in between, so End's own size test is the one doing the work.
        var unsampled = new DragSnapTracker<Window>();
        unsampled.Begin(new Window(), SmallOnPrimary, MixedDpi);
        unsampled.End(RightEdgeOfRightDisplay, widened).ShouldBeNull();
    }

    [Fact]
    public void Update_SizeChangeWhenTheDragBeganOffEveryDisplay_Disarms()
    {
        // Begin records DPI 0 when no display holds the frame's centre - a window pushed past an edge,
        // or left behind by a monitor disconnect. 0 is "unknown", not a scaling factor: the resize
        // below lands the centre back on the primary at 96, and treating 96 != 0 as a DPI change let a
        // plain resize on a single-DPI desk stay armed and snap LeftHalf on the drop.
        var offEveryDisplay = new Rect(-500, 300, 401, 301);
        ScreenGeometry.DisplayContaining(offEveryDisplay, Displays).ShouldBeNull();

        var widened = offEveryDisplay with { Width = 1000 };
        ScreenGeometry.DisplayContaining(widened, Displays).ShouldBe(Primary);

        var tracker = new DragSnapTracker<Window>();
        tracker.Begin(new Window(), offEveryDisplay, Displays);
        tracker.Update(LeftEdge, widened).ShouldBeNull();
        tracker.End(LeftEdge, widened).ShouldBeNull();
    }

    [Fact]
    public void Update_SizeChangeMatchingTheDpiRatio_StaysArmedAndCommits()
    {
        // The symptom the DPI exception exists for: dragging a per-monitor-aware window (Explorer, Edge,
        // Terminal) from a 100 percent monitor to a 150 percent one snapped nothing at all. Windows
        // resizes the window itself through WM_DPICHANGED mid-drag, the tracker read that as a resize,
        // and the drag was dead from there on. Here the new size is the old one through the ratio, which
        // is what tells this apart from the resize above, so the drag stays armed and still snaps.
        var window = new Window();
        var tracker = new DragSnapTracker<Window>();
        tracker.Begin(window, Baseline, MixedDpi);

        tracker.Update(RightEdgeOfRightDisplay, RescaledOnRight)
            .ShouldBe(new SnapTarget(Tile.RightHalf, RightAt150));

        var result = tracker.End(RightEdgeOfRightDisplay, RescaledOnRight);
        result.ShouldNotBeNull();
        result.Value.Window.ShouldBeSameAs(window);
        result.Value.Target.ShouldBe(new SnapTarget(Tile.RightHalf, RightAt150));
    }

    [Fact]
    public void Update_ResizeAfterADpiChange_StillDisarms()
    {
        // The rescaled size becomes the size to expect, not a licence to ignore every later resize.
        var tracker = new DragSnapTracker<Window>();
        tracker.Begin(new Window(), Baseline, MixedDpi);
        tracker.Update(RightEdgeOfRightDisplay, RescaledOnRight).ShouldNotBeNull();

        tracker.Update(RightEdgeOfRightDisplay, RescaledOnRight with { Width = 700 }).ShouldBeNull();
        tracker.End(RightEdgeOfRightDisplay, RescaledOnRight with { Width = 700 }).ShouldBeNull();
    }

    [Fact]
    public void Update_NullCursor_ShowsNoPreview_AndCommitsNothing()
    {
        // GetCursorPos fails while another desktop holds the input (a UAC prompt, the lock screen), and
        // the (0,0) it leaves behind is a real point inside the primary display: read as a sample it
        // would preview the top-left corner and drop the window there. A missing sample must do nothing
        // - and must not disarm, because the very next sample is usually good.
        var tracker = Begun(new Window());
        tracker.Update(null, Baseline with { X = 3 }).ShouldBeNull();
        tracker.Update(LeftEdge, Baseline with { X = 3 }).ShouldBe(new SnapTarget(Tile.LeftHalf, Primary));
        tracker.End(null, Baseline with { X = 3 }).ShouldBeNull();
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
