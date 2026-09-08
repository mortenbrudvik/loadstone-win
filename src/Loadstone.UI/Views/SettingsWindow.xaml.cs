using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using Loadstone.UI.ViewModels;
using Wpf.Ui.Appearance;
using Wpf.Ui.Controls;

namespace Loadstone.UI.Views;

/// <summary>Created once and hidden on close; shown again by the tray or a second launch.</summary>
internal partial class SettingsWindow : FluentWindow
{
    public SettingsWindow(SettingsViewModel general, ShortcutsViewModel shortcuts)
    {
        InitializeComponent();
        General = general;
        Shortcuts = shortcuts;
        DataContext = this;
        SystemThemeWatcher.Watch(this);
    }

    public SettingsViewModel General { get; }

    public ShortcutsViewModel Shortcuts { get; }

    /// <summary>Brings the window to the front, re-reading the registry-backed state first.</summary>
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
