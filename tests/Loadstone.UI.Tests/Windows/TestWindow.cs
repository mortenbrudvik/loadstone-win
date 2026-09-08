using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using Loadstone.UI.Interop;

namespace Loadstone.UI.Tests.Windows;

/// <summary>A real, visible WPF window owned by the test, for driving Win32 code against. Use under [WpfFact].</summary>
internal sealed class TestWindow : IDisposable
{
    // Not in NativeConstants on purpose: the app never creates a window of its own with a parent, so
    // production has no use for these two. Only the tests that need a real child HWND do.
    private const int WS_CHILD = 0x40000000;
    private const int WS_VISIBLE = 0x10000000;

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

    /// <summary>
    /// Puts this window in the foreground and reports whether Windows allowed it. Windows often will
    /// not: SetForegroundWindow obeys a process that already holds the foreground, or received the
    /// last input event, or finds no foreground window at all, and a test host started from a terminal
    /// is none of those. Callers that need the foreground skip on false rather than
    /// assert against a window belonging to somebody else — that assertion would be about the user's
    /// desktop, not about Loadstone.
    /// </summary>
    public bool TakeForeground()
    {
        // Twice: the first Activate on a window shown with ShowActivated=false sometimes only gets as
        // far as flashing the taskbar button, and the second one lands once the window manager has
        // finished with the show.
        for (var attempt = 0; attempt < 2; attempt++)
        {
            Window.Activate();
            Pump();
            if (NativeMethods.User32.GetForegroundWindow() == Handle)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// A real child HWND inside this window — the shape the ancestor walk exists for, since the handle
    /// an entry point is handed is usually a control inside an app rather than the app. Dispose it
    /// before the window; a `using` declared after this one does that.
    /// </summary>
    public HwndSource CreateChild()
    {
        var source = new HwndSource(new HwndSourceParameters("LoadstoneTestChild")
        {
            ParentWindow = Handle,
            WindowStyle = WS_CHILD | WS_VISIBLE,
            PositionX = 8,
            PositionY = 8,
            Width = 80,
            Height = 24,
        });
        Pump();
        return source;
    }

    /// <summary>
    /// A message-only window: a real handle that is never visible and carries neither a caption nor a
    /// sizing frame. This is the shape of Loadstone's own hotkey window, which must never come back
    /// from a manageability check.
    /// </summary>
    public static HwndSource CreateMessageOnlyWindow() => new(new HwndSourceParameters("LoadstoneTestMessageOnly")
    {
        ParentWindow = NativeConstants.HWND_MESSAGE,
        WindowStyle = 0,
        Width = 0,
        Height = 0,
    });

    public void Dispose()
    {
        Window.Close();
        Pump();
    }
}
