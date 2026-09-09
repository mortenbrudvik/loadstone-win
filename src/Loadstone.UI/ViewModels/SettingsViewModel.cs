using System.IO;
using System.Reflection;
using System.Security;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Loadstone.Core.Platform;
using Loadstone.Core.Settings;
using Loadstone.UI.Services;
using Microsoft.Extensions.Logging;

namespace Loadstone.UI.ViewModels;

/// <summary>The General tab. Every change applies immediately; there is no Save button.</summary>
internal sealed partial class SettingsViewModel : ObservableObject
{
    private readonly ISettingsService _settings;
    private readonly IStartupRegistration _startup;
    private readonly ILogger<SettingsViewModel> _logger;

    /// <summary>
    /// Echo suppression, raised while the view model assigns its own properties so that showing state
    /// it has just read does not write it straight back. Raised and cleared synchronously, by
    /// <see cref="WithoutSideEffects"/> and nowhere else.
    /// </summary>
    private bool _loading;

    /// <summary>
    /// <see cref="SetLaunchAtLogin"/>'s re-entrancy guard, held across the write so a second click
    /// cannot start a second one. Deliberately not the same flag as <see cref="_loading"/>: the
    /// packaged write is an out-of-process call and the dispatcher pumps input for the whole of it,
    /// so anything a suppression flag swallowed in that window would be a real user change. One flag
    /// doing both jobs is how a click on <see cref="DragSnappingEnabled"/> during a launch-at-login
    /// write used to be dropped - silently, and for the lifetime of the process.
    /// </summary>
    private bool _writing;

    public SettingsViewModel(ISettingsService settings, IStartupRegistration startup, ILogger<SettingsViewModel> logger)
    {
        _settings = settings;
        _startup = startup;
        _logger = logger;
        LaunchAtLoginMessage = "";
        LaunchAtLoginActionMessage = "";
        WithoutSideEffects(() => DragSnappingEnabled = settings.Current.DragSnappingEnabled);
        RefreshStartup();
    }

    [ObservableProperty]
    public partial bool DragSnappingEnabled { get; set; }

    [ObservableProperty]
    public partial bool LaunchAtLogin { get; set; }

    /// <summary>
    /// What the platform's status means, recomputed from <see cref="StartupStatus"/> on every
    /// <see cref="RefreshStartup"/>. Deliberately separate from <see cref="LaunchAtLoginActionMessage"/>:
    /// this slot is owned entirely by <see cref="RefreshStartup"/>, which would otherwise blank out a
    /// pending write-failure explanation the next time anything - including a second, redundant
    /// Settings-open trigger while the window is already showing - causes a refresh.
    /// </summary>
    [ObservableProperty]
    public partial string LaunchAtLoginMessage { get; set; }

    /// <summary>
    /// Transient feedback about the toggle's own last action, owned entirely by
    /// <see cref="SetLaunchAtLogin"/>: cleared when an attempt starts, set when the write comes back
    /// false, and never touched by <see cref="RefreshStartup"/>. Kept apart from
    /// <see cref="LaunchAtLoginMessage"/> so that a refresh triggered for an unrelated reason - the
    /// packaged status snapshot being re-read on every Settings open - cannot silently erase the
    /// explanation for a write that just failed.
    /// </summary>
    [ObservableProperty]
    public partial string LaunchAtLoginActionMessage { get; set; }

    [ObservableProperty]
    public partial bool ShowOpenStartupApps { get; set; }

    public string Version { get; } =
        Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+')[0]
        ?? Assembly.GetExecutingAssembly().GetName().Version?.ToString(3)
        ?? "?";

    partial void OnDragSnappingEnabledChanged(bool value)
    {
        if (_loading)
        {
            return;
        }

        _settings.Update(s => s.DragSnappingEnabled = value);
    }

    /// <summary>
    /// The toggle's action. A command and not the generated property hook, because the packaged
    /// implementation awaits a WinRT call: the hook is a void partial method and could only block.
    /// The rollback is still a re-read — the only thing that knows the truth after a write that may
    /// have half landed.
    /// </summary>
    [RelayCommand]
    private async Task SetLaunchAtLogin(bool value)
    {
        if (_writing)
        {
            return;
        }

        _writing = true;
        try
        {
            // Cleared before the write, not after: a fresh attempt should start without whatever an
            // earlier one left behind, whether this one goes on to succeed or fail in turn.
            LaunchAtLoginActionMessage = "";
            var written = value ? await _startup.EnableAsync() : await _startup.DisableAsync();

            // The asynchronous refresh, not the plain one. This is the single place where the state is
            // guaranteed to have just moved, and the packaged implementation's Status is a snapshot
            // from the last InitializeAsync rather than a live query: re-reading that snapshot here
            // would report the pre-write value and flip the switch back after a write that succeeded.
            // The registry path re-reads the registry on every Status get, so there it costs nothing.
            await RefreshStartupAsync();
            if (!written)
            {
                LaunchAtLoginActionMessage = "Could not change Launch at login; Windows refused.";
            }
        }
        finally
        {
            _writing = false;
        }
    }

