using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using Loadstone.Core.Geometry;
using Loadstone.UI.Interop;
using static Loadstone.UI.Interop.NativeMethods;
using Rect = Loadstone.Core.Geometry.Rect;

namespace Loadstone.UI.Input;

/// <summary>
/// The translucent blue rectangle shown over the tile a drag will snap to. Borderless, click-through,
/// never activated, topmost, placed with SetWindowPos in physical pixels because WPF's own placement
/// is wrong on mixed-DPI desks. Only ever hidden, never closed.
/// </summary>
internal sealed class SnapPreview : Window, ISnapPreview
{
    public SnapPreview()
    {
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

        var accent = SystemParameters.WindowGlassColor;
        Content = new Border
        {
            CornerRadius = new CornerRadius(10),
            BorderThickness = new Thickness(2),
            Margin = new Thickness(1),
            Background = new SolidColorBrush(Color.FromArgb(56, accent.R, accent.G, accent.B)),   // 22 percent
            BorderBrush = new SolidColorBrush(Color.FromArgb(217, accent.R, accent.G, accent.B)), // 85 percent
        };

        SourceInitialized += (_, _) =>
        {
            var handle = Handle;
            var exStyle = User32.GetWindowLongPtr(handle, NativeConstants.GWL_EXSTYLE);
            User32.SetWindowLongPtr(handle, NativeConstants.GWL_EXSTYLE,
                exStyle | (nint)(NativeConstants.WS_EX_TRANSPARENT | NativeConstants.WS_EX_TOOLWINDOW | NativeConstants.WS_EX_NOACTIVATE));
        };
    }

    private nint Handle => new WindowInteropHelper(this).EnsureHandle();

    public void Show(Rect frame)
    {
        // Inset a little so it reads as a preview rather than as the window itself.
        var inset = frame.Inflate(-6, -6);
        if (!IsVisible)
        {
            Show();
        }

        // Topmost is asserted by this process on its own window, which Windows honors even for a
        // background process; a request from another process would be refused.
        User32.SetWindowPos(Handle, NativeConstants.HWND_TOPMOST, inset.X, inset.Y, inset.Width, inset.Height,
            NativeConstants.SWP_NOACTIVATE | NativeConstants.SWP_SHOWWINDOW);
    }

    public new void Hide()
    {
        if (IsVisible)
        {
            base.Hide();
        }
    }
}
