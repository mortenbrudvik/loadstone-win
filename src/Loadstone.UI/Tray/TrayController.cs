using System.ComponentModel;
using System.Drawing;
using System.IO;
using System.Security;
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
using Wpf.Ui.Appearance;
using static Loadstone.UI.Interop.NativeMethods;

namespace Loadstone.UI.Tray;

/// <summary>
/// The tray icon and its menu. Right click opens the menu, left click opens Settings. Menu commands
/// act on the window captured just before the menu opens, and that is not whatever is in front by
/// then: the right-click has already handed the foreground to the taskbar or its hidden-icons flyout,
/// and opening the menu makes Loadstone the foreground application after that. So the capture asks
/// <see cref="ForegroundHistory"/> which window was in front before the tray, if Loadstone may act on
/// it. Constructing builds
/// the menu and the icon object; <see cref="Start"/> is what puts the icon in the tray and starts
/// following the foreground, like the other services that construct inert and start on request.
/// </summary>
internal sealed class TrayController : IDisposable
{
    public const string HelpUrl = "https://brudvik.com/loadstone/windows/help";

    private readonly ICommandDispatcher _dispatcher;
    private readonly HotkeyCenter _hotkeys;
    private readonly ShellActions _shell;
    private readonly ILogger<TrayController> _logger;
    private readonly ContextMenu _menu = new();
    private readonly TaskbarIcon _icon;
    private readonly Action _applySystemTheme;
    private readonly Func<WindowIdentity?>? _suppliedForeground;
    private readonly ForegroundHistory? _history;
    private readonly Action _createIcon;
    private Icon? _trayIcon;
    private WindowIdentity? _capturedForeground;
    private bool _started;

    public TrayController(ICommandDispatcher dispatcher, HotkeyCenter hotkeys, ShellActions shell, ILogger<TrayController> logger)
        : this(dispatcher, hotkeys, shell, logger, ApplicationThemeManager.ApplySystemTheme, history: new ForegroundHistory(logger))
    {
    }

    /// <summary>
    /// The theme apply as a seam. ApplicationThemeManager.Apply raises a static event that every
    /// FluentWindow in the process handles on its own thread, so a test that called the real one
    /// after another test's window existed died of cross-thread access; the test checks the wiring
    /// and leaves the apply to WPF-UI.
    /// </summary>
    internal TrayController(
        ICommandDispatcher dispatcher,
        HotkeyCenter hotkeys,
        ShellActions shell,
        ILogger<TrayController> logger,
        Action applySystemTheme,
        Func<WindowIdentity?>? foreground = null,
        ForegroundHistory? history = null,
        Action? createIcon = null)
    {
        _dispatcher = dispatcher;
        _hotkeys = hotkeys;
        _shell = shell;
        _logger = logger;
        _applySystemTheme = applySystemTheme;
        _history = history;
        _suppliedForeground = foreground;

        Rebuild();
        _icon = new TaskbarIcon
        {
            ToolTipText = "Loadstone",
            ContextMenu = _menu,
            MenuActivation = PopupActivationMode.RightClick,
        };
        _createIcon = createIcon ?? (() => _icon.ForceCreate());
        ApplyThemeIcon();

        _icon.TrayLeftMouseUp += OnTrayLeftMouseUp;
        _icon.PreviewTrayContextMenuOpen += OnPreviewTrayContextMenuOpen;
        _hotkeys.BindingsChanged += OnBindingsChanged;
    }

    /// <summary>Puts the icon in the tray and follows the taskbar theme, the display scale, and the window in front from then on. Safe to call more than once.</summary>
    public void Start()
    {
        if (_started)
        {
            return;
        }

        _started = true;
        try
        {
            _createIcon();
        }
        catch (Exception ex) when (ex is InvalidOperationException or Win32Exception)
        {
            // Shell_NotifyIcon refuses while Explorer is restarting, which is exactly when a
            // launch-at-login app starts. Hotkeys and drag snapping do not need the icon, so losing it
            // must not take the process down; H.NotifyIcon re-adds the icon on TaskbarCreated anyway.
            _logger.LogError(ex, "tray: could not create the tray icon");
        }

        SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
        SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;
        _history?.Install();
    }

