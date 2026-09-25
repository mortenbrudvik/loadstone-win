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
    public void Perform_SamplesTheFrameOnce_ThenReadsAgainAfterTheWrite()
    {
        // ReadFrame is a method because it is two or three Win32 calls and answers differently between
        // reads while the window is moving. The placement is decided from the one sample taken up
        // front. The second read is after the write, and only answers whether the visible frame changed.
        var window = new FakeWindow(Start);
        NewDirector().Perform(WindowCommand.NextDisplay, window).ShouldBe(CommandOutcome.Moved);
        window.Reads.ShouldBe(2);
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
    public void Restore_OneMoveAtATime_SameCommandTwice_StepsBackOnce()
    {
        var window = new FakeWindow(Start);
        var director = NewDirector(() => true);
        director.Perform(WindowCommand.LeftHalf, window);
        var left = window.Frame!.Value;
        director.Perform(WindowCommand.LeftHalf, window);
        window.Frame.ShouldBe(left);

        director.Perform(WindowCommand.Restore, window).ShouldBe(CommandOutcome.Moved);
        window.Frame.ShouldBe(Start);
        director.Perform(WindowCommand.Restore, window).ShouldBe(CommandOutcome.NothingToRestore);
    }

    [Fact]
    public void Restore_OneMoveAtATime_UnchangedVisibleFrame_DoesNotPush_WhenTheRequestDiffers()
    {
        // The second press asks for the whole tile again. A fixed-size window already centered
        // there reports Moved and stays put, so the request and the visible frame are different
        // rectangles. Comparing the request would store a step that does not move the window.
        var window = new FakeWindow(Start);
        var director = NewDirector(() => true);
        director.Perform(WindowCommand.LeftHalf, window);
        var centered = new Rect(10, 10, 50, 50);
        window.Frame = centered;
        window.HoldFrame = true;
        director.Perform(WindowCommand.LeftHalf, window).ShouldBe(CommandOutcome.Moved);
        window.Writes[^1].ShouldNotBe(centered);
        window.HoldFrame = false;

        director.Perform(WindowCommand.Restore, window).ShouldBe(CommandOutcome.Moved);
        window.Frame.ShouldBe(Start);
        director.Perform(WindowCommand.Restore, window).ShouldBe(CommandOutcome.NothingToRestore);
    }

    [Fact]
    public void Perform_AlreadyOnTheTile_LeavesNothingToRestore()
    {
        var left = Tile.LeftHalf.Frame(Primary.WorkArea);
        var window = new FakeWindow(left);
        var director = NewDirector();
        director.Perform(WindowCommand.LeftHalf, window).ShouldBe(CommandOutcome.Moved);
        director.RememberedWindowCount.ShouldBe(0);
        director.Perform(WindowCommand.Restore, window).ShouldBe(CommandOutcome.NothingToRestore);
        window.Frame.ShouldBe(left);
    }

    [Fact]
    public void ApplyRestoreMode_WhileOneMoveStaysOn_KeepsTheSteps()
    {
        // Changed fires for every settings write. Re-reading the toggle while it is still on
        // must not throw away the in-between frames.
        var window = new FakeWindow(Start);
        var director = NewDirector(() => true);
        director.Perform(WindowCommand.LeftHalf, window);
        var left = window.Frame!.Value;
        director.Perform(WindowCommand.TopRight, window);
        director.ApplyRestoreMode();

        director.Perform(WindowCommand.Restore, window).ShouldBe(CommandOutcome.Moved);
        window.Frame.ShouldBe(left);
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
        // straight back on, before any further command. The window stays put until Restore.
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
    public void RightHalf_AlreadyInTheRightHalf_CarriesOnIntoTheLeftHalfOfTheDisplayToTheRight()
    {
        // The primary's right half is (600,0,601,861). The secondary lies to its right, sharing rows 0
        // to 600, and its work area is 1000x700 at (1201,-100), so its left half is 500x700 there.
        // Filling the half, it goes on without the half being written again: one write, and one read
        // either side of it.
        var window = new FakeWindow(new Rect(600, 0, 601, 861));
        NewDirector().Perform(WindowCommand.RightHalf, window).ShouldBe(CommandOutcome.CarriedOn(Side.Right, new Rect(600, 0, 601, 861)));
        window.Frame.ShouldBe(new Rect(1201, -100, 500, 700));
        window.Writes.Count.ShouldBe(1);
        window.Reads.ShouldBe(2);
    }

    [Theory]
    [InlineData(601, 0, 600, 861)]
    [InlineData(599, 0, 602, 861)]
    [InlineData(600, 0, 600, 861)]
    [InlineData(600, 0, 602, 861)]
    [InlineData(600, 1, 601, 860)]
    [InlineData(600, -1, 601, 862)]
    [InlineData(600, 0, 601, 860)]
    [InlineData(600, 0, 601, 862)]
    public void RightHalf_WithinAPixelOfTheHalf_CarriesOnAtTheFirstPress(int x, int y, int width, int height)
    {
        // One edge each a pixel off the half (600,0,601,861), inward and then outward: left, right,
        // top, bottom. A half carried over by Next Display usually lands like that. It counts as in
        // the half, so the first press carries it on instead of nudging it a pixel.
        var window = new FakeWindow(new Rect(x, y, width, height));
        NewDirector().Perform(WindowCommand.RightHalf, window).ShouldBe(CommandOutcome.CarriedOn(Side.Right, new Rect(x, y, width, height)));
        window.Frame.ShouldBe(new Rect(1201, -100, 500, 700));
        window.Writes.Count.ShouldBe(1);
    }

    [Theory]
    [InlineData(602, 0, 599, 861)]
    [InlineData(598, 0, 603, 861)]
    [InlineData(600, 0, 599, 861)]
    [InlineData(600, 0, 603, 861)]
    [InlineData(600, 2, 601, 859)]
    [InlineData(600, -2, 601, 863)]
    [InlineData(600, 0, 601, 859)]
    [InlineData(600, 0, 601, 863)]
    public void RightHalf_TwoPixelsOffTheHalf_IsLaidIntoTheHalfFirst(int x, int y, int width, int height)
    {
        // The same four edges, each two pixels off inward and then outward: not in the half yet, so the
        // press lays it in. The outward rows keep "within a pixel" from turning into "covers the half,
        // give or take a pixel".
        var window = new FakeWindow(new Rect(x, y, width, height));
        NewDirector().Perform(WindowCommand.RightHalf, window).ShouldBe(CommandOutcome.Moved);
        window.Frame.ShouldBe(new Rect(600, 0, 601, 861));
        window.Writes.Count.ShouldBe(1);
    }

    [Fact]
    public void RightHalf_WindowFillingTheWorkArea_IsLaidIntoTheHalf_NotCarriedOn()
    {
        // A maximized window already reaches the right edge of the half, and the secondary lies beside
        // it, but it is not in the half: the press lays it in, and only the next press carries it on.
        var window = new FakeWindow(Primary.WorkArea);
        NewDirector().Perform(WindowCommand.RightHalf, window).ShouldBe(CommandOutcome.Moved);
        window.Frame.ShouldBe(new Rect(600, 0, 601, 861));
        window.Writes.Count.ShouldBe(1);
    }

    [Fact]
    public void LeftHalf_WindowFillingTheWorkArea_IsLaidIntoTheHalf_NotCarriedOn()
    {
        // The mirror on the secondary, which has the primary to its left.
        var window = new FakeWindow(Secondary.WorkArea);
        NewDirector().Perform(WindowCommand.LeftHalf, window).ShouldBe(CommandOutcome.Moved);
        window.Frame.ShouldBe(new Rect(1201, -100, 500, 700));
        window.Writes.Count.ShouldBe(1);
    }

    [Fact]
    public void LeftHalf_AlreadyInTheLeftHalf_CarriesOnIntoTheRightHalfOfTheDisplayToTheLeft()
    {
        var window = new FakeWindow(new Rect(1201, -100, 500, 700));
        NewDirector().Perform(WindowCommand.LeftHalf, window).ShouldBe(CommandOutcome.CarriedOn(Side.Left, new Rect(1201, -100, 500, 700)));
        window.Frame.ShouldBe(new Rect(600, 0, 601, 861));
    }

    [Fact]
    public void LeftHalf_NotYetInTheLeftHalf_TilesOnItsOwnDisplay()
    {
        // The window sits on the secondary, which has the primary to its left. A window not yet in
        // the half is laid into it first.
        var window = new FakeWindow(new Rect(1301, 0, 401, 301));
        NewDirector().Perform(WindowCommand.LeftHalf, window).ShouldBe(CommandOutcome.Moved);
        window.Frame.ShouldBe(new Rect(1201, -100, 500, 700));
    }

    [Fact]
    public void LeftHalf_AlreadyInTheLeftHalfOfTheLeftmostDisplay_StaysWithoutABeep()
    {
        // Nothing lies to the left of the primary. Previous Display would wrap round to the secondary;
        // carrying on stops at the edge of the desk and answers Moved, as the same press always has.
        var left = new Rect(0, 0, 600, 861);
        var window = new FakeWindow(left);
        NewDirector().Perform(WindowCommand.LeftHalf, window).ShouldBe(CommandOutcome.Moved);
        window.Frame.ShouldBe(left);
        window.Writes.Count.ShouldBe(1);
    }

    [Fact]
    public void RightHalf_FixedSizeWindowAlreadyCentredInTheHalf_CarriesOnToo()
    {
        // A window without a sizing frame is centred on the half instead of filling it, so it is never
        // within a pixel of the half; it is in the half when writing the half leaves it where it was.
        // 301x201 centred on (600,0,601,861) sits at (750,330); centred on (1201,-100,500,700) it sits
        // at (1201 + 199/2, -100 + 499/2), which is (1300,149). Three reads: the sample before anything
        // is written, then one after each write; the one after writing the half is what decides.
        var window = new FakeWindow(new Rect(750, 330, 301, 201)) { FixedSize = true };
        var director = NewDirector();
        director.Perform(WindowCommand.RightHalf, window).ShouldBe(CommandOutcome.CarriedOn(Side.Right, new Rect(750, 330, 301, 201)));
        window.Frame.ShouldBe(new Rect(1300, 149, 301, 201));
        window.Reads.ShouldBe(3);

        // Carrying on was the first move Loadstone made, so it is the one Restore undoes.
        director.Perform(WindowCommand.Restore, window).ShouldBe(CommandOutcome.Moved);
        window.Frame.ShouldBe(new Rect(750, 330, 301, 201));
    }

    [Fact]
    public void RightHalf_RefusedWrite_DoesNotCarryOn()
    {
        // The fixed-size window is judged by writing the half, and that write is refused.
        var window = new FakeWindow(new Rect(750, 330, 301, 201)) { FixedSize = true, NextOutcome = CommandOutcome.Rejected(5) };
        NewDirector().Perform(WindowCommand.RightHalf, window).ShouldBe(CommandOutcome.Rejected(5));
        window.Writes.Count.ShouldBe(1);
    }

    [Fact]
    public void RightHalf_FrameUnreadableAfterTheWrite_CountsAsAMove_AndDoesNotCarryOn()
    {
        // The fixed-size window is judged by reading it back after writing the half. When that read
        // fails, whether it was already in place cannot be told, so the press counts as a move: it
        // is remembered for Restore, like any other unreadable read-back, and it goes no further.
        var window = new FakeWindow(new Rect(750, 330, 301, 201)) { FixedSize = true, UnreadableAfterWrite = true };
        var director = NewDirector();
        director.Perform(WindowCommand.RightHalf, window).ShouldBe(CommandOutcome.Moved);
        window.Writes.Count.ShouldBe(1);
        director.RememberedWindowCount.ShouldBe(1);
    }

    [Fact]
    public void RightHalf_CarryOnRefused_ReportsTheRefusal_AndRemembersNothing()
    {
        // The half is written and accepted, leaving the fixed-size window where it was, then the write
        // onto the secondary is refused. Answering Moved from the first write would tell the user a
        // window moved that never left, with no beep.
        var centred = new Rect(750, 330, 301, 201);
        var window = new FakeWindow(centred)
        {
            FixedSize = true,
            OutcomeFor = frame => frame.X >= 1201 ? CommandOutcome.Rejected(5) : CommandOutcome.Moved,
        };
        var director = NewDirector();
        director.Perform(WindowCommand.RightHalf, window).ShouldBe(CommandOutcome.Rejected(5) with { CarryOn = new CarryOn(Side.Right, centred) });
        window.Frame.ShouldBe(centred);
        director.RememberedWindowCount.ShouldBe(0);
    }

    [Fact]
    public void RightHalf_FixedSizeWindowPressedTwice_IsCentredInTheHalf_ThenCarriedOn_AndRestoresToStart()
    {
        // 501x401 centred on (600,0,601,861) sits at (650,230). The second press writes the half again,
        // which leaves it there, so it goes on: centred on (1201,-100,500,700) it sits at
        // (1201 + (500 - 501) / 2, -100 + 299 / 2), which is (1201,49), overhanging the half by a pixel.
        var window = new FakeWindow(Start) { FixedSize = true };
        var director = NewDirector();
        director.Perform(WindowCommand.RightHalf, window).ShouldBe(CommandOutcome.Moved);
        window.Frame.ShouldBe(new Rect(650, 230, 501, 401));
        director.Perform(WindowCommand.RightHalf, window).ShouldBe(CommandOutcome.CarriedOn(Side.Right, new Rect(650, 230, 501, 401)));
        window.Frame.ShouldBe(new Rect(1201, 49, 501, 401));

        director.Perform(WindowCommand.Restore, window).ShouldBe(CommandOutcome.Moved);
        window.Frame.ShouldBe(Start);
        director.Perform(WindowCommand.Restore, window).ShouldBe(CommandOutcome.NothingToRestore);
    }

    [Fact]
    public void Restore_OneMoveAtATime_StepsBackAcrossAFixedSizeCarryOn()
    {
        // Writing the half again left the window where it was, so that write is no step; carrying it
        // on is one, and centring it in the half the one before.
        var window = new FakeWindow(Start) { FixedSize = true };
        var director = NewDirector(() => true);
        director.Perform(WindowCommand.RightHalf, window);
        director.Perform(WindowCommand.RightHalf, window);
        window.Frame.ShouldBe(new Rect(1201, 49, 501, 401));

        director.Perform(WindowCommand.Restore, window).ShouldBe(CommandOutcome.Moved);
        window.Frame.ShouldBe(new Rect(650, 230, 501, 401));
        director.Perform(WindowCommand.Restore, window).ShouldBe(CommandOutcome.Moved);
        window.Frame.ShouldBe(Start);
        director.Perform(WindowCommand.Restore, window).ShouldBe(CommandOutcome.NothingToRestore);
    }

    [Fact]
    public void RightHalf_WindowThatIgnoresTheWrite_IsTakenAsInTheHalf_AndStaysPut()
    {
        // An app that accepts a write and ignores it reads back unchanged after the half is written,
        // exactly like a fixed-size window already in the half, so it is carried on. The onward write
        // is ignored too: the window stays where it was, nothing is remembered, and the press answers
        // Moved without a beep, as the same press did before halves carried on. It is a plain Moved,
        // not a carry-on, because the window never left: the log must not say it went anywhere.
        var window = new FakeWindow(Start) { HoldFrame = true };
        var director = NewDirector();
        director.Perform(WindowCommand.RightHalf, window).ShouldBe(CommandOutcome.Moved);
        window.Writes.ShouldBe([new Rect(600, 0, 601, 861), new Rect(1201, -100, 500, 700)]);
        window.Frame.ShouldBe(Start);
        director.RememberedWindowCount.ShouldBe(0);
    }

    [Fact]
    public void RightHalf_WindowOffEveryDisplay_IsLaidIntoThePrimarysHalf_AndNeverCarriesOn()
    {
        // The primary only stands in for a window on no display, so the window was never in its half,
        // even when writing the half leaves the frame unchanged: a minimized window that refuses to
        // restore, or one that ignores the write. Carrying on would send it to the display beside the
        // primary.
        var window = new FakeWindow(new Rect(-9000, -9000, 300, 200)) { HoldFrame = true };
        NewDirector().Perform(WindowCommand.RightHalf, window).ShouldBe(CommandOutcome.Moved);
        window.Writes.ShouldBe([new Rect(600, 0, 601, 861)]);
    }

    [Fact]
    public void OnlyTheLeftAndRightHalvesCarryOn()
    {
        // Every other tile, pressed again on the primary, which has the secondary to its right, and on
        // the secondary, which has the primary to its left: none of them goes on, so a continuation
        // given to any of them, either way, fails here. Walking every command covers a new tile too.
        foreach (var command in WindowCommands.All)
        {
            if (command.ToTile() is not { } tile || tile is Tile.LeftHalf or Tile.RightHalf)
            {
                continue;
            }

            StaysPutWhenPressedAgain(command, tile, Primary);
            StaysPutWhenPressedAgain(command, tile, Secondary);
        }
    }

    private void StaysPutWhenPressedAgain(WindowCommand command, Tile tile, Display display)
    {
        var placed = tile.Frame(display.WorkArea);
        var window = new FakeWindow(placed);
        var where = $"{command.Id()} on {display.Frame}";
        NewDirector().Perform(command, window).ShouldBe(CommandOutcome.Moved, where);
        window.Frame.ShouldBe(placed, where);
        window.Writes.Count.ShouldBe(1, where);
    }

    [Fact]
    public void Snap_AlreadyInThatHalf_NeverCarriesOn()
    {
        // A drag ends where the user let go of it. Carrying on belongs to a key pressed again.
        var right = new Rect(600, 0, 601, 861);
        var window = new FakeWindow(right);
        NewDirector().Snap(Tile.RightHalf, window, Primary).ShouldBe(CommandOutcome.Moved);
        window.Frame.ShouldBe(right);
        window.Writes.Count.ShouldBe(1);
    }

    [Fact]
    public void Restore_AfterCarryingOn_ReturnsToTheFrameBeforeTheFirstCommand()
    {
        var window = new FakeWindow(Start);
        var director = NewDirector();
        director.Perform(WindowCommand.RightHalf, window);
        director.Perform(WindowCommand.RightHalf, window);
        window.Frame.ShouldBe(new Rect(1201, -100, 500, 700));

        director.Perform(WindowCommand.Restore, window).ShouldBe(CommandOutcome.Moved);
        window.Frame.ShouldBe(Start);
        director.Perform(WindowCommand.Restore, window).ShouldBe(CommandOutcome.NothingToRestore);
    }

    [Fact]
    public void Restore_OneMoveAtATime_StepsBackAcrossTheCarryOn()
    {
        // Carrying on is one step back, and the tile before it is the next. The press that carried on
        // wrote nothing else, so there is no step for a half the window was already in.
        var window = new FakeWindow(Start);
        var director = NewDirector(() => true);
        director.Perform(WindowCommand.RightHalf, window);
        director.Perform(WindowCommand.RightHalf, window);

        director.Perform(WindowCommand.Restore, window).ShouldBe(CommandOutcome.Moved);
        window.Frame.ShouldBe(new Rect(600, 0, 601, 861));
        director.Perform(WindowCommand.Restore, window).ShouldBe(CommandOutcome.Moved);
        window.Frame.ShouldBe(Start);
        director.Perform(WindowCommand.Restore, window).ShouldBe(CommandOutcome.NothingToRestore);
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
