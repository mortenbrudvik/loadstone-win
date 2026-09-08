using Loadstone.Core.Commands;

namespace Loadstone.UI.Tray;

internal static class TrayMenuBuilder
{
    /// <summary>Every command in section order with a separator after each section, then Settings, Help, a separator, and Quit.</summary>
    public static IReadOnlyList<TrayMenuEntry> Build(Func<WindowCommand, Shortcut?> effectiveShortcut)
    {
        var entries = new List<TrayMenuEntry>();
        foreach (var section in WindowCommands.SectionOrder)
        {
            foreach (var command in WindowCommands.In(section))
            {
                entries.Add(new TrayMenuEntry(TrayMenuEntryKind.Command, command.Title(), effectiveShortcut(command)?.DisplayText, command));
            }

            entries.Add(new TrayMenuEntry(TrayMenuEntryKind.Separator));
        }

        entries.Add(new TrayMenuEntry(TrayMenuEntryKind.Settings, "Settings…"));
        entries.Add(new TrayMenuEntry(TrayMenuEntryKind.Help, "Loadstone Help"));
        entries.Add(new TrayMenuEntry(TrayMenuEntryKind.Separator));
        entries.Add(new TrayMenuEntry(TrayMenuEntryKind.Quit, "Quit Loadstone"));
        return entries;
    }
}
