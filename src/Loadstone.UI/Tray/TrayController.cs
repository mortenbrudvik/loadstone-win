using System.ComponentModel;
using System.Drawing;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using CommunityToolkit.Mvvm.Input;
using H.NotifyIcon;
using H.NotifyIcon.Core;
using Loadstone.Core.Commands;
using Loadstone.Core.Windows;
using Loadstone.UI.Input;
using Loadstone.UI.Interop;
using Loadstone.UI.Services;
using Loadstone.UI.Windows;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;

namespace Loadstone.UI.Tray;

/// <summary>
/// The tray icon and its menu. Right click opens the menu, left click opens Settings. The foreground
/// window is captured just before the menu opens, because opening it makes Loadstone the foreground
/// application, and menu commands act on that captured window.
/// </summary>
internal sealed class TrayController : IDisposable
{
    public const string HelpUrl = "https://mortenbrudvik.github.io/loadstone-site/help/";

    private readonly ICommandDispatcher _dispatcher;
    private readonly HotkeyCenter _hotkeys;
    private readonly ShellActions _shell;
    private readonly ILogger<TrayController> _logger;
    private readonly ContextMenu _menu = new();
    private readonly TaskbarIcon _icon;
    private Icon? _trayIcon;
    private WindowIdentity? _capturedForeground;

    public TrayController(ICommandDispatcher dispatcher, HotkeyCenter hotkeys, ShellActions shell, ILogger<TrayController> logger)
    {
        _dispatcher = dispatcher;
        _hotkeys = hotkeys;
        _shell = shell;
        _logger = logger;

        Rebuild();
        _icon = new TaskbarIcon
        {
            ToolTipText = "Loadstone",
            ContextMenu = _menu,
            MenuActivation = PopupActivationMode.RightClick,
        };
        ApplyThemeIcon();

        try
        {
            _icon.ForceCreate();
        }
        catch (Exception ex) when (ex is InvalidOperationException or Win32Exception)
        {
            // Shell_NotifyIcon refuses while Explorer is restarting, which is exactly when a
            // launch-at-login app starts. Hotkeys and drag snapping do not need the icon, so losing it
            // must not take the process down; H.NotifyIcon re-adds the icon on TaskbarCreated anyway.
            _logger.LogError(ex, "tray: could not create the tray icon");
        }

        _icon.TrayLeftMouseUp += OnTrayLeftMouseUp;
        _icon.PreviewTrayContextMenuOpen += OnPreviewTrayContextMenuOpen;
        _hotkeys.BindingsChanged += OnBindingsChanged;
        SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
    }

    /// <summary>Light taskbar means a dark glyph. Read from the personalization key Windows keeps for the system theme.</summary>
    public static bool IsTaskbarLight()
    {
        using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
        return key?.GetValue("SystemUsesLightTheme") is int value && value == 1;
    }

    public void Dispose()
    {
        SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
        _hotkeys.BindingsChanged -= OnBindingsChanged;
        _icon.TrayLeftMouseUp -= OnTrayLeftMouseUp;
        _icon.PreviewTrayContextMenuOpen -= OnPreviewTrayContextMenuOpen;
        _icon.Dispose();
        _trayIcon?.Dispose();
    }

    private void OnTrayLeftMouseUp(object? sender, RoutedEventArgs e) => _shell.OpenSettings();

    private void OnPreviewTrayContextMenuOpen(object? sender, RoutedEventArgs e) =>
        _capturedForeground = FocusedWindow.Resolve() is { } hwnd ? Win32Window.For(hwnd)?.Identity : null;

    private void OnBindingsChanged(object? sender, EventArgs e) => Rebuild();

    private void OnUserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
    {
        if (e.Category == UserPreferenceCategory.General)
        {
            Application.Current?.Dispatcher.BeginInvoke(ApplyThemeIcon);
        }
    }

    private void Rebuild()
    {
        _menu.Items.Clear();
        foreach (var entry in TrayMenuBuilder.Build(_hotkeys.EffectiveShortcut))
        {
            _menu.Items.Add(ToItem(entry));
        }
    }

    private object ToItem(TrayMenuEntry entry) => entry.Kind switch
    {
        TrayMenuEntryKind.Separator => new Separator(),
        TrayMenuEntryKind.Command => new MenuItem
        {
            Header = entry.Title,
            InputGestureText = entry.Gesture,
            // Non-null for a Command entry: TrayMenuEntry.ForCommand is the only way to make one.
            Command = new RelayCommand(() => RunCaptured(entry.Command!.Value)),
        },
        TrayMenuEntryKind.Settings => new MenuItem { Header = entry.Title, Command = new RelayCommand(_shell.OpenSettings) },
        TrayMenuEntryKind.Help => new MenuItem { Header = entry.Title, Command = new RelayCommand(OpenHelp) },
        TrayMenuEntryKind.Quit => new MenuItem { Header = entry.Title, Command = new RelayCommand(_shell.Quit) },
    };

    /// <summary>
    /// The captured window may have closed while the menu was open, and Windows reuses handles quickly,
    /// so the identity is re-checked before the command runs: acting on a recycled handle would move a
    /// window that was never the user's target.
    /// </summary>
    private void RunCaptured(WindowCommand command)
    {
        if (_capturedForeground is not { } captured)
        {
            _dispatcher.Run(command);
            return;
        }

        if (!WindowLiveness.IsAlive(captured) || !WindowStyles.IsManageable(WindowStyles.Snapshot(captured.Hwnd), Environment.ProcessId))
        {
            _logger.LogInformation("tray: captured window is gone or not manageable, skipped");
            Feedback.Beep();
            return;
        }

        _dispatcher.Run(command, captured);
    }

    private void OpenHelp() => UriLauncher.OpenOrBeep(HelpUrl, _logger);

    /// <summary>
    /// Follows the taskbar theme. A missing or renamed asset must not be fatal here: this runs from the
    /// constructor, where a throw kills startup, and from the theme-change callback, where a throw kills a
    /// running app whose only UI is this icon. Log it and keep whatever icon is already showing.
    /// </summary>
    private void ApplyThemeIcon()
    {
        var name = IsTaskbarLight() ? "tray-light.ico" : "tray-dark.ico";
        try
        {
            var resource = Application.GetResourceStream(new Uri($"pack://application:,,,/Assets/{name}"));
            if (resource is null)
            {
                _logger.LogError("tray: icon asset {Name} is missing from the package", name);
                return;
            }

            using var stream = resource.Stream;
            var icon = new Icon(stream);
            _icon.Icon = icon;
            _trayIcon?.Dispose();
            _trayIcon = icon;
        }
        catch (Exception ex) when (ex is IOException or ArgumentException)
        {
            // IOException: WPF cannot locate the pack resource. ArgumentException: the stream is not an icon.
            _logger.LogError(ex, "tray: could not load icon asset {Name}", name);
        }
    }
}
