using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using Loadstone.Core.Geometry;
using Loadstone.UI.Interop;
using Loadstone.UI.Windows;
using Microsoft.Extensions.Logging;
using static Loadstone.UI.Interop.NativeMethods;
using Rect = Loadstone.Core.Geometry.Rect;

namespace Loadstone.UI.Input;

/// <summary>
/// The translucent rectangle, painted in the system accent colour - or the High Contrast theme's
/// highlight - shown over the tile a drag will snap to, and re-read on every show, because Windows
/// changes both mid-session. Borderless, click-through, never activated, topmost, placed with
/// SetWindowPos in physical pixels because WPF's own placement is wrong on mixed-DPI desks. Only ever
/// hidden, never closed.
/// </summary>
internal sealed class SnapPreview : Window, ISnapPreview
{
    /// <summary>How far the preview is inset on each side so it reads as a preview rather than as the window itself.</summary>
    private const int Inset = 6;

    private readonly ILogger<SnapPreview> _logger;
    private readonly Border _fill;
    private Color _accent;
    private bool _highContrast;
    private bool _placementFailureLogged;

    public SnapPreview(ILogger<SnapPreview> logger)
    {
        _logger = logger;

        // WPF makes the first Window constructed Application.MainWindow, and at startup that is this
        // one. WPF-UI's ApplicationThemeManager.Apply dresses MainWindow with an opaque background and
        // a Mica backdrop at every theme apply, which on builds before 22523 left this translucent
        // window a solid slab. The slot is refused here and taken by SettingsWindow when it is built;
        // Application.Current is null under the test host.
        if (Application.Current is { } app && ReferenceEquals(app.MainWindow, this))
        {
            app.MainWindow = null;
        }

        Title = "Loadstone snap preview";
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ShowInTaskbar = false;
        ShowActivated = false;
        ResizeMode = ResizeMode.NoResize;
        Focusable = false;
        IsHitTestVisible = false;
        Topmost = true;
        Left = -10000;
        Top = -10000;
        Width = 1;
        Height = 1;

        _fill = new Border
        {
            CornerRadius = new CornerRadius(10),
            BorderThickness = new Thickness(2),
            Margin = new Thickness(1),
        };
        _accent = SystemParameters.WindowGlassColor;
        _highContrast = SystemParameters.HighContrast;
        Paint(_accent, _highContrast);
        Content = _fill;

        SourceInitialized += (_, _) => MakeClickThrough();
    }

    private nint Handle => new WindowInteropHelper(this).EnsureHandle();

    public void Show(Rect frame)
    {
        // Re-read the accent rather than keep the one this window was born with: Windows changes it
        // mid-session, from Settings or by following the wallpaper, and a preview stuck on the old
        // colour until the next restart is the sort of thing nobody thinks to report. High Contrast
        // is a mid-session switch too (Left Alt + Left Shift + Print Screen).
        var accent = SystemParameters.WindowGlassColor;
        var highContrast = SystemParameters.HighContrast;
        if (accent != _accent || highContrast != _highContrast)
        {
            _accent = accent;
            _highContrast = highContrast;
            Paint(accent, highContrast);
        }

        var inset = frame.Inflate(-Inset, -Inset);
        if (inset.IsEmpty)
        {
            // Inflate has no floor, so a tile under 2 * Inset wide or tall comes back inverted and the
            // negative width would go straight to SetWindowPos. Nothing readable fits there anyway.
            Hide();
            return;
        }

        if (!IsVisible)
        {
            Show();
        }

        // Topmost is asserted by this process on its own window, which Windows honors even for a
        // background process; a request from another process would be refused.
        if (User32.SetWindowPos(Handle, NativeConstants.HWND_TOPMOST, inset.X, inset.Y, inset.Width, inset.Height,
                NativeConstants.SWP_NOACTIVATE | NativeConstants.SWP_SHOWWINDOW))
        {
            _placementFailureLogged = false;
            return;
        }

        // The window is still parked at -10000, so there is no preview at all while the drop goes on
        // to commit. Worth a line, but only one: this runs 60 times a second during a drag.
        if (!_placementFailureLogged)
        {
            _placementFailureLogged = true;
            _logger.LogError("SetWindowPos could not place the snap preview at {Frame}: error {Error}", inset, Marshal.GetLastWin32Error());
        }
    }

    public new void Hide()
    {
        if (IsVisible)
        {
            base.Hide();
        }
    }

    private void Paint(Color accent, bool highContrast)
    {
        var (fill, border) = Colours(accent, highContrast);
        _fill.Background = new SolidColorBrush(fill);
        _fill.BorderBrush = new SolidColorBrush(border);
    }

    /// <summary>
    /// The fill and border for the preview. Normally a 22 percent tint of the accent with an 85 percent
    /// border, so the window beneath still reads through it. In High Contrast a 22 percent tint of the
    /// DWM colour can be near-invisible on a black desktop, so the theme's own highlight colour is
    /// used instead, with a solid border and a heavier fill.
    /// </summary>
    internal static (Color Fill, Color Border) Colours(Color accent, bool highContrast)
    {
        if (highContrast)
        {
            var highlight = SystemColors.HighlightColor;
            return (Color.FromArgb(102, highlight.R, highlight.G, highlight.B), Color.FromArgb(255, highlight.R, highlight.G, highlight.B)); // 40 percent, solid
        }

        return (Color.FromArgb(56, accent.R, accent.G, accent.B), Color.FromArgb(217, accent.R, accent.G, accent.B)); // 22 percent, 85 percent
    }

    /// <summary>
    /// WS_EX_TRANSPARENT is what stops the preview swallowing the click that ends the drag, so a
    /// failed write here is not cosmetic: a topmost, opaque window would sit over the drop target for
    /// the whole drag. Like the read, SetWindowLongPtr returns 0 both for a previous value of 0 and
    /// for failure, so the thread's last error - cleared first - is what tells the two apart.
    /// </summary>
    private void MakeClickThrough()
    {
        var handle = Handle;
        if (WindowStyles.StyleWord(handle, NativeConstants.GWL_EXSTYLE) is not { } exStyle)
        {
            _logger.LogError("could not read the snap preview's extended style; it may swallow the click that ends a drag");
            return;
        }

        var wanted = (nint)exStyle | (nint)(NativeConstants.WS_EX_TRANSPARENT | NativeConstants.WS_EX_TOOLWINDOW | NativeConstants.WS_EX_NOACTIVATE);
        Marshal.SetLastSystemError(0);
        var previous = User32.SetWindowLongPtr(handle, NativeConstants.GWL_EXSTYLE, wanted);
        var error = Marshal.GetLastWin32Error();
        if (previous == 0 && error != 0)
        {
            _logger.LogError("could not make the snap preview click-through: error {Error}; it may swallow the click that ends a drag", error);
        }
    }
}
