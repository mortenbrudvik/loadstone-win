using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Data;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
using Loadstone.UI.Interop;
using Loadstone.UI.ViewModels;
using Loadstone.UI.Windows;
using Microsoft.Extensions.Logging;
using Wpf.Ui;
using Wpf.Ui.Appearance;
using Wpf.Ui.Controls;
using static Loadstone.UI.Interop.NativeMethods;

namespace Loadstone.UI.Views;

/// <summary>Created once and hidden on close; shown again by the tray or a second launch.</summary>
internal partial class SettingsWindow : FluentWindow
{
    private readonly ILogger<SettingsWindow> _logger;

    public SettingsWindow(SettingsViewModel general, ShortcutsViewModel shortcuts, IContentDialogService dialogs, ILogger<SettingsWindow> logger)
    {
        _logger = logger;
        InitializeComponent();
        General = general;
        Shortcuts = shortcuts;
        DataContext = this;

        // The one dialog host in the app. The Shortcuts tab's "Restore defaults" asks through the
        // service before clearing anything, and the host has to be registered before that button
        // can be clicked, which this constructor guarantees: the button lives in this window.
        dialogs.SetDialogHost(DialogHost);
        if (AppIconFrame(32, logger) is { } frame)
        {
            SettingsTitleBar.Icon = new ImageIcon { Source = frame };
        }

        // WPF makes the first Window it constructs Application.MainWindow, and in this app that is
        // the snap preview, built at startup long before this window. WPF-UI's
        // ApplicationThemeManager.Apply runs WindowBackgroundManager.UpdateBackground on MainWindow at
        // every theme apply, which restores an opaque background first and, on builds before 22523,
        // never clears it again - the translucent preview became a solid slab after a theme change.
        // This is the window the theme should dress, so it takes the slot. Null under the test host,
        // which has no Application.
        if (Application.Current is { } app)
        {
            app.MainWindow = this;
        }

        SystemThemeWatcher.Watch(this);
    }

    /// <summary>
    /// The app icon frame closest to <paramref name="pixels"/>, or null when the resource is missing,
    /// so a renamed or dropped asset leaves the window without an icon rather than without a window.
    /// The component-qualified pack URI resolves under the test host too, whose entry assembly is not
    /// Loadstone.
    /// </summary>
    private static BitmapFrame? AppIconFrame(int pixels, ILogger logger)
    {
        try
        {
            var decoder = new IconBitmapDecoder(
                new Uri("pack://application:,,,/Loadstone;component/Assets/loadstone.ico"),
                BitmapCreateOptions.DelayCreation,
                BitmapCacheOption.OnDemand);
            return decoder.Frames.OrderBy(f => Math.Abs(f.PixelWidth - pixels)).FirstOrDefault();
        }
        catch (Exception ex) when (ex is IOException or NotSupportedException or FileFormatException or UriFormatException)
        {
            logger.LogError(ex, "could not load the app icon for the Settings title bar; the window opens without one");
            return null;
        }
    }

    /// <summary>
    /// Takes the maximize box out of the window style. ShowMaximize="False" on the title bar only
    /// hides the button: the style bit is what the shell consults for a title-bar double-click and
    /// Win+Up, and WPF sets it for every ResizeMode that allows resizing, which this window needs for
    /// its minimum size. Done here because the style word exists only once the HWND does.
    /// </summary>
    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        var hwnd = new WindowInteropHelper(this).Handle;
        if (WindowStyles.StyleWord(hwnd, NativeConstants.GWL_STYLE) is not { } style)
        {
            _logger.LogError("could not read the Settings window style; the maximize box stays");
            return;
        }

        // Like the read, SetWindowLongPtr returns 0 both for a previous value of 0 and for failure, so
        // the thread's last error - cleared first - is what tells the two apart.
        Marshal.SetLastSystemError(0);
        var previous = User32.SetWindowLongPtr(hwnd, NativeConstants.GWL_STYLE, (nint)(style & ~NativeConstants.WS_MAXIMIZEBOX));
        var error = Marshal.GetLastWin32Error();
        if (previous == 0 && error != 0)
        {
            _logger.LogError("could not remove the maximize box from the Settings window: error {Error}", error);
            return;
        }

        // Style changes are cached until the frame is told they happened.
        _ = User32.SetWindowPos(hwnd, 0, 0, 0, 0, 0,
            NativeConstants.SWP_NOMOVE | NativeConstants.SWP_NOSIZE | NativeConstants.SWP_NOZORDER | NativeConstants.SWP_NOACTIVATE | NativeConstants.SWP_FRAMECHANGED);
    }

    public SettingsViewModel General { get; }

    public ShortcutsViewModel Shortcuts { get; }

    /// <summary>
    /// Brings the window to the front and shows the current <see cref="IStartupRegistration.Status"/>.
    /// This read does not re-fetch a packaged startup task; <see cref="SettingsViewModel.RefreshStartupAsync"/> does.
    /// </summary>
    public void ShowAndActivate()
    {
        General.RefreshStartup();
        Show();
        if (WindowState == WindowState.Minimized)
        {
            WindowState = WindowState.Normal;
        }

        Activate();
    }

    /// <summary>
    /// Hides instead of closing, because the window is created once and reused. <c>base.OnClosing</c>
    /// runs first so the public <see cref="Window.Closing"/> event still fires; skipping it made that
    /// event dead for every subscriber. A subscriber that cancels is honoured rather than hidden.
    /// </summary>
    protected override void OnClosing(CancelEventArgs e)
    {
        base.OnClosing(e);
        if (e.Cancel)
        {
            return;
        }

        e.Cancel = true;
        Hide();
    }
}

/// <summary>Visible for a non-empty string, collapsed otherwise.</summary>
public sealed class StringToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        string.IsNullOrEmpty(value as string) ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
