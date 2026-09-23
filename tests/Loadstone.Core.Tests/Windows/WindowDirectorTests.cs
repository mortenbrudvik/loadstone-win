using Loadstone.Core.Commands;
using Loadstone.Core.Geometry;
using Loadstone.Core.Windows;
using Shouldly;
using Xunit;

namespace Loadstone.Core.Tests.Windows;

public class WindowDirectorTests
{
    private static readonly Display Primary = new(new Rect(0, 0, 1201, 901), new Rect(0, 0, 1201, 861), 96);
    private static readonly Display Secondary = new(new Rect(1201, -100, 1000, 700), new Rect(1201, -100, 1000, 700), 96);
    private static readonly Rect Start = new(100, 100, 501, 401);

    private readonly HashSet<WindowIdentity> _dead = [];
    private List<Display> _displays = [Primary, Secondary];

    private WindowDirector NewDirector(Func<bool>? restoreOneMoveAtATime = null) =>
        new(() => _displays, id => !_dead.Contains(id), restoreOneMoveAtATime);

    [Fact]
    public void Perform_Tile_LaysTileIntoTheWorkAreaOfTheWindowsDisplay()
    {
        var window = new FakeWindow(Start);
        NewDirector().Perform(WindowCommand.LeftHalf, window).ShouldBe(CommandOutcome.Moved);
        window.Frame.ShouldBe(Tile.LeftHalf.Frame(Primary.WorkArea));
    }

    [Fact]
    public void Perform_Center_KeepsSizeAndCenters()
    {
        var window = new FakeWindow(Start);
        NewDirector().Perform(WindowCommand.Center, window).ShouldBe(CommandOutcome.Moved);
        window.Frame.ShouldBe(Layout.Centered(Start, Primary.WorkArea));
    }

    [Fact]
    public void Perform_UnreadableFrame_DoesNothing()
    {
        var window = new FakeWindow(Start) { Frame = null };
        NewDirector().Perform(WindowCommand.LeftHalf, window).ShouldBe(CommandOutcome.FrameUnreadable);
        window.Writes.ShouldBeEmpty();
    }

    [Fact]
    public void Perform_ReadsTheFrameExactlyOnce()
    {
        // ReadFrame is a method because it is two or three Win32 calls and answers differently between
        // reads while the window is moving; every branch must work from the one sample taken up front.
        var window = new FakeWindow(Start);
        NewDirector().Perform(WindowCommand.NextDisplay, window).ShouldBe(CommandOutcome.Moved);
        window.Reads.ShouldBe(1);
    }

    [Fact]
    public void Perform_NoDisplays_ReportsNoDisplay()
    {
        _displays = [];
        var window = new FakeWindow(Start);
        NewDirector().Perform(WindowCommand.LeftHalf, window).ShouldBe(CommandOutcome.NoDisplay);
    }

    [Fact]
    public void Perform_WindowOffEveryDisplay_UsesThePrimaryDisplay()
    {
        var window = new FakeWindow(new Rect(-9000, -9000, 300, 200));
        NewDirector().Perform(WindowCommand.Maximize, window).ShouldBe(CommandOutcome.Moved);
        window.Frame.ShouldBe(Primary.WorkArea);
    }

    [Fact]
    public void Restore_ReturnsToTheFrameBeforeTheFirstCommand_Once()
    {
        var window = new FakeWindow(Start);
        var director = NewDirector();
        director.Perform(WindowCommand.LeftHalf, window);
        director.Perform(WindowCommand.TopRight, window);
        director.Perform(WindowCommand.Center, window);

        director.Perform(WindowCommand.Restore, window).ShouldBe(CommandOutcome.Moved);
        window.Frame.ShouldBe(Start);
        director.RememberedWindowCount.ShouldBe(0);

        director.Perform(WindowCommand.Restore, window).ShouldBe(CommandOutcome.NothingToRestore);
    }

    [Fact]
    public void Restore_NeverRecords_SoRestoreAfterRestoreHasNothing()
    {
        var window = new FakeWindow(Start);
        var director = NewDirector();
        director.Perform(WindowCommand.Restore, window).ShouldBe(CommandOutcome.NothingToRestore);
        window.Writes.ShouldBeEmpty();
        director.RememberedWindowCount.ShouldBe(0);
    }

