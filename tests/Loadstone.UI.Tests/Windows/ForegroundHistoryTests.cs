using System.Windows;
using Loadstone.UI.Interop;
using Loadstone.UI.Windows;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using Xunit;

namespace Loadstone.UI.Tests.Windows;

public class ForegroundHistoryTests
{
    private const int Loadstone = 99;

    /// <summary>An ordinary app window: visible, with a caption and a sizing frame, owned by some other process.</summary>
    private static readonly WindowSnapshot AppWindow =
        new(IsWindow: true, IsVisible: true, IsCloaked: false, Pid: 4242, ClassName: "AppWindow", Style: NativeConstants.WS_CAPTION | NativeConstants.WS_THICKFRAME, ExStyle: 0);

    /// <summary>A shell surface or a tray app's popup while it is open: always on top, no caption, no sizing frame.</summary>
    private static readonly WindowSnapshot Surface = AppWindow with { ClassName = "Windows.UI.Core.CoreWindow", Style = 0, ExStyle = NativeConstants.WS_EX_TOPMOST };

    /// <summary>What each made-up handle is right now; a test changes an entry between what took the foreground and when the menu asks.</summary>
    private readonly Dictionary<nint, WindowSnapshot> _windows = [];
    private readonly HashSet<nint> _minimized = [];

    private ForegroundHistory NewHistory() =>
        new(NullLogger.Instance, ownPid: Loadstone, hwnd => _windows.TryGetValue(hwnd, out var snapshot) ? snapshot : AppWindow, _minimized.Contains);

    /// <summary>
    /// A right-click on the tray icon hands the foreground to the shell before the menu opens: to the
    /// hidden-icons flyout when the icon is kept there, which is where Windows 11 puts a new app's
    /// icon, and to the taskbar itself. Both stay on screen, so they are never taken down at all.
    /// </summary>
    [Theory]
    [InlineData("TopLevelWindowForOverflowXamlIsland")]
    [InlineData("NotifyIconOverflowWindow")]
    [InlineData("Shell_TrayWnd")]
    [InlineData("Shell_SecondaryTrayWnd")]
    public void Observe_TheTrayTakingTheForeground_KeepsTheWindowBeforeIt(string trayClass)
    {
        _windows[9] = AppWindow with { ClassName = trayClass };
        var history = NewHistory();

        history.Observe(1);
        history.Observe(9);

        history.Last.ShouldBe((nint)1);
        history.Resolve().ShouldBe((nint)1);
    }

    /// <summary>
    /// Start and Quick Settings take the foreground when they open, and the click on the ^ chevron
    /// that dismisses them opens the tray, so the user's window never comes back in between; measured
    /// live, the menu captured nothing after either. By the time it opens, Start is cloaked and Quick
    /// Settings hidden, so they are off the screen and the window before them is the one to act on.
    /// </summary>
    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void Resolve_AShellSurfaceDismissedByTheTimeTheMenuOpens_IsPassedOver(bool cloaked, bool hidden)
    {
        _windows[9] = Surface;
        var history = NewHistory();
        history.Observe(1);
        history.Observe(9);

        _windows[9] = Surface with { IsCloaked = cloaked, IsVisible = !hidden };

        history.Resolve().ShouldBe((nint)1);
    }

    /// <summary>
    /// Most tray apps hand the foreground to a hidden helper window of their own before showing their
    /// menu, the classic TrackPopupMenu recipe WinForms and Electron follow. Used just before
    /// Loadstone's menu, that helper is never on screen, so it never stands in front of the user's window.
    /// </summary>
    [Fact]
    public void Resolve_ATrayAppsHiddenHelperWindow_IsPassedOver()
    {
        _windows[3] = AppWindow with { ClassName = "WindowsForms10.Window.0.app", IsVisible = false };
        var history = NewHistory();

        history.Observe(1);
        history.Observe(3);

        history.Resolve().ShouldBe((nint)1);
    }

    /// <summary>
    /// A window can take the foreground while it is still opening, before it is visible; seen live,
    /// judged then an always-on-top window was passed over and the menu acted on the window before
    /// it, which was the user's terminal. Judged when the menu opens, it is on screen and counts.
    /// </summary>
    [Fact]
    public void Resolve_AWindowThatTookTheForegroundBeforeItWasVisible_CountsOnceItIs()
    {
        _windows[2] = AppWindow with { ExStyle = NativeConstants.WS_EX_TOPMOST, IsVisible = false };
        var history = NewHistory();
        history.Observe(1);
        history.Observe(2);

        _windows[2] = _windows[2] with { IsVisible = true };

        history.Resolve().ShouldBe((nint)2);
    }

