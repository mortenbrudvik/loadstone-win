namespace Loadstone.Core.Commands;

/// <summary>
/// Everything a user can ask Loadstone to do to a window, in menu and settings order. This is the
/// single source of truth for the hotkey list, the tray menu, and the Shortcuts tab.
/// <see cref="WindowCommands.Id"/>, <see cref="WindowCommands.Section"/>, <see cref="WindowCommands.ToTile"/>
/// and <see cref="WindowCommands.DefaultShortcut"/> have no discard arm, so a new member fails the
/// build until each of those names it. <see cref="WindowCommands.Title"/> and
/// <c>WindowDirector.Perform</c> do not: a new non-tile command compiles once those four are updated,
/// and throws when the menu is built or the command is pressed. The nets are
/// <c>WindowCommandTests.Title_AnswersForEveryCommand_WithNoTwoAlike</c> and
/// <c>WindowDirectorTests.Perform_EveryCommand_IsHandled</c>.
/// </summary>
public enum WindowCommand
{
    LeftHalf,
    RightHalf,
    TopHalf,
    BottomHalf,
    TopLeft,
    TopRight,
    BottomLeft,
    BottomRight,
    LeftThird,
    CenterThird,
    RightThird,
    LeftTwoThirds,
    RightTwoThirds,
    Maximize,
    Center,
    Restore,
    NextDisplay,
    PreviousDisplay,
}
