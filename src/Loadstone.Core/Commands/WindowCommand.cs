namespace Loadstone.Core.Commands;

/// <summary>
/// Everything a user can ask Loadstone to do to a window, in menu and settings order. This is the
/// single source of truth for the hotkey list, the tray menu, and the Shortcuts tab: a new member
/// fails the build until every switch in <see cref="WindowCommands"/> covers it, and then it appears
/// everywhere automatically.
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
