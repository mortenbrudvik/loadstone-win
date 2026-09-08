using System.IO;
using System.Reflection;
using System.Security;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Loadstone.Core.Platform;
using Loadstone.Core.Settings;
using Loadstone.UI.Interop;
using Loadstone.UI.Services;
using Microsoft.Extensions.Logging;
using static Loadstone.UI.Interop.NativeMethods;

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
        _applying = true;
        DragSnappingEnabled = settings.Current.DragSnappingEnabled;
        _applying = false;
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
    /// The setter re-enters itself on rollback (the assignment below goes through the same
    /// generated setter), so the guard is what stops a failing Enable from recursing forever.
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
            if (value)
            {
                _startup.Enable();
            }
            else
            {
                _startup.Disable();
            }

            RefreshStartup();
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or InvalidOperationException or SecurityException)
        {
            _logger.LogError(ex, "Launch at login {Action} failed", value ? "enable" : "disable");
            LaunchAtLoginMessage = $"Could not change Launch at login: {ex.Message}";
            LaunchAtLogin = _startup.Status != StartupStatus.NotRegistered;
        }
        finally
        {
            _applying = false;
        }
    }

    [RelayCommand]
    private void OpenStartupApps()
    {
        if (!UriLauncher.TryOpen("ms-settings:startupapps", _logger))
        {
            User32.MessageBeep(NativeConstants.MB_OK);
        }
    }

    /// <summary>Re-reads the registry; the launch-at-login state lives there, not in settings.json.</summary>
    public void RefreshStartup()
    {
        var status = _startup.Status;
        var wasApplying = _applying;
        _applying = true;
        try
        {
            LaunchAtLogin = status != StartupStatus.NotRegistered;
        }
        finally
        {
            _applying = wasApplying;
        }

        ShowOpenStartupApps = status == StartupStatus.DisabledInTaskManager;
        LaunchAtLoginMessage = status == StartupStatus.DisabledInTaskManager
            ? "Disabled in Task Manager › Startup apps."
            : "";
    }
}
