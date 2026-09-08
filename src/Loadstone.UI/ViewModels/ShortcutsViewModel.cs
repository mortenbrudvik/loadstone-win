using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Loadstone.Core.Commands;
using Loadstone.UI.Input;

namespace Loadstone.UI.ViewModels;

internal sealed partial class ShortcutsViewModel : ObservableObject
{
    private readonly HotkeyCenter _hotkeys;

    public ShortcutsViewModel(HotkeyCenter hotkeys)
    {
        _hotkeys = hotkeys;
        Groups = WindowCommands.SectionOrder
            .Select(section => new ShortcutGroupViewModel(
                section.ToString(),
                WindowCommands.In(section).Select(command => new ShortcutRowViewModel(command, hotkeys)).ToList()))
            .ToList();
    }

    public IReadOnlyList<ShortcutGroupViewModel> Groups { get; }

    [RelayCommand]
    private void RestoreDefaults()
    {
        _hotkeys.ResetAll();
        foreach (var row in Groups.SelectMany(g => g.Rows))
        {
            row.Refresh();
        }
    }
}
