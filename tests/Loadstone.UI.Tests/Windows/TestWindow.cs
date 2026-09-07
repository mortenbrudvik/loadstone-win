using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;

namespace Loadstone.UI.Tests.Windows;

/// <summary>A real, visible WPF window owned by the test, for driving Win32 code against. Use under [WpfFact].</summary>
internal sealed class TestWindow : IDisposable
{
    public TestWindow(bool resizable = true, WindowState state = WindowState.Normal)
    {
        Window = new Window
        {
            Title = "Loadstone test window",
            Width = 500,
            Height = 400,
            Left = 120,
            Top = 120,
            ShowActivated = false,
            ShowInTaskbar = false,
            ResizeMode = resizable ? ResizeMode.CanResize : ResizeMode.NoResize,
            WindowStartupLocation = WindowStartupLocation.Manual,
        };
        Window.Show();
        Window.WindowState = state;
        Handle = new WindowInteropHelper(Window).Handle;
        Pump();
    }

    public Window Window { get; }
    public nint Handle { get; }

    /// <summary>Lets WPF and the window manager process pending messages, so a SetWindowPos has taken effect.</summary>
    public static void Pump()
    {
        var frame = new DispatcherFrame();
        Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Background, () => frame.Continue = false);
        Dispatcher.PushFrame(frame);
    }

    public void Dispose()
    {
        Window.Close();
        Pump();
    }
}
