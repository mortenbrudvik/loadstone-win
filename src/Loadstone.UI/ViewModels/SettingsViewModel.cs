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
        WithoutSideEffects(() => DragSnappingEnabled = settings.Current.DragSnappingEnabled);
        RefreshStartup();
    }

    [ObservableProperty]
    public partial bool DragSnappingEnabled { get; set; }

    [ObservableProperty]
    public partial bool LaunchAtLogin { get; set; }

    [ObservableProperty]
    public partial string LaunchAtLoginMessage { get; set; }

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
    /// The setter re-enters itself (<see cref="RefreshStartup"/> puts the registry's answer back
    /// through this same generated setter), so the guard is what stops a refused write from
    /// recursing forever. The guard is set by hand here rather than through
    /// <see cref="WithoutSideEffects"/> because it has to cover the registry call as well as the
    /// assignments. Nothing catches: <see cref="IStartupRegistration.EnableAsync"/> reports a refusal
    /// instead of throwing, and the rollback is simply re-reading the registry - the only thing that
    /// knows the truth after a write that may have half landed.
    /// </summary>
    partial void OnLaunchAtLoginChanged(bool value)
    {
        if (_applying)
        {
            return;
        }

        _applying = true;
        try
        {
            // Blocking on the seam here is temporary; the toggle becomes an AsyncRelayCommand in the
            // next task, before any implementation that actually awaits is registered.
            var written = (value ? _startup.EnableAsync() : _startup.DisableAsync()).GetAwaiter().GetResult();
            RefreshStartup();
            if (!written)
            {
                LaunchAtLoginMessage = "Could not change Launch at login; Windows refused the registry write.";
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
        WithoutSideEffects(() => LaunchAtLogin = status is StartupStatus.Enabled or StartupStatus.DisabledInTaskManager);

        ShowOpenStartupApps = status == StartupStatus.DisabledInTaskManager;
        LaunchAtLoginMessage = status switch
        {
            StartupStatus.DisabledInTaskManager => "Disabled in Task Manager › Startup apps.",
            StartupStatus.Unknown => "Could not read Launch at login; Windows refused the registry.",
            _ => "",
        };
    }

    /// <summary>
    /// Runs <paramref name="assign"/> with the property setters' side effects off, so showing state
    /// that was just read does not write it straight back. Saves and restores the flag rather than
    /// clearing it, because it nests: <see cref="RefreshStartup"/> also runs from inside
    /// <see cref="OnLaunchAtLoginChanged"/>, which set the flag first and still needs it set when
    /// this returns.
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
