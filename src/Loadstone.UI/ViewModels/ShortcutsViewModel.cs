using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Loadstone.Core.Commands;
using Loadstone.UI.Input;

namespace Loadstone.UI.ViewModels;

internal sealed partial class ShortcutsViewModel : ObservableObject, IDisposable
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

    /// <summary>
    /// Both this and the hotkey center live as long as the app, so the subscription above cannot
    /// outlive its target; the detach is here because the container disposes what it built and every
    /// other subscription in this layer is symmetric, so an asymmetric one reads as a forgotten line
    /// rather than a decision.
    /// </summary>
    public void Dispose() => _hotkeys.BindingsChanged -= OnBindingsChanged;

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
