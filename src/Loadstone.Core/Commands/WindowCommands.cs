using Loadstone.Core.Geometry;

namespace Loadstone.Core.Commands;

public static class WindowCommands
{
    private const KeyModifiers CtrlAlt = KeyModifiers.Control | KeyModifiers.Alt;
    private const KeyModifiers CtrlAltWin = KeyModifiers.Control | KeyModifiers.Alt | KeyModifiers.Win;

    /// <summary>
    /// Every command, in menu and settings order. Wrapped read-only because Enum.GetValues hands back
    /// a plain mutable array: exposed bare, a caller could cast it back to WindowCommand[] and reorder
    /// or overwrite the single source of truth behind the hotkeys, the tray menu, and the Shortcuts
    /// tab, for every other caller at once.
    /// </summary>
    public static IReadOnlyList<WindowCommand> All { get; } = Array.AsReadOnly(Enum.GetValues<WindowCommand>());

    /// <summary>The sections in display order. Read-only for the same reason as <see cref="All"/>.</summary>
    public static IReadOnlyList<CommandSection> SectionOrder { get; } = Array.AsReadOnly(Enum.GetValues<CommandSection>());

    public static IReadOnlyList<WindowCommand> In(CommandSection section) =>
        All.Where(c => c.Section() == section).ToList();

    public static WindowCommand? FromId(string id)
    {
        foreach (var command in All)
        {
            if (command.Id() == id)
            {
                return command;
            }
        }

        return null;
    }

    /// <summary>
    /// Persistence key: a user's custom shortcut is stored under this id in settings.json, so an id
    /// must never change once shipped. They match the Mac app's ids exactly.
    /// </summary>
    public static string Id(this WindowCommand command) => command switch
    {
        WindowCommand.LeftHalf => "leftHalf",
        WindowCommand.RightHalf => "rightHalf",
        WindowCommand.TopHalf => "topHalf",
        WindowCommand.BottomHalf => "bottomHalf",
        WindowCommand.TopLeft => "topLeft",
        WindowCommand.TopRight => "topRight",
        WindowCommand.BottomLeft => "bottomLeft",
        WindowCommand.BottomRight => "bottomRight",
        WindowCommand.LeftThird => "leftThird",
        WindowCommand.CenterThird => "centerThird",
        WindowCommand.RightThird => "rightThird",
        WindowCommand.LeftTwoThirds => "leftTwoThirds",
        WindowCommand.RightTwoThirds => "rightTwoThirds",
        WindowCommand.Maximize => "maximize",
        WindowCommand.Center => "center",
        WindowCommand.Restore => "restore",
        WindowCommand.NextDisplay => "nextDisplay",
        WindowCommand.PreviousDisplay => "previousDisplay",
    };

    /// <summary>
    /// The menu and Shortcuts-tab title. This is the one command switch that is NOT self-enforcing:
    /// the compiler cannot see that the outer <c>is { } tile</c> already handled every tile command,
    /// so the inner switch needs a discard arm, and a new non-tile command would compile here and
    /// throw at run time. It is safe only second-hand: <see cref="Id"/>, <see cref="Section"/>,
    /// <see cref="ToTile"/> and <see cref="DefaultShortcut"/> have no discard arm and fail the build
    /// first, so the throw below is unreachable in any build that succeeds.
    /// </summary>
    public static string Title(this WindowCommand command) => command.ToTile() is { } tile
        ? tile.Title()
        : command switch
        {
            WindowCommand.Center => "Center",
            WindowCommand.Restore => "Restore",
            WindowCommand.NextDisplay => "Next Display",
            WindowCommand.PreviousDisplay => "Previous Display",
            _ => throw new ArgumentOutOfRangeException(nameof(command), command, "tile commands are titled above"),
        };

