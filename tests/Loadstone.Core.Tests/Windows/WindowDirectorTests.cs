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

    private WindowDirector NewDirector() => new(() => _displays, id => !_dead.Contains(id));

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
        var window = new FakeWindow(Tile.LeftHalf.Frame(Primary.WorkArea));
        NewDirector().Perform(WindowCommand.NextDisplay, window).ShouldBe(CommandOutcome.Moved);
        window.Frame.ShouldBe(Layout.Mapped(Tile.LeftHalf.Frame(Primary.WorkArea), Primary.WorkArea, Secondary.WorkArea));
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