    [Fact]
    public void Restore_RefusedWrite_KeepsTheEntryForALaterAttempt()
    {
        var window = new FakeWindow(Start);
        var director = NewDirector();
        director.Perform(WindowCommand.LeftHalf, window);

        window.NextOutcome = CommandOutcome.Rejected(5);
        director.Perform(WindowCommand.Restore, window).ShouldBe(CommandOutcome.Rejected(5));
        director.RememberedWindowCount.ShouldBe(1);

        window.NextOutcome = CommandOutcome.Moved;
        director.Perform(WindowCommand.Restore, window).ShouldBe(CommandOutcome.Moved);
        window.Frame.ShouldBe(Start);
    }

    [Fact]
    public void RefusedFirstWrite_LeavesNothingToRestore()
    {
        var window = new FakeWindow(Start) { NextOutcome = CommandOutcome.Rejected(5) };
        var director = NewDirector();
        director.Perform(WindowCommand.LeftHalf, window).ShouldBe(CommandOutcome.Rejected(5));
        director.RememberedWindowCount.ShouldBe(0);
    }

    [Fact]
    public void DeadIdentities_ArePrunedBeforeEveryCommand()
    {
        var gone = new FakeWindow(Start, hwnd: 7, pid: 7);
        var alive = new FakeWindow(Start, hwnd: 8, pid: 8);
        var director = NewDirector();
        director.Perform(WindowCommand.LeftHalf, gone);
        director.Perform(WindowCommand.LeftHalf, alive);
        director.RememberedWindowCount.ShouldBe(2);

        _dead.Add(gone.Identity);
        director.Perform(WindowCommand.Center, alive);
        director.RememberedWindowCount.ShouldBe(1);
    }

    [Fact]
    public void DeadIdentities_ArePrunedBeforeASnapToo()
    {
        // Prune runs at the top of Snap as well as Perform, and a drag-to-snap session can go a whole
        // day without a hotkey command: without this the ledger grew a frame per closed window.
        var gone = new FakeWindow(Start, hwnd: 7, pid: 7);
        var alive = new FakeWindow(Start, hwnd: 8, pid: 8);
        var director = NewDirector();
        director.Perform(WindowCommand.LeftHalf, gone);
        director.Perform(WindowCommand.LeftHalf, alive);
        director.RememberedWindowCount.ShouldBe(2);

        _dead.Add(gone.Identity);
        director.Snap(Tile.RightHalf, alive, Secondary).ShouldBe(CommandOutcome.Moved);
        director.RememberedWindowCount.ShouldBe(1);
    }

    [Fact]
    public void Restore_AfterTheHandleIsRecycled_HasNothingToRestore()
    {
        // The symptom this guards: Windows hands a closed window's hwnd to the next window, and a
        // Restore aimed at that stranger teleported it to the dead window's frame. The pid in
        // WindowIdentity is the whole defence, so the ledger must miss on a same-hwnd/other-pid window.
        var tiled = new FakeWindow(Start, hwnd: 7, pid: 7);
        var director = NewDirector();
        director.Perform(WindowCommand.LeftHalf, tiled).ShouldBe(CommandOutcome.Moved);

        _dead.Add(tiled.Identity);
        var stranger = new FakeWindow(new Rect(40, 60, 300, 200), hwnd: 7, pid: 9);
        director.Perform(WindowCommand.Restore, stranger).ShouldBe(CommandOutcome.NothingToRestore);
        stranger.Writes.ShouldBeEmpty();
        stranger.Frame.ShouldBe(new Rect(40, 60, 300, 200));
    }

    [Fact]
    public void Restore_OneMoveAtATime_StepsBackThroughEachAcceptedMove()
    {
        var oneMove = true;
        var window = new FakeWindow(Start);
        var director = NewDirector(() => oneMove);
        var left = Tile.LeftHalf.Frame(Primary.WorkArea);
        var topRight = Tile.TopRight.Frame(Primary.WorkArea);

        director.Perform(WindowCommand.LeftHalf, window);
        director.Perform(WindowCommand.TopRight, window);
        director.Perform(WindowCommand.Center, window);

        director.Perform(WindowCommand.Restore, window).ShouldBe(CommandOutcome.Moved);
        window.Frame.ShouldBe(topRight);

        director.Perform(WindowCommand.Restore, window).ShouldBe(CommandOutcome.Moved);
        window.Frame.ShouldBe(left);

        director.Perform(WindowCommand.Restore, window).ShouldBe(CommandOutcome.Moved);
        window.Frame.ShouldBe(Start);
        director.RememberedWindowCount.ShouldBe(0);

        director.Perform(WindowCommand.Restore, window).ShouldBe(CommandOutcome.NothingToRestore);
    }

