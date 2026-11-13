using Loadstone.Core.Commands;
using Loadstone.Core.Geometry;
using Loadstone.UI.Controls;
using Shouldly;
using Xunit;

namespace Loadstone.UI.Tests.Controls;

public class MiniDesktopModelTests
{
    private static readonly Rect Work = MiniDesktopModel.Screen.WorkArea;

    [Fact]
    public void Opens_AsAHalfAndTwoQuarters()
    {
        var model = new MiniDesktopModel();

        model.FrameOf(0).ShouldBe(Tile.LeftHalf.Frame(Work));
        model.FrameOf(1).ShouldBe(Tile.TopRight.Frame(Work));
        model.FrameOf(2).ShouldBe(Tile.BottomRight.Frame(Work));
    }

    /// <summary>
    /// Press throws for a command that lays out no tile, and it would throw from a click on the About
    /// tab, on the UI thread. Center, Restore and the display moves are the ones that must stay out.
    /// </summary>
    [Fact]
    public void Script_HoldsOnlyTileCommands_ForWindowsThatExist()
    {
        MiniDesktopModel.Script.ShouldAllBe(step => step.Command.ToTile() != null);
        MiniDesktopModel.Script.ShouldAllBe(step => step.Window >= 0 && step.Window < MiniDesktopModel.WindowCount);
    }

    [Fact]
    public void Press_LaysTheNextScriptedTileIntoTheWorkArea_AndBringsThatWindowToTheFront()
    {
        var model = new MiniDesktopModel();

        foreach (var (window, command) in MiniDesktopModel.Script)
        {
            var press = model.Press();

            press.Window.ShouldBe(window);
            press.Command.ShouldBe(command);
            press.Frame.ShouldBe(command.ToTile()!.Value.Frame(Work));
            model.FrameOf(window).ShouldBe(press.Frame);
            model.BackToFront[^1].ShouldBe(window);
        }
    }

    /// <summary>The script is a loop: its last step is what makes the next lap start from the opening layout's left half.</summary>
    [Fact]
    public void Press_WrapsRound_AfterTheLastStep()
    {
        var model = new MiniDesktopModel();
        for (var i = 0; i < MiniDesktopModel.Script.Count; i++)
        {
            model.Press();
        }

        model.FrameOf(0).ShouldBe(Tile.LeftHalf.Frame(Work));
        model.Press().Command.ShouldBe(MiniDesktopModel.Script[0].Command);
    }

    /// <summary>
    /// A snapped window shrinks back to a floating size when it is picked up, and the pointer keeps
    /// its place on it: halfway along the half is halfway along the floating window, and the distance
    /// below the top edge carries over.
    /// </summary>
    [Fact]
    public void PickUp_ShrinksTheWindowUnderThePointer()
    {
        var model = new MiniDesktopModel();
        var half = model.FrameOf(0);
        var pointer = new Point(half.X + half.Width / 2, half.Y + 20);

        var frame = model.PickUp(0, pointer);

        frame.Width.ShouldBe(MiniDesktopModel.Floating.Width);
        frame.Height.ShouldBe(MiniDesktopModel.Floating.Height);
        (pointer.X - frame.X).ShouldBe(MiniDesktopModel.Floating.Width / 2);
        (pointer.Y - frame.Y).ShouldBe(20);
        model.BackToFront[^1].ShouldBe(0);
    }

    [Fact]
    public void DragTo_CarriesTheWindowWithThePointer()
    {
        var model = new MiniDesktopModel();
        var start = new Point(400, 30);
        var picked = model.PickUp(0, start);

        var moved = model.DragTo(0, new Point(start.X + 250, start.Y + 100));

        moved.ShouldBe(picked with { X = picked.X + 250, Y = picked.Y + 100 });
    }

    /// <summary>The zones are the app's own, read against the whole display: the bottom band starts at the taskbar's lower edge, not at the work area's.</summary>
    [Theory]
    [InlineData(0, 400, Tile.LeftHalf)]
    [InlineData(1919, 400, Tile.RightHalf)]
    [InlineData(960, 0, Tile.Maximize)]
    [InlineData(0, 0, Tile.TopLeft)]
    [InlineData(1919, 779, Tile.BottomRight)]
    [InlineData(300, 779, Tile.LeftThird)]
    [InlineData(960, 779, Tile.CenterThird)]
    [InlineData(1700, 779, Tile.RightThird)]
    public void Drop_InAZone_SnapsToThatTile(int x, int y, Tile expected)
    {
        var model = new MiniDesktopModel();
        model.PickUp(1, new Point(1400, 100));

        // The last row of the display, taskbar included: the data would be measuring nothing if the display grew.
        MiniDesktopModel.Screen.Frame.Bottom.ShouldBe(780);
        var drop = model.Drop(1, new Point(x, y));

        drop.Tile.ShouldBe(expected);
        drop.Frame.ShouldBe(expected.Frame(Work));
        model.FrameOf(1).ShouldBe(drop.Frame);
    }

    [Fact]
    public void Drop_AwayFromEveryZone_LeavesTheWindowWhereItWasDropped()
    {
        var model = new MiniDesktopModel();
        var picked = model.PickUp(1, new Point(1400, 100));

        var drop = model.Drop(1, new Point(900, 400));

        drop.Tile.ShouldBeNull();
        drop.Frame.ShouldBe(picked with { X = picked.X - 500, Y = picked.Y + 300 });
    }

    /// <summary>
    /// A floating window can be carried until most of it hangs past the work area without the pointer
    /// ever reaching a zone. Left there it would sit under the taskbar, or with its title strip out of
    /// reach of the next drag.
    /// </summary>
    [Fact]
    public void Drop_AwayFromEveryZone_PullsAnOverhangingWindowBackOntoTheWorkArea()
    {
        var model = new MiniDesktopModel();
        model.PickUp(1, new Point(980, 100));

        var drop = model.Drop(1, new Point(1700, 620));

        drop.Tile.ShouldBeNull();
        drop.Frame.Right.ShouldBeLessThanOrEqualTo(Work.Right);
        drop.Frame.Bottom.ShouldBeLessThanOrEqualTo(Work.Bottom);
        drop.Frame.Left.ShouldBeGreaterThanOrEqualTo(Work.Left);
        drop.Frame.Top.ShouldBeGreaterThanOrEqualTo(Work.Top);
    }

    /// <summary>A mouse-up with no drag before it - a plain click on a window - must not move anything.</summary>
    [Fact]
    public void Drop_WithNothingHeld_ChangesNothing()
    {
        var model = new MiniDesktopModel();
        var before = model.FrameOf(2);

        var drop = model.Drop(2, new Point(0, 400));

        drop.Tile.ShouldBeNull();
        drop.Frame.ShouldBe(before);
        model.FrameOf(2).ShouldBe(before);
    }
}
