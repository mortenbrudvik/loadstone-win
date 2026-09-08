using Loadstone.Core.Commands;

namespace Loadstone.UI.Tray;

internal enum TrayMenuEntryKind
{
    Command,
    Separator,
    Settings,
    Help,
    Quit,
}

/// <summary>
/// One line of the tray menu, as data, so the menu's shape is testable without WPF. The shape is a flat
/// union — <see cref="Command"/> carries a value only for <see cref="TrayMenuEntryKind.Command"/> — and
/// the factories below are what hold that invariant: the menu reads <c>Command.Value</c> without a check,
/// so an entry built with the wrong pair would throw on a menu click rather than at the call site.
/// </summary>
internal sealed record TrayMenuEntry
{
    private TrayMenuEntry(TrayMenuEntryKind kind, string title = "", string? gesture = null, WindowCommand? command = null)
    {
        Kind = kind;
        Title = title;
        Gesture = gesture;
        Command = command;
    }

    public TrayMenuEntryKind Kind { get; }

    public string Title { get; }

    /// <summary>The chord shown right-aligned on the item, or null when the command has none.</summary>
    public string? Gesture { get; }

    /// <summary>Set exactly when <see cref="Kind"/> is <see cref="TrayMenuEntryKind.Command"/>.</summary>
    public WindowCommand? Command { get; }

    public static TrayMenuEntry Separator => new(TrayMenuEntryKind.Separator);

    public static TrayMenuEntry ForCommand(WindowCommand command, string title, string? gesture) =>
        new(TrayMenuEntryKind.Command, title, gesture, command);

    public static TrayMenuEntry Settings(string title) => new(TrayMenuEntryKind.Settings, title);

    public static TrayMenuEntry Help(string title) => new(TrayMenuEntryKind.Help, title);

    public static TrayMenuEntry Quit(string title) => new(TrayMenuEntryKind.Quit, title);
}
