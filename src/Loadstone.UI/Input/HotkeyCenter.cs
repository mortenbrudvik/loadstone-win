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
    private readonly HashSet<WindowCommand> _unparsableReported = [];
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

    /// <summary>The custom chord if the user set one, null if they cleared it, else the default.</summary>
    public Shortcut? EffectiveShortcut(WindowCommand command)
    {
        if (_settings.Current.Shortcuts.TryGetValue(command.Id(), out var text))
        {
            return Shortcut.TryParse(text, out var custom) ? custom : null;
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
        foreach (var command in WindowCommands.All)
        {
            _registrar.Unregister(IdOf(command));
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
        var id = IdOf(command);
        ReportUnparsableTextOnce(command);
        var shortcut = EffectiveShortcut(command);
        _layoutWarnings[command] = shortcut is null ? null : _probe.CharacterTypedBy(shortcut);

        if (_suspended)
        {
            return;
        }

        if (shortcut is null)
        {
            _registrar.Unregister(id);
            _status[command] = HotkeyStatus.Unbound;
            return;
        }

        var status = _registrar.Register(id, shortcut);
        _status[command] = status;
        if (status.State != HotkeyState.Registered)
        {
            _logger.LogWarning("{Command}: {Shortcut} not registered ({State}, Win32 error {Error})", command.Id(), shortcut, status.State, status.Win32Error);
        }

        if (_layoutWarnings[command] is { } character)
        {
            _logger.LogWarning("{Command}: {Shortcut} types '{Character}' on the active keyboard layout", command.Id(), shortcut, character);
        }
    }

    /// <summary>
    /// Stored text that no longer parses is treated as "cleared", which looks the same in the UI as a
    /// binding the user cleared on purpose. Say so once per command so a hand-edited or migrated
    /// settings file is diagnosable from the log. An empty string is the deliberate "cleared" value.
    /// </summary>
    private void ReportUnparsableTextOnce(WindowCommand command)
    {
        if (!_settings.Current.Shortcuts.TryGetValue(command.Id(), out var text)
            || text.Length == 0
            || Shortcut.TryParse(text, out _)
            || !_unparsableReported.Add(command))
        {
            return;
        }

        _logger.LogWarning("{Command}: stored shortcut \"{Text}\" could not be parsed; treating it as unbound", command.Id(), text);
    }

    /// <summary>Hotkey ids are 1-based so that id 0, which Windows also uses for "no id", never appears.</summary>
    private static int IdOf(WindowCommand command) => (int)command + 1;

    private void OnHotkeyPressed(int id)
    {
        if (_suspended)
        {
            return;
        }

        var command = (WindowCommand)(id - 1);
        if (Enum.IsDefined(command))
        {
            _dispatcher.Run(command);
        }
    }
}
