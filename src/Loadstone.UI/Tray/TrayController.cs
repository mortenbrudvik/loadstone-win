using System.Drawing;
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
using static Loadstone.UI.Interop.NativeMethods;

namespace Loadstone.UI.Tray;

/// <summary>
/// The tray icon and its menu. Right click opens the menu, left click opens Settings. The foreground
/// window is captured just before the menu opens, because opening it makes Loadstone the foreground
/// application, and menu commands act on that captured window.
/// </summary>
internal sealed class TrayController : IDisposable
{
    public const string HelpUrl = "https://github.com/mortenbrudvik/loadstone-win#readme";

    private readonly ICommandDispatcher _dispatcher;
    private readonly HotkeyCenter _hotkeys;
    private readonly IShellActions _shell;
    private readonly ILogger<TrayController> _logger;
    private readonly ContextMenu _menu = new();
    private readonly TaskbarIcon _icon;
    private Icon? _trayIcon;
    private WindowIdentity? _capturedForeground;

    public TrayController(ICommandDispatcher dispatcher, HotkeyCenter hotkeys, IShellActions shell, ILogger<TrayController> logger)
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
        _icon.ForceCreate();

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
        _capturedForeground = FocusedWindow.Resolve() is { } hwnd ? new WindowIdentity(hwnd, Win32Window.ProcessIdOf(hwnd)) : null;

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
            User32.MessageBeep(NativeConstants.MB_OK);
            return;
        }

        _dispatcher.Run(command, captured.Hwnd);
    }

    private void OpenHelp()
    {
        if (!UriLauncher.TryOpen(HelpUrl, _logger))
        {
            User32.MessageBeep(NativeConstants.MB_OK);
        }
    }

    private void ApplyThemeIcon()
    {
        var name = IsTaskbarLight() ? "tray-light.ico" : "tray-dark.ico";
        using var stream = Application.GetResourceStream(new Uri($"pack://application:,,,/Assets/{name}"))!.Stream;
        var icon = new Icon(stream);
        _icon.Icon = icon;
        _trayIcon?.Dispose();
        _trayIcon = icon;
    }
}
