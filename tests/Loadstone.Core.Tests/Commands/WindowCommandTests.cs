using Loadstone.Core.Commands;
using Loadstone.Core.Geometry;
using Shouldly;
using Xunit;

namespace Loadstone.Core.Tests.Commands;

public class WindowCommandTests
{
    // Pinned: these are settings keys. Renaming one silently discards a user's custom shortcut.
    private static readonly string[] Ids =
    [
        "leftHalf", "rightHalf", "topHalf", "bottomHalf",
        "topLeft", "topRight", "bottomLeft", "bottomRight",
        "leftThird", "centerThird", "rightThird", "leftTwoThirds", "rightTwoThirds",
        "maximize", "center", "restore", "nextDisplay", "previousDisplay",
    ];

    [Fact]
    public void All_HasEveryCommandInMenuOrder_WithPinnedIds()
    {
        WindowCommands.All.Select(c => c.Id()).ShouldBe(Ids);
    }

    [Fact]
    public void FromId_RoundTrips_AndRejectsUnknown()
    {
        foreach (var command in WindowCommands.All)
        {
            WindowCommands.FromId(command.Id()).ShouldBe(command);
        }

        WindowCommands.FromId("LeftHalf").ShouldBeNull();
        WindowCommands.FromId("").ShouldBeNull();
    }

    [Fact]
    public void Sections_GroupCommandsInOrder()
    {
        WindowCommands.SectionOrder.ShouldBe([CommandSection.Halves, CommandSection.Corners, CommandSection.Thirds, CommandSection.Window, CommandSection.Displays]);
        WindowCommands.In(CommandSection.Halves).ShouldBe([WindowCommand.LeftHalf, WindowCommand.RightHalf, WindowCommand.TopHalf, WindowCommand.BottomHalf]);
        WindowCommands.In(CommandSection.Corners).Count.ShouldBe(4);
        WindowCommands.In(CommandSection.Thirds).Count.ShouldBe(5);
        WindowCommands.In(CommandSection.Window).ShouldBe([WindowCommand.Maximize, WindowCommand.Center, WindowCommand.Restore]);
        WindowCommands.In(CommandSection.Displays).ShouldBe([WindowCommand.NextDisplay, WindowCommand.PreviousDisplay]);
    }

    [Theory]
    [InlineData(WindowCommand.LeftHalf, "Ctrl+Alt+Left")]
    [InlineData(WindowCommand.RightHalf, "Ctrl+Alt+Right")]
    [InlineData(WindowCommand.TopHalf, "Ctrl+Alt+Up")]
    [InlineData(WindowCommand.BottomHalf, "Ctrl+Alt+Down")]
    [InlineData(WindowCommand.TopLeft, "Ctrl+Alt+U")]
    [InlineData(WindowCommand.TopRight, "Ctrl+Alt+I")]
    [InlineData(WindowCommand.BottomLeft, "Ctrl+Alt+J")]
    [InlineData(WindowCommand.BottomRight, "Ctrl+Alt+K")]
    [InlineData(WindowCommand.LeftThird, "Ctrl+Alt+D")]
    [InlineData(WindowCommand.CenterThird, "Ctrl+Alt+F")]
    [InlineData(WindowCommand.RightThird, "Ctrl+Alt+G")]
    [InlineData(WindowCommand.LeftTwoThirds, "Ctrl+Alt+E")]
    [InlineData(WindowCommand.RightTwoThirds, "Ctrl+Alt+T")]
    [InlineData(WindowCommand.Maximize, "Ctrl+Alt+Enter")]
    [InlineData(WindowCommand.Center, "Ctrl+Alt+C")]
    [InlineData(WindowCommand.Restore, "Ctrl+Alt+Backspace")]
    [InlineData(WindowCommand.NextDisplay, "Ctrl+Alt+Win+Right")]
    [InlineData(WindowCommand.PreviousDisplay, "Ctrl+Alt+Win+Left")]
    public void DefaultShortcut_MatchesTheMacDefaultsTranslatedToWindows(WindowCommand command, string expected)
    {
        command.DefaultShortcut().ToString().ShouldBe(expected);
    }

    [Fact]
    public void DefaultShortcuts_AreUnique()
    {
        WindowCommands.All.Select(c => c.DefaultShortcut()).Distinct().Count().ShouldBe(WindowCommands.All.Count);
    }

    [Fact]
    public void ToTile_MapsTileCommandsAndNothingElse()
    {
        WindowCommand.LeftHalf.ToTile().ShouldBe(Tile.LeftHalf);
        WindowCommand.Maximize.ToTile().ShouldBe(Tile.Maximize);
        WindowCommand.Center.ToTile().ShouldBeNull();
        WindowCommand.Restore.ToTile().ShouldBeNull();
        WindowCommand.NextDisplay.ToTile().ShouldBeNull();
        WindowCommand.PreviousDisplay.ToTile().ShouldBeNull();
        var mapped = WindowCommands.All.Select(c => c.ToTile()).OfType<Tile>().ToList();
        mapped.Count.ShouldBe(mapped.Distinct().Count());
        mapped.OrderBy(t => t).ShouldBe(Enum.GetValues<Tile>().OrderBy(t => t));
    }

    [Fact]
    public void Title_UsesTileTitlesAndNamesTheRest()
    {
        WindowCommand.TopLeft.Title().ShouldBe("Top Left Corner");
        WindowCommand.Center.Title().ShouldBe("Center");
        WindowCommand.Restore.Title().ShouldBe("Restore");
        WindowCommand.NextDisplay.Title().ShouldBe("Next Display");
        WindowCommand.PreviousDisplay.Title().ShouldBe("Previous Display");
    }

    [Fact]
    public void Title_AnswersForEveryCommand_WithNoTwoAlike()
    {
        // Title() is the one command switch with a discard arm, so a new non-tile member compiles and
        // only fails when the tray menu is built - by which time the menu is already half drawn.
        foreach (var command in WindowCommands.All)
        {
            command.Title().ShouldNotBeNullOrWhiteSpace(command.Id());
        }

        WindowCommands.All.Select(c => c.Title()).Distinct().Count().ShouldBe(WindowCommands.All.Count);
    }

    [Fact]
    public void EveryCommand_HasAKnownSection_AndAUsableDefaultShortcut()
    {
        // A default that is not usable never reaches RegisterHotKey, so the command ships dead: the
        // menu shows the chord and pressing it does nothing.
        foreach (var command in WindowCommands.All)
        {
            WindowCommands.SectionOrder.ShouldContain(command.Section());
            WindowCommands.In(command.Section()).ShouldContain(command);
            command.DefaultShortcut().IsUsable.ShouldBeTrue(command.Id());
        }
    }
}