    [RelayCommand]
    private void OpenStartupApps() => UriLauncher.OpenOrBeep("ms-settings:startupapps", _logger);

    /// <summary>
    /// Creates the directory before opening it. NLog makes it on first write, so on a run where
    /// logging failed — exactly when a user is sent looking — it is not there and the shell would
    /// beep instead of showing anything.
    /// </summary>
    [RelayCommand]
    private void OpenLogFolder()
    {
        try
        {
            Directory.CreateDirectory(LoggingSetup.LogDirectory);
        }
        // Wider than this path is likely to need, on purpose: an escape from a button click on the UI
        // thread ends the process and takes the hotkeys and the user's edge snapping with it, which is
        // the rule IStartupRegistration states about itself twice. CreateDirectory can also answer
        // ArgumentException or NotSupportedException for a path it will not accept.
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SecurityException
            or ArgumentException or NotSupportedException)
        {
            _logger.LogError(ex, "could not create the log directory before opening it");
        }

        UriLauncher.OpenOrBeep(LoggingSetup.LogDirectory, _logger);
    }

    /// <summary>
    /// Re-reads the registry; the launch-at-login state lives there, not in settings.json. Only
    /// <see cref="StartupStatus.Enabled"/> and <see cref="StartupStatus.DisabledInTaskManager"/> tick
    /// the box: <see cref="StartupStatus.Unknown"/> means the key would not open, and a ticked box
    /// the user cannot untick is a worse lie than an unticked one.
    /// </summary>
    public void RefreshStartup()
    {
        var status = _startup.Status;

        // LaunchAtLogin has no setter side effect since the write moved onto SetLaunchAtLogin, so this
        // wrapper suppresses nothing today. Kept deliberately rather than by accident: the assignment
        // is a read being shown, and the day the property grows a hook the wrapper is the difference
        // between showing state and writing it straight back.
        WithoutSideEffects(() => LaunchAtLogin = status is StartupStatus.Enabled
            or StartupStatus.DisabledInTaskManager or StartupStatus.EnabledByPolicy);

        ShowOpenStartupApps = status == StartupStatus.DisabledInTaskManager;
        LaunchAtLoginMessage = status switch
        {
            StartupStatus.DisabledInTaskManager => "Disabled in Task Manager › Startup apps.",
            StartupStatus.DisabledByPolicy => "Disabled by your organization's policy.",
            StartupStatus.EnabledByPolicy => "Enabled by your organization's policy.",
            StartupStatus.Unknown => "Could not read Launch at login.",
            StartupStatus.NotRegistered => "",
            StartupStatus.Enabled => "",
        };

        // Unconditional, and load-bearing. After a refused write the corrective value is often the one
        // the property already holds, and [ObservableProperty] suppresses the notification on an equal
        // value - so the switch would keep the position the click gave it while the platform holds the
        // opposite. The two-way binding in SettingsWindow.xaml makes the ordinary case a real change,
        // but a notification is what the view consumes, so it is raised rather than relied upon.
        OnPropertyChanged(nameof(LaunchAtLogin));
    }

    /// <summary>
    /// Re-reads the platform before showing it. <see cref="IStartupRegistration.Status"/> on the
    /// packaged implementation is a snapshot taken once during <see cref="IStartupRegistration.InitializeAsync"/>,
    /// not a live query, so a user who disables Loadstone in Task Manager while it keeps running would
    /// otherwise still see the toggle switched on the next time they open Settings.
    /// <see cref="IStartupRegistration.InitializeAsync"/> is safely re-callable for exactly this: the
    /// packaged implementation re-fetches the startup task and reassigns its cached copy, and the
    /// registry implementation just re-runs its stale-path check.
    /// </summary>
    public async Task RefreshStartupAsync()
    {
        await _startup.InitializeAsync();
        RefreshStartup();
    }

    /// <summary>
    /// Runs <paramref name="assign"/> with the property setters' side effects off, so showing state
    /// that was just read does not write it straight back. Saves and restores <see cref="_loading"/>
    /// rather than clearing it, so that it nests: a bare clear in an inner call would unsuppress the
    /// outer one for the rest of its assignment.
    /// </summary>
    private void WithoutSideEffects(Action assign)
    {
        var was = _loading;
        _loading = true;
        try
        {
            assign();
        }
        finally
        {
            _loading = was;
        }
    }
}
