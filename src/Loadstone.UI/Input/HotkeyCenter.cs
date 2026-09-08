using Loadstone.Core.Commands;
using Loadstone.Core.Settings;
using Loadstone.UI.Windows;
using Microsoft.Extensions.Logging;

namespace Loadstone.UI.Input;

/// <summary>
/// Owns one global hotkey per <see cref="WindowCommand"/>: resolves the effective chord (custom,
/// else default), registers it, records the registration status and any keyboard-layout warning
/// for Settings, and routes presses to the dispatcher.
/// </summary>
internal sealed class HotkeyCenter : IDisposable
{
    private readonly IHotkeyRegistrar _registrar;
    private readonly ISettingsService _settings;
    private readonly ICommandDispatcher _dispatcher;
    private readonly IKeyboardLayoutProbe _probe;
    private readonly ILogger<HotkeyCenter> _logger;
    private readonly Dictionary<WindowCommand, HotkeyStatus> _status = new();
    private readonly Dictionary<WindowCommand, string?> _layoutWarnings = new();
    private readonly HashSet<WindowCommand> _rejectedTextReported = [];
    private bool _started;
    private bool _suspended;

    public HotkeyCenter(
        IHotkeyRegistrar registrar,
        ISettingsService settings,
        ICommandDispatcher dispatcher,
        IKeyboardLayoutProbe probe,
        ILogger<HotkeyCenter> logger)
    {
        _registrar = registrar;
        _settings = settings;
        _dispatcher = dispatcher;
        _probe = probe;
        _logger = logger;
        _registrar.HotkeyPressed += OnHotkeyPressed;
    }

    /// <summary>Raised after any binding changes, so menus and settings rows can refresh.</summary>
    public event EventHandler? BindingsChanged;

    /// <summary>Registers every command. Safe to call more than once.</summary>
    public void Start()
    {
        if (_started)
        {
            return;
        }

        _started = true;
        RegisterAll();
        foreach (var command in WindowCommands.All)
        {
            _logger.LogInformation("{Command}: {Shortcut} ({State})", command.Id(), EffectiveShortcut(command)?.ToString() ?? "unbound", StatusOf(command).State);
        }
    }

    /// <summary>
    /// The custom chord if the user set one, null if they cleared it or the stored text is not a chord
    /// this app will register, else the default.
    /// </summary>
    public Shortcut? EffectiveShortcut(WindowCommand command)
    {
        if (_settings.Current.Shortcuts.TryGetValue(command.Id(), out var text))
        {
            // IsUsable is the recorder's rule and it has to be applied here too, because the recorder is
            // not the only way a chord gets into settings.json: the file is hand-editable and survives
            // upgrades. A hand-written "restore": "Backspace" parses perfectly well, and registering it
            // would hand Backspace to Loadstone system-wide, in every application, with no way left to
            // type it — including in the Settings window that would undo it.
            return Shortcut.TryParse(text, out var custom) && custom.IsUsable ? custom : null;
        }

        return command.DefaultShortcut();
    }

    public bool IsCustomized(WindowCommand command) => _settings.Current.Shortcuts.ContainsKey(command.Id());

    public HotkeyStatus StatusOf(WindowCommand command) =>
        _status.TryGetValue(command, out var status) ? status : HotkeyStatus.Unbound;

    /// <summary>The character the chord types on the active layout, or null.</summary>
    public string? LayoutWarning(WindowCommand command) => _layoutWarnings.GetValueOrDefault(command);

    /// <summary>Persists and re-registers. Null clears the binding; the default removes the customization.</summary>
    public void SetShortcut(WindowCommand command, Shortcut? shortcut)
    {
        var id = command.Id();
        _settings.Update(s =>
        {
            if (shortcut is null)
            {
                s.Shortcuts[id] = "";
            }
            else if (shortcut == command.DefaultShortcut())
            {
                s.Shortcuts.Remove(id);
            }
            else
            {
                s.Shortcuts[id] = shortcut.ToString();
            }
        });
        RegisterOne(command);
        BindingsChanged?.Invoke(this, EventArgs.Empty);
    }