    [Fact]
    public void Restore_OneMoveAtATime_IncludesADragSnap()
    {
        var window = new FakeWindow(Start);
        var director = NewDirector(() => true);
        var snapped = Tile.RightHalf.Frame(Secondary.WorkArea);

        director.Snap(Tile.RightHalf, window, Secondary);
        director.Perform(WindowCommand.LeftHalf, window);

        director.Perform(WindowCommand.Restore, window).ShouldBe(CommandOutcome.Moved);
        window.Frame.ShouldBe(snapped);
        director.Perform(WindowCommand.Restore, window).ShouldBe(CommandOutcome.Moved);
        window.Frame.ShouldBe(Start);
    }

    [Fact]
    public void Restore_OneMoveAtATime_RefusedWrite_KeepsThatStep()
    {
        var window = new FakeWindow(Start);
        var director = NewDirector(() => true);
        director.Perform(WindowCommand.LeftHalf, window);
        var left = window.Frame!.Value;
        director.Perform(WindowCommand.TopRight, window);

        window.NextOutcome = CommandOutcome.Rejected(5);
        director.Perform(WindowCommand.Restore, window).ShouldBe(CommandOutcome.Rejected(5));
        window.Frame.ShouldBe(Tile.TopRight.Frame(Primary.WorkArea));
        director.RememberedWindowCount.ShouldBe(1);

        window.NextOutcome = CommandOutcome.Moved;
        director.Perform(WindowCommand.Restore, window).ShouldBe(CommandOutcome.Moved);
        window.Frame.ShouldBe(left);
        director.Perform(WindowCommand.Restore, window).ShouldBe(CommandOutcome.Moved);
        window.Frame.ShouldBe(Start);
    }

    [Fact]
    public void Restore_OneMoveAtATime_TurnedOff_CollapsesToTheOriginalFrame()
    {
        // Turning the setting off has to forget the in-between frames even when it is turned
        // straight back on, before any further command. Otherwise the next Restore would still
        // step, and "off" would not mean the pre-Loadstone position.
        var oneMove = true;
        var window = new FakeWindow(Start);
        var director = NewDirector(() => oneMove);
        director.Perform(WindowCommand.LeftHalf, window);
        director.Perform(WindowCommand.TopRight, window);
        director.Perform(WindowCommand.Center, window);

        oneMove = false;
        director.ApplyRestoreMode();
        oneMove = true;
        director.ApplyRestoreMode();

        director.Perform(WindowCommand.Restore, window).ShouldBe(CommandOutcome.Moved);
        window.Frame.ShouldBe(Start);
        director.Perform(WindowCommand.Restore, window).ShouldBe(CommandOutcome.NothingToRestore);
    }

    [Fact]
    public void Restore_OneMoveAtATime_TurnedOn_KeepsTheFrameAlreadyRemembered()
    {
        var oneMove = false;
        var window = new FakeWindow(Start);
        var director = NewDirector(() => oneMove);
        director.Perform(WindowCommand.LeftHalf, window);
        var left = window.Frame!.Value;

        oneMove = true;
        director.ApplyRestoreMode();
        director.Perform(WindowCommand.TopRight, window);

        director.Perform(WindowCommand.Restore, window).ShouldBe(CommandOutcome.Moved);
        window.Frame.ShouldBe(left);
        director.Perform(WindowCommand.Restore, window).ShouldBe(CommandOutcome.Moved);
        window.Frame.ShouldBe(Start);
    }

    [Fact]
    public void Restore_OneMoveAtATime_KeepsAtMostThirtyTwoFrames_AndStillTheOriginal()
    {
        var window = new FakeWindow(Start);
        var director = NewDirector(() => true);
        var before = new List<Rect>();
        for (var i = 0; i < 33; i++)
        {
            var placed = new Rect(i * 10, i * 10, 200, 150);
            window.Frame = placed;
            before.Add(placed);
            director.Perform(WindowCommand.LeftHalf, window).ShouldBe(CommandOutcome.Moved);
        }

        var restored = new List<Rect>();
        for (var i = 0; i < 32; i++)
        {
            director.Perform(WindowCommand.Restore, window).ShouldBe(CommandOutcome.Moved);
            restored.Add(window.Frame!.Value);
        }

        director.Perform(WindowCommand.Restore, window).ShouldBe(CommandOutcome.NothingToRestore);
        restored[0].ShouldBe(before[32]);
        restored[^1].ShouldBe(before[0]);
        restored.ShouldNotContain(before[1]);
    }

