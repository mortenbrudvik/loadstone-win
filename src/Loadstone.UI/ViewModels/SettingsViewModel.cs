using System.Reflection;
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
    private bool _applying;

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
        if (_applying)
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
        if (_applying)
        {
            return;
        }

        _applying = true;
        try
        {
            // Cleared before the write, not after: a fresh attempt should start without whatever an
            // earlier one left behind, whether this one goes on to succeed or fail in turn.
            LaunchAtLoginActionMessage = "";
            var written = value ? await _startup.EnableAsync() : await _startup.DisableAsync();
            RefreshStartup();
            if (!written)
            {
                LaunchAtLoginActionMessage = "Could not change Launch at login; Windows refused.";
            }
        }
        finally
        {
            _applying = false;
        }
    }

    [RelayCommand]
    private void OpenStartupApps() => UriLauncher.OpenOrBeep("ms-settings:startupapps", _logger);

    /// <summary>
    /// Re-reads the registry; the launch-at-login state lives there, not in settings.json. Only
    /// <see cref="StartupStatus.Enabled"/> and <see cref="StartupStatus.DisabledInTaskManager"/> tick
    /// the box: <see cref="StartupStatus.Unknown"/> means the key would not open, and a ticked box
    /// the user cannot untick is a worse lie than an unticked one.
    /// </summary>
    public void RefreshStartup()
    {
        var status = _startup.Status;
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
    /// that was just read does not write it straight back. Saves and restores the flag rather than
    /// clearing it, because it nests: <see cref="RefreshStartup"/> also runs from inside
    /// <see cref="SetLaunchAtLogin"/>, which set the flag first and still needs it set when this
    /// returns.
    /// </summary>
    private void WithoutSideEffects(Action assign)
    {
        var was = _applying;
        _applying = true;
        try
        {
            assign();
        }
        finally
        {
            _applying = was;
        }
    }
}