    public static CommandSection Section(this WindowCommand command) => command switch
    {
        WindowCommand.LeftHalf or WindowCommand.RightHalf or WindowCommand.TopHalf or WindowCommand.BottomHalf => CommandSection.Halves,
        WindowCommand.TopLeft or WindowCommand.TopRight or WindowCommand.BottomLeft or WindowCommand.BottomRight => CommandSection.Corners,
        WindowCommand.LeftThird or WindowCommand.CenterThird or WindowCommand.RightThird or WindowCommand.LeftTwoThirds or WindowCommand.RightTwoThirds => CommandSection.Thirds,
        WindowCommand.Maximize or WindowCommand.Center or WindowCommand.Restore => CommandSection.Window,
        WindowCommand.NextDisplay or WindowCommand.PreviousDisplay => CommandSection.Displays,
    };

    /// <summary>The tile a command lays out, or null for Center, Restore, and the display moves.</summary>
    public static Tile? ToTile(this WindowCommand command) => command switch
    {
        WindowCommand.LeftHalf => Tile.LeftHalf,
        WindowCommand.RightHalf => Tile.RightHalf,
        WindowCommand.TopHalf => Tile.TopHalf,
        WindowCommand.BottomHalf => Tile.BottomHalf,
        WindowCommand.TopLeft => Tile.TopLeft,
        WindowCommand.TopRight => Tile.TopRight,
        WindowCommand.BottomLeft => Tile.BottomLeft,
        WindowCommand.BottomRight => Tile.BottomRight,
        WindowCommand.LeftThird => Tile.LeftThird,
        WindowCommand.CenterThird => Tile.CenterThird,
        WindowCommand.RightThird => Tile.RightThird,
        WindowCommand.LeftTwoThirds => Tile.LeftTwoThirds,
        WindowCommand.RightTwoThirds => Tile.RightTwoThirds,
        WindowCommand.Maximize => Tile.Maximize,
        WindowCommand.Center or WindowCommand.Restore or WindowCommand.NextDisplay or WindowCommand.PreviousDisplay => null,
    };

    /// <summary>
    /// The Mac defaults translated to Windows: Control-Option becomes Ctrl+Alt, Command becomes Win.
    /// The corner keys U I / J K form a square on the keyboard.
    /// </summary>
    public static Shortcut DefaultShortcut(this WindowCommand command) => command switch
    {
        WindowCommand.LeftHalf => new Shortcut(CtrlAlt, VirtualKeys.Left),
        WindowCommand.RightHalf => new Shortcut(CtrlAlt, VirtualKeys.Right),
        WindowCommand.TopHalf => new Shortcut(CtrlAlt, VirtualKeys.Up),
        WindowCommand.BottomHalf => new Shortcut(CtrlAlt, VirtualKeys.Down),
        WindowCommand.TopLeft => new Shortcut(CtrlAlt, 'U'),
        WindowCommand.TopRight => new Shortcut(CtrlAlt, 'I'),
        WindowCommand.BottomLeft => new Shortcut(CtrlAlt, 'J'),
        WindowCommand.BottomRight => new Shortcut(CtrlAlt, 'K'),
        WindowCommand.LeftThird => new Shortcut(CtrlAlt, 'D'),
        WindowCommand.CenterThird => new Shortcut(CtrlAlt, 'F'),
        WindowCommand.RightThird => new Shortcut(CtrlAlt, 'G'),
        WindowCommand.LeftTwoThirds => new Shortcut(CtrlAlt, 'E'),
        WindowCommand.RightTwoThirds => new Shortcut(CtrlAlt, 'T'),
        WindowCommand.Maximize => new Shortcut(CtrlAlt, VirtualKeys.Enter),
        WindowCommand.Center => new Shortcut(CtrlAlt, 'C'),
        WindowCommand.Restore => new Shortcut(CtrlAlt, VirtualKeys.Backspace),
        WindowCommand.NextDisplay => new Shortcut(CtrlAltWin, VirtualKeys.Right),
        WindowCommand.PreviousDisplay => new Shortcut(CtrlAltWin, VirtualKeys.Left),
    };
}