    [Fact]
    public void Restore_WithNoDisplaysAttached_StillRestores()
    {
        // Restore writes back a frame recorded earlier, so it answers before the display lookup. When
        // it did ask for a display, undoing a tile after the last external monitor was unplugged
        // reported NoDisplay and left the window on its tile.
        var window = new FakeWindow(Start);
        var director = NewDirector();
        director.Perform(WindowCommand.LeftHalf, window).ShouldBe(CommandOutcome.Moved);

        _displays = [];
        director.Perform(WindowCommand.Restore, window).ShouldBe(CommandOutcome.Moved);
        window.Frame.ShouldBe(Start);
    }

    [Fact]
    public void Perform_EveryCommand_IsHandled()
    {
        // Perform's command switch ends in a throw that only the arm-free switches keep unreachable; a
        // spot-check of five commands would stay green while a new one threw under the user's hotkey.
        foreach (var command in WindowCommands.All)
        {
            var window = new FakeWindow(Start);
            var expected = command == WindowCommand.Restore
                ? CommandOutcome.NothingToRestore
                : CommandOutcome.Moved;
            NewDirector().Perform(command, window).ShouldBe(expected, command.Id());
        }
    }

    [Fact]
    public void Snap_UsesTheGivenDisplay_NotTheOneUnderTheWindow()
    {
        var window = new FakeWindow(Start);
        NewDirector().Snap(Tile.RightHalf, window, Secondary).ShouldBe(CommandOutcome.Moved);
        window.Frame.ShouldBe(Tile.RightHalf.Frame(Secondary.WorkArea));
    }

    [Fact]
    public void Snap_RecordsTheOriginalFrameForRestore()
    {
        var window = new FakeWindow(Start);
        var director = NewDirector();
        director.Snap(Tile.RightHalf, window, Secondary);
        director.Perform(WindowCommand.Restore, window).ShouldBe(CommandOutcome.Moved);
        window.Frame.ShouldBe(Start);
    }

    [Fact]
    public void NextDisplay_MapsTheFrameOntoTheNeighbor()
    {
        // The primary's left half is (0,0,600,861); the secondary's work area is 1000x700 at (1201,-100),
        // so the left half there is 500x700. Written out rather than taken from Layout.Mapped so this
        // still fails if the mapping changes.
        var window = new FakeWindow(new Rect(0, 0, 600, 861));
        NewDirector().Perform(WindowCommand.NextDisplay, window).ShouldBe(CommandOutcome.Moved);
        window.Frame.ShouldBe(new Rect(1201, -100, 500, 700));
    }

    [Fact]
    public void NextDisplay_WindowOverhangingItsWorkArea_KeepsTheOverhang()
    {
        // 501x801 at (100,100) ends 40 px below the primary's work area, under the taskbar, which is an
        // ordinary place for a window to be. It has to arrive on the secondary still overhanging — bottom
        // 632 against a work area that ends at 600 — because trimming it is a resize nobody asked for.
        var window = new FakeWindow(new Rect(100, 100, 501, 801));
        NewDirector().Perform(WindowCommand.NextDisplay, window).ShouldBe(CommandOutcome.Moved);
        window.Frame.ShouldBe(new Rect(1284, -19, 417, 651));
    }

    [Fact]
    public void NextDisplay_WindowOffEveryDisplay_CentersItOnTheNeighbor()
    {
        // Parked at (-9000,-9000) by a disconnected monitor, so the primary is only standing in as the
        // source display: the frame's offsets describe a display the window was never on, and mapping
        // them would land it somewhere just as invisible. Centring on the neighbour is what fetches it
        // back, at its own size.
        var window = new FakeWindow(new Rect(-9000, -9000, 300, 200));
        NewDirector().Perform(WindowCommand.NextDisplay, window).ShouldBe(CommandOutcome.Moved);
        window.Frame.ShouldBe(new Rect(1551, 150, 300, 200));
    }

    [Fact]
    public void PreviousDisplay_WrapsAround()
    {
        var window = new FakeWindow(Start);
        NewDirector().Perform(WindowCommand.PreviousDisplay, window).ShouldBe(CommandOutcome.Moved);
        ScreenGeometry.DisplayContaining(window.Frame!.Value, _displays).ShouldBe(Secondary);
    }

    [Fact]
    public void NextDisplay_WithOneDisplay_ReportsNoOtherDisplay()
    {
        _displays = [Primary];
        var window = new FakeWindow(Start);
        NewDirector().Perform(WindowCommand.NextDisplay, window).ShouldBe(CommandOutcome.NoOtherDisplay);
        window.Writes.ShouldBeEmpty();
    }
}