    /// <summary>
    /// Styles change while a window keeps the foreground: a fullscreen game or player is always on top
    /// without a frame, and gets one back when it goes windowed. It is judged as it is when the menu
    /// opens, the same as a hotkey pressed then would judge it.
    /// </summary>
    [Fact]
    public void Resolve_AWindowThatHasSinceGainedAFrame_Counts()
    {
        _windows[2] = Surface with { ClassName = "SDL_app" };
        var history = NewHistory();
        history.Observe(1);
        history.Observe(2);

        _windows[2] = AppWindow with { ClassName = "SDL_app" };

        history.Resolve().ShouldBe((nint)2);
    }

    /// <summary>
    /// The first window still on screen is the one the user left for the tray, and when Loadstone
    /// would not move it, the menu has nothing to act on, as a hotkey pressed there would not. A click
    /// on the desktop is a choice of no window; an always-on-top toolbar left open is not an app window.
    /// </summary>
    [Theory]
    [InlineData("Progman", 0L, 0L)]
    [InlineData("ZoomFloatingToolbar", 0L, NativeConstants.WS_EX_TOPMOST | NativeConstants.WS_EX_TOOLWINDOW)]
    public void Resolve_AWindowStillOnScreenThatLoadstoneWillNotMove_IsNothing(string className, long style, long exStyle)
    {
        _windows[7] = AppWindow with { ClassName = className, Style = style, ExStyle = exStyle };
        var history = NewHistory();

        history.Observe(1);
        history.Observe(7);

        history.Resolve().ShouldBeNull();
    }

    /// <summary>
    /// A window minimized since it was last in front, from its own taskbar button say, left the
    /// foreground on the taskbar. It is no longer in front, so the menu has nothing to act on, as on
    /// the Mac, rather than restoring it to tile it.
    /// </summary>
    [Fact]
    public void Resolve_TheLastWindowSinceMinimized_IsNothing()
    {
        var history = NewHistory();
        history.Observe(1);
        history.Observe(2);

        _minimized.Add(2);

        history.Resolve().ShouldBeNull();
    }

    /// <summary>Closed from its taskbar thumbnail, say, while the taskbar kept the foreground: the window before it is in front again.</summary>
    [Fact]
    public void Resolve_AWindowClosedSince_IsPassedOver()
    {
        var history = NewHistory();
        history.Observe(1);
        history.Observe(2);

        _windows[2] = AppWindow with { IsWindow = false, IsVisible = false, Pid = 0, ClassName = "" };

        history.Resolve().ShouldBe((nint)1);
    }

    /// <summary>Windows reuses handles: one that another process's window now holds is not the window that was in front.</summary>
    [Fact]
    public void Resolve_AHandleAnotherProcessHasSinceTaken_IsPassedOver()
    {
        var history = NewHistory();
        history.Observe(1);
        history.Observe(2);

        _windows[2] = AppWindow with { Pid = 5555 };

        history.Resolve().ShouldBe((nint)1);
    }

    /// <summary>
    /// Loadstone's own windows never count, so a command from the menu with Settings in front acts on
    /// the window behind it. The hook also skips them, but only for this process; the rule is here.
    /// </summary>
    [Fact]
    public void Observe_ALoadstoneWindow_KeepsTheWindowBeforeIt()
    {
        _windows[8] = AppWindow with { Pid = Loadstone, ClassName = "HwndWrapper[Loadstone]" };
        var history = NewHistory();

        history.Observe(1);
        history.Observe(8);

        history.Last.ShouldBe((nint)1);
        history.Resolve().ShouldBe((nint)1);
    }

    [Fact]
    public void Observe_AnotherWindow_TakesItsPlace()
    {
        var history = NewHistory();

        history.Observe(1);
        history.Observe(2);

        history.Last.ShouldBe((nint)2);
        history.Resolve().ShouldBe((nint)2);
    }

    /// <summary>A window brought back to the front is the most recent again, with the one it covered behind it.</summary>
    [Fact]
    public void Observe_AWindowBroughtBackToTheFront_IsTheMostRecentAgain()
    {
        var history = NewHistory();
        history.Observe(1);
        history.Observe(2);
        history.Observe(1);

        history.Resolve().ShouldBe((nint)1);

        _windows[1] = AppWindow with { IsVisible = false };
        history.Resolve().ShouldBe((nint)2);
    }

