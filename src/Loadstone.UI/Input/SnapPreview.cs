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
/// The translucent rectangle, painted in the system accent colour, shown over the tile a drag will
/// snap to - and re-read on every show, because Windows changes it mid-session. Borderless, click-through,
/// never activated, topmost, placed with SetWindowPos in physical pixels because WPF's own placement
/// is wrong on mixed-DPI desks. Only ever hidden, never closed.
/// </summary>
internal sealed class SnapPreview : Window, ISnapPreview
{
    /// <summary>How far the preview is inset on each side so it reads as a preview rather than as the window itself.</summary>
    private const int Inset = 6;

    private readonly ILogger<SnapPreview> _logger;
    private readonly Border _fill;
    private Color _accent;
    private bool _placementFailureLogged;

    public SnapPreview(ILogger<SnapPreview> logger)
    {
        _logger = logger;
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
        Paint(_accent);
        Content = _fill;

        SourceInitialized += (_, _) => MakeClickThrough();
    }

    private nint Handle => new WindowInteropHelper(this).EnsureHandle();

    public void Show(Rect frame)
    {
        // Re-read the accent rather than keep the one this window was born with: Windows changes it
        // mid-session, from Settings or by following the wallpaper, and a preview stuck on the old
        // colour until the next restart is the sort of thing nobody thinks to report.
        var accent = SystemParameters.WindowGlassColor;
        if (accent != _accent)
        {
            _accent = accent;
            Paint(accent);
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

    private void Paint(Color accent)
    {
        _fill.Background = new SolidColorBrush(Color.FromArgb(56, accent.R, accent.G, accent.B));   // 22 percent
        _fill.BorderBrush = new SolidColorBrush(Color.FromArgb(217, accent.R, accent.G, accent.B)); // 85 percent
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
