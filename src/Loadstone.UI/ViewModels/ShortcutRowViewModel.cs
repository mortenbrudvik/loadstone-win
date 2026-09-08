using CommunityToolkit.Mvvm.ComponentModel;
using Loadstone.Core.Commands;
using Loadstone.UI.Input;

namespace Loadstone.UI.ViewModels;

/// <summary>One row of the Shortcuts tab: a command, its recorder, and a status caption.</summary>
internal sealed partial class ShortcutRowViewModel : ObservableObject
{
    private readonly HotkeyCenter _hotkeys;
    private bool _refreshing;

    public ShortcutRowViewModel(WindowCommand command, HotkeyCenter hotkeys)
    {
        Command = command;
        _hotkeys = hotkeys;
        Status = "";
        Refresh();
    }

    public WindowCommand Command { get; }

    public string Title => Command.Title();

    // Partial properties take no initializer; the constructor assigns Status before anything reads it.
    [ObservableProperty]
    public partial Shortcut? Shortcut { get; set; }

    [ObservableProperty]
    public partial bool IsRecording { get; set; }

    [ObservableProperty]
    public partial string Status { get; set; }

    /// <summary>Re-reads the effective shortcut and status from the hotkey center without writing anything back.</summary>
    public void Refresh()
    {
        _refreshing = true;
        try
        {
            Shortcut = _hotkeys.EffectiveShortcut(Command);
        }
        finally
        {
            _refreshing = false;
        }

        Status = StatusText();
    }

    partial void OnShortcutChanged(Shortcut? value)
    {
        if (_refreshing)
        {
            return;
        }

        _hotkeys.SetShortcut(Command, value);
        Status = StatusText();
    }

    partial void OnIsRecordingChanged(bool value)
    {
        if (value)
        {
            _hotkeys.Suspend();
        }
        else
        {
            _hotkeys.Resume();
        }
    }

    private string StatusText()
    {
        var status = _hotkeys.StatusOf(Command);
        return status.State switch
        {
            HotkeyState.InUse => SharesItsChordWith() is { } other ? $"Also bound to {other.Title()}" : "In use by another app",
            HotkeyState.Failed => $"Could not register (error {status.Win32Error})",
            _ => _hotkeys.LayoutWarning(Command) is { } character ? $"Types {character} on your keyboard layout" : "",
        };
    }

    /// <summary>
    /// Windows reports a chord already taken by another Loadstone row exactly as it reports one taken by
    /// another application, so name the row instead of blaming an app the user cannot find.
    /// </summary>
    private WindowCommand? SharesItsChordWith()
    {
        if (_hotkeys.EffectiveShortcut(Command) is not { } chord)
        {
            return null;
        }

        foreach (var other in WindowCommands.All)
        {
            if (other != Command && _hotkeys.EffectiveShortcut(other) == chord)
            {
                return other;
            }
        }

        return null;
    }
}