    public void ResetAll()
    {
        _settings.Update(s => s.Shortcuts.Clear());
        RegisterAll();
        BindingsChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Releases every chord while a recorder is recording, so pressing the current chord records instead of moving a window.</summary>
    public void Suspend()
    {
        if (_suspended)
        {
            return;
        }

        _suspended = true;
        var anyStillLive = false;
        foreach (var command in WindowCommands.All)
        {
            var status = _registrar.Unregister(command);
            if (status.State != HotkeyState.NotReleased)
            {
                continue;
            }

            // A chord that would not come back is still live while the recorder is listening for it, so
            // pressing it moves a window instead of being captured. Say so on the row: leaving the last
            // status in place would show a cheerful "registered" for the one case the user needs to see.
            _status[command] = status;
            anyStillLive = true;
            _logger.LogWarning(
                "{Command}: could not be released for recording (Win32 error {Error}); its chord is still live",
                command.Id(),
                status.Win32Error);
        }

        // Only when something went wrong. Resume() overwrites these statuses with fresh ones, so the
        // window in which a stuck chord can be shown at all is exactly the recording the rows are in the
        // middle of; nothing refreshes on the ordinary path.
        if (anyStillLive)
        {
            BindingsChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public void Resume()
    {
        if (!_suspended)
        {
            return;
        }

        _suspended = false;
        RegisterAll();

        // Registration statuses are only real once the center is running again, so tell the rows to re-read them.
        BindingsChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Dispose()
    {
        _registrar.HotkeyPressed -= OnHotkeyPressed;
    }

    private void RegisterAll()
    {
        foreach (var command in WindowCommands.All)
        {
            RegisterOne(command);
        }
    }

    private void RegisterOne(WindowCommand command)
    {
        ReportRejectedTextOnce(command);
        var shortcut = EffectiveShortcut(command);
        var warning = shortcut is null ? null : _probe.CharacterTypedBy(shortcut);
        _layoutWarnings[command] = warning;

        if (_suspended)
        {
            return;
        }

        if (shortcut is null)
        {
            // Unregister answers Unbound, or NotReleased when Windows kept the chord; either way it is
            // the truth about this command and belongs on the row.
            _status[command] = _registrar.Unregister(command);
            return;
        }

        var status = _registrar.Register(command, shortcut);
        _status[command] = status;
        if (status.State != HotkeyState.Registered)
        {
            _logger.LogWarning("{Command}: {Shortcut} not registered ({State}, Win32 error {Error})", command.Id(), shortcut, status.State, status.Win32Error);
        }

        if (warning is { } character)
        {
            _logger.LogWarning("{Command}: {Shortcut} types '{Character}' on the active keyboard layout", command.Id(), shortcut, character);
        }
    }

    /// <summary>
    /// Stored text this app refuses — text that no longer parses, or that parses into a chord the
    /// recorder would never have produced — is treated as "cleared", which looks the same in the UI as
    /// a binding the user cleared on purpose. Say so once per command so a hand-edited or migrated
    /// settings file is diagnosable from the log. An empty string is the deliberate "cleared" value.
    /// </summary>
    private void ReportRejectedTextOnce(WindowCommand command)
    {
        if (!_settings.Current.Shortcuts.TryGetValue(command.Id(), out var text) || text.Length == 0)
        {
            return;
        }

        if (!Shortcut.TryParse(text, out var stored))
        {
            if (_rejectedTextReported.Add(command))
            {
                _logger.LogWarning("{Command}: stored shortcut \"{Text}\" could not be parsed; treating it as unbound", command.Id(), text);
            }

            return;
        }

        if (!stored.IsUsable && _rejectedTextReported.Add(command))
        {
            _logger.LogWarning(
                "{Command}: stored shortcut \"{Text}\" has no modifier and is not a function key; registering it would take that key away from every application, so treating it as unbound",
                command.Id(),
                text);
        }
    }

    private void OnHotkeyPressed(WindowCommand command)
    {
        if (_suspended)
        {
            return;
        }

        _dispatcher.Run(command);
    }
}
