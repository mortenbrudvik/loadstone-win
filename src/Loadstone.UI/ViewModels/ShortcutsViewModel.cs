using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Loadstone.Core.Commands;
using Loadstone.UI.Input;
using Wpf.Ui;
using Wpf.Ui.Controls;
using Wpf.Ui.Extensions;

namespace Loadstone.UI.ViewModels;

internal sealed partial class ShortcutsViewModel : ObservableObject, IDisposable
{
    private readonly HotkeyCenter _hotkeys;
    private readonly Func<Task<bool>> _confirmRestoreDefaults;

    public ShortcutsViewModel(HotkeyCenter hotkeys, IContentDialogService dialogs)
        : this(hotkeys, () => AskToRestoreDefaultsAsync(dialogs))
    {
    }

    /// <summary>
    /// The question as a seam, the way <see cref="SettingsViewModel"/> takes its shell and its beep:
    /// through the public constructor it is a ContentDialog in the Settings window, which no test
    /// of this class should have to show.
    /// </summary>
    internal ShortcutsViewModel(HotkeyCenter hotkeys, Func<Task<bool>> confirmRestoreDefaults)
    {
        _hotkeys = hotkeys;
        _confirmRestoreDefaults = confirmRestoreDefaults;
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

    /// <summary>
    /// Asks first. One click used to erase every recorded chord with nothing in between, and the
    /// button was the first Tab stop on its tab. Async so the dialog can be awaited; the generated
    /// command refuses a second click while the first is still being answered.
    /// </summary>
    [RelayCommand]
    private async Task RestoreDefaultsAsync()
    {
        if (await _confirmRestoreDefaults())
        {
            _hotkeys.ResetAll();
        }
    }

    private static async Task<bool> AskToRestoreDefaultsAsync(IContentDialogService dialogs)
    {
        var answer = await dialogs.ShowSimpleDialogAsync(new SimpleContentDialogCreateOptions
        {
            Title = "Restore default shortcuts?",
            Content = "Every shortcut goes back to its default. The ones you recorded yourself are lost.",
            PrimaryButtonText = "Restore",
            CloseButtonText = "Cancel",
        });
        return answer == ContentDialogResult.Primary;
    }

    private void OnBindingsChanged(object? sender, EventArgs e)
    {
        foreach (var row in Groups.SelectMany(g => g.Rows))
        {
            row.Refresh();
        }
    }
}
