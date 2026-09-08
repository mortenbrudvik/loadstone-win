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

        // The one refresh point for every row: a chord recorded while the center is suspended only gets
        // its real registration status when Resume() re-registers it, and one row's chord can change
        // another row's status. Rows never subscribe themselves, so no row refreshes twice per event.
        _hotkeys.BindingsChanged += OnBindingsChanged;
    }

    public IReadOnlyList<ShortcutGroupViewModel> Groups { get; }

    [RelayCommand]
    private void RestoreDefaults() => _hotkeys.ResetAll();

    private void OnBindingsChanged(object? sender, EventArgs e)
    {
        foreach (var row in Groups.SelectMany(g => g.Rows))
        {
            row.Refresh();
        }
    }
}
