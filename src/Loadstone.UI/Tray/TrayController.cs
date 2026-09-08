using System.Diagnostics;
using System.Drawing;
using System.Windows;
using System.Windows.Controls;
using CommunityToolkit.Mvvm.Input;
using H.NotifyIcon;
using H.NotifyIcon.Core;
using Loadstone.Core.Commands;
using Loadstone.UI.Input;
using Loadstone.UI.Windows;
using Microsoft.Win32;

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
    private readonly ContextMenu _menu = new();
    private readonly TaskbarIcon _icon;
    private Icon? _trayIcon;
    private nint _capturedForeground;

    public TrayController(ICommandDispatcher dispatcher, HotkeyCenter hotkeys, IShellActions shell)
    {
        _dispatcher = dispatcher;
        _hotkeys = hotkeys;
        _shell = shell;

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

    private void OnPreviewTrayContextMenuOpen(object? sender, RoutedEventArgs e) => _capturedForeground = FocusedWindow.Resolve() ?? 0;

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

    private void RunCaptured(WindowCommand command)
    {
        if (_capturedForeground != 0)
        {
            _dispatcher.Run(command, _capturedForeground);
        }
        else
        {
            _dispatcher.Run(command);
        }
    }

    private static void OpenHelp() => Process.Start(new ProcessStartInfo(HelpUrl) { UseShellExecute = true });

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
