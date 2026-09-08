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
                entries.Add(TrayMenuEntry.ForCommand(command, command.Title(), effectiveShortcut(command)?.DisplayText));
            }

            entries.Add(TrayMenuEntry.Separator);
        }

        entries.Add(TrayMenuEntry.Settings("Settings…"));
        entries.Add(TrayMenuEntry.Help("Loadstone Help"));
        entries.Add(TrayMenuEntry.Separator);
        entries.Add(TrayMenuEntry.Quit("Quit Loadstone"));
        return entries;
    }
}