    /// <summary>The history runs for the whole session, so it keeps only the recent windows rather than every one ever in front.</summary>
    [Fact]
    public void Observe_ManyWindows_ForgetsTheOldest()
    {
        var history = NewHistory();
        history.Observe(1);
        for (nint hwnd = 100; hwnd < 100 + ForegroundHistory.RecentWindows; hwnd++)
        {
            _windows[hwnd] = Surface with { IsCloaked = true };
            history.Observe(hwnd);
        }

        history.Resolve().ShouldBeNull();
    }

    [Fact]
    public void Observe_NoWindowAtAll_ChangesNothing()
    {
        var history = NewHistory();

        history.Observe(1);
        history.Observe(0);

        history.Last.ShouldBe((nint)1);
    }

    [Fact]
    public void Last_BeforeAnyWindowWasSeen_IsNull()
    {
        var history = NewHistory();

        history.Last.ShouldBeNull();
        history.Resolve().ShouldBeNull();
    }

    /// <summary>
    /// What takes the foreground first on a click in the taskbar is not the taskbar but a XAML input
    /// window inside it (Windows.UI.Input.InputSite.WindowClass, a child of Shell_TrayWnd), seen live
    /// on Windows 11. A window is part of the tray when its root is, so a real child window inside the
    /// window playing the flyout is part of it too.
    /// </summary>
    [WpfFact]
    public void Observe_AWindowInsideTheTray_KeepsTheWindowBeforeIt()
    {
        using var usersWindow = new TestWindow();
        using var flyout = new TestWindow();
        using var inside = flyout.CreateChild();
        var history = new ForegroundHistory(
            NullLogger.Instance,
            ownPid: 0,
            hwnd => hwnd == flyout.Handle ? WindowStyles.Snapshot(hwnd) with { ClassName = "TopLevelWindowForOverflowXamlIsland" } : WindowStyles.Snapshot(hwnd));

        history.Observe(usersWindow.Handle);
        history.Observe(inside.Handle);

        history.Last.ShouldBe(usersWindow.Handle);
    }

    /// <summary>The same minimized rule through the real IsIconic, which the made-up windows above stand in for.</summary>
    [WpfFact]
    public void Resolve_ARealMinimizedWindow_IsNothing()
    {
        using var normal = new TestWindow();
        using var minimized = new TestWindow(state: WindowState.Minimized);
        var history = new ForegroundHistory(NullLogger.Instance, ownPid: 0, WindowStyles.Snapshot);

        history.Observe(normal.Handle);
        history.Resolve().ShouldBe(normal.Handle);

        history.Observe(minimized.Handle);
        history.Resolve().ShouldBeNull();
    }

    /// <summary>
    /// USER32 may still be calling a hook it refused to take back, so its delegate must stay rooted:
    /// a second Install would overwrite it. Nothing re-installs today; the type makes sure nothing can.
    /// </summary>
    [WpfFact]
    public void Install_AfterDispose_IsRefused()
    {
        var history = new ForegroundHistory(NullLogger.Instance, ownPid: 0, WindowStyles.Snapshot);
        history.Install().ShouldBeTrue();
        history.Dispose();

        history.Install().ShouldBeFalse();
        history.IsInstalled.ShouldBeFalse();
    }

    /// <summary>
    /// The hook, not just the filter: a real window taken to the foreground is what the history holds
    /// once the thread has pumped the event. Own-process windows count here, because ownPid 0 owns no
    /// windows; in the app the hook skips Loadstone's own.
    /// </summary>
    [WpfFact]
    public void Install_FollowsTheWindowInTheForeground()
    {
        using var history = new ForegroundHistory(NullLogger.Instance, ownPid: 0, WindowStyles.Snapshot);
        history.Install().ShouldBeTrue();
        using var first = new TestWindow();
        using var second = new TestWindow();

        if (!first.TakeForeground())
        {
            Assert.Skip("Windows would not give the test window the foreground");
        }

        TestWindow.Pump();
        history.Last.ShouldBe(first.Handle);

        if (!second.TakeForeground())
        {
            Assert.Skip("Windows would not give the test window the foreground");
        }

        TestWindow.Pump();
        history.Last.ShouldBe(second.Handle);
    }

    /// <summary>The window already in front when the hook goes in has raised no event yet, and is still the one to act on.</summary>
    [WpfFact]
    public void Install_StartsFromTheWindowAlreadyInFront()
    {
        using var window = new TestWindow();
        if (!window.TakeForeground())
        {
            Assert.Skip("Windows would not give the test window the foreground");
        }

        using var history = new ForegroundHistory(NullLogger.Instance, ownPid: 0, WindowStyles.Snapshot);
        history.Install().ShouldBeTrue();

        history.Last.ShouldBe(window.Handle);
    }
}