    /// <summary>Light taskbar means a dark glyph. Read from the personalization key Windows keeps for the system theme.</summary>
    public static bool IsTaskbarLight() =>
        IsTaskbarLight(() => Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize"));

    /// <summary>
    /// The same, over whatever opens the key. A policy or an ACL can refuse an HKCU read - the
    /// registry startup registration guards the identical call - and this runs from the constructor,
    /// where a throw ends startup. The dark glyph is the fallback because it reads on the default
    /// taskbar.
    /// </summary>
    internal static bool IsTaskbarLight(Func<RegistryKey?> openPersonalize)
    {
        try
        {
            using var key = openPersonalize();
            return key?.GetValue("SystemUsesLightTheme") is int value && value == 1;
        }
        catch (Exception ex) when (ex is SecurityException or UnauthorizedAccessException or IOException)
        {
            return false;
        }
    }

    /// <summary>
    /// The frame for the tray, which the shell draws at the small-icon size. <c>new Icon(stream)</c>
    /// picks the frame nearest SM_CXICON - 32 px at 100 percent, 48 at 150 - so the 16, 20 and 24 px
    /// frames drawn for the tray were never used and the shell scaled a larger one down.
    /// </summary>
    internal static Icon SmallTrayIcon(Stream stream) =>
        new Icon(stream, new System.Drawing.Size(User32.GetSystemMetrics(NativeConstants.SM_CXSMICON), User32.GetSystemMetrics(NativeConstants.SM_CYSMICON)));

    public void Dispose()
    {
        SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged;
        SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
        _hotkeys.BindingsChanged -= OnBindingsChanged;
        _icon.TrayLeftMouseUp -= OnTrayLeftMouseUp;
        _icon.PreviewTrayContextMenuOpen -= OnPreviewTrayContextMenuOpen;
        _icon.Dispose();
        _trayIcon?.Dispose();
        _history?.Dispose();
    }

    private void OnTrayLeftMouseUp(object? sender, RoutedEventArgs e) => _shell.OpenSettings();

    private void OnPreviewTrayContextMenuOpen(object? sender, RoutedEventArgs e) => CaptureForeground();

    /// <summary>The menu the icon opens. Tests invoke its items; the shell is what shows it.</summary>
    internal ContextMenu Menu => _menu;

    /// <summary>What the menu asks for the window to act on; null when the internal constructor was given none.</summary>
    internal ForegroundHistory? History => _history;

    /// <summary>Same capture the icon's preview event runs, so a test can open the menu without a click.</summary>
    internal WindowIdentity? CaptureForeground() =>
        _capturedForeground = _suppliedForeground is { } supplied ? supplied() : WindowBeforeTheTray();

    /// <summary>
    /// The window the menu's commands act on, decided once and logged from that same decision: the
    /// window a menu command moves is chosen here, out of sight, and a command that beeps or moves the
    /// wrong window can only be explained by this line.
    /// </summary>
    private WindowIdentity? WindowBeforeTheTray()
    {
        if (_history is not { IsInstalled: true })
        {
            // Without the hook, whatever is in front now is all there is, which is the tray, so the
            // commands beep as they did before the history existed.
            var fallback = FocusedWindow.Resolve() is { } hwnd ? Win32Window.For(hwnd)?.Identity : null;
            _logger.LogDebug("tray: menu opened without the foreground hook; its commands act on the foreground as it opened, {Target}", fallback?.ToString() ?? "nothing Loadstone moves");
            return fallback;
        }

        var (window, decider) = _history.Decide();
        if (window is { } chosen && Win32Window.For(chosen)?.Identity is { } captured)
        {
            _logger.LogDebug("tray: menu opened; its commands act on window {Hwnd} of pid {Pid}", captured.Hwnd, captured.Pid);
            return captured;
        }

        if (decider is { } d)
        {
            // The window that decided it, so a beep can be told apart: the desktop, a toolbar, a
            // window minimized since, or one that went away as the menu opened.
            var why = window is not null ? "gone" : User32.IsIconic(d) ? "minimized" : "not one Loadstone moves";
            _logger.LogDebug(
                "tray: menu opened with no window for its commands to act on; the window in front before the tray, {Hwnd} ({Class}), is {Why}",
                d,
                WindowStyles.Snapshot(d).ClassName,
                why);
        }
        else
        {
            _logger.LogDebug("tray: menu opened with no window for its commands to act on; none of the windows recently in front is still on screen");
        }

        return null;
    }

    private void OnBindingsChanged(object? sender, EventArgs e) => Rebuild();

    private void OnUserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
    {
        if (e.Category == UserPreferenceCategory.General)
        {
            Application.Current?.Dispatcher.BeginInvoke(FollowSystemTheme);
        }
    }

    /// <summary>
    /// Brings the app theme and the icon to the system's. The theme is applied once at startup and is
    /// otherwise followed only by the Settings window's watcher, which exists once Settings has been
    /// opened: a launch-at-login session that never opened it kept the menu - drawn from the WPF-UI
    /// theme dictionaries - in the startup theme after a light/dark switch. Applying it here as well
    /// is idempotent once the watcher is live. WPF-UI dresses Application.MainWindow on every apply,
    /// which is why the Settings window, not the snap preview, holds that slot.
    /// </summary>
    internal void FollowSystemTheme()
    {
        _applySystemTheme();
        ApplyThemeIcon();
    }

    /// <summary>The small-icon metric moves with the display scale, so the frame is chosen again.</summary>
    private void OnDisplaySettingsChanged(object? sender, EventArgs e) => Application.Current?.Dispatcher.BeginInvoke(ApplyThemeIcon);

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
    /// running app whose only UI is this icon. Log it and keep whatever icon is already showing. The
    /// component-qualified pack URI resolves under the test host too, whose entry assembly is not Loadstone.
    /// </summary>
    private void ApplyThemeIcon()
    {
        var name = IsTaskbarLight() ? "tray-light.ico" : "tray-dark.ico";
        try
        {
            var resource = Application.GetResourceStream(new Uri($"pack://application:,,,/Loadstone;component/Assets/{name}"));
            if (resource is null)
            {
                _logger.LogError("tray: icon asset {Name} is missing from the package", name);
                return;
            }

            using var stream = resource.Stream;
            var icon = SmallTrayIcon(stream);
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
