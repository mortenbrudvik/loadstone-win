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

/// <summary>One line of the tray menu, as data, so the menu's shape is testable without WPF.</summary>
internal sealed record TrayMenuEntry(TrayMenuEntryKind Kind, string Title = "", string? Gesture = null, WindowCommand? Command = null);
