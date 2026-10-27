using System.IO;
using System.Security;
using System.Windows.Controls;
using Loadstone.Core.Commands;
using Loadstone.Core.Windows;
using Loadstone.UI.Input;
using Loadstone.UI.Interop;
using Loadstone.UI.Services;
using Loadstone.UI.Tests.Windows;
using Loadstone.UI.Tray;
using Loadstone.UI.Windows;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Shouldly;
using Xunit;

namespace Loadstone.UI.Tests.Tray;

public class TrayControllerTests
{
    /// <summary>
    /// The Help item opens the Windows user guide on brudvik.com, beside the Mac one, since
    /// 2026-10-27; before that it opened a GitHub Pages site, which now refreshes to this address.
    /// The installer's support URL is pinned to the same constant by InnoScriptTests.
    /// </summary>
    [Fact]
    public void HelpUrl_IsTheWindowsGuideOnBrudvikCom()
    {
        TrayController.HelpUrl.ShouldBe("https://brudvik.com/loadstone/help/windows");
    }

    /// <summary>
    /// A system light/dark change used to re-pick only the icon. The app theme - which the tray
    /// menu's WPF-UI styles draw from - was applied once at startup and then followed the system
    /// only through the Settings window's watcher, which exists only once Settings has been opened.
    /// A launch-at-login session that never opened Settings kept the menu in the startup theme.
    /// The apply itself is WPF-UI's and is taken through a seam: the real one raises a static event
    /// that every FluentWindow in the process handles on its own thread.
    /// </summary>
    [WpfFact]
    public void FollowSystemTheme_AppliesTheSystemThemeBeforeRePickingTheIcon()
    {
        var registrar = Substitute.For<IHotkeyRegistrar>();
        registrar.Register(Arg.Any<WindowCommand>(), Arg.Any<Shortcut>()).Returns(HotkeyStatus.Registered);
        var probe = Substitute.For<IKeyboardLayoutProbe>();
        probe.CharacterTypedBy(Arg.Any<Shortcut>()).Returns((string?)null);
        using var hotkeys = new HotkeyCenter(registrar, new InMemorySettings(), Substitute.For<ICommandDispatcher>(), probe, NullLogger<HotkeyCenter>.Instance);
        var applied = 0;
        var tray = new TrayController(Substitute.For<ICommandDispatcher>(), hotkeys, new ShellActions(), NullLogger<TrayController>.Instance, () => applied++);
        try
        {
            applied.ShouldBe(0);

            tray.FollowSystemTheme();

            applied.ShouldBe(1);
        }
        finally
        {
            tray.Dispose();
        }
    }

    /// <summary>
    /// The constructor builds the menu and the icon object and does nothing else the user can see:
    /// the icon reaches the tray, and the static SystemEvents and the foreground hook are hooked, only
    /// in Start(), which no test calls with the real icon. That is what lets the container smoke test
    /// resolve the controller at all.
    /// </summary>
    [WpfFact]
    public void Construction_WithoutStart_IsInertAndDisposes()
    {
        var registrar = Substitute.For<IHotkeyRegistrar>();
        registrar.Register(Arg.Any<WindowCommand>(), Arg.Any<Shortcut>()).Returns(HotkeyStatus.Registered);
        var probe = Substitute.For<IKeyboardLayoutProbe>();
        probe.CharacterTypedBy(Arg.Any<Shortcut>()).Returns((string?)null);
        using var hotkeys = new HotkeyCenter(registrar, new InMemorySettings(), Substitute.For<ICommandDispatcher>(), probe, NullLogger<HotkeyCenter>.Instance);

        var tray = new TrayController(Substitute.For<ICommandDispatcher>(), hotkeys, new ShellActions(), NullLogger<TrayController>.Instance);

        Should.NotThrow(tray.Dispose);
    }

    [WpfFact]
    public void Menu_WithNothingCaptured_RunsAgainstWhateverIsForeground()
    {
        var dispatcher = Dispatcher();
        using var hotkeys = Hotkeys(dispatcher);
        var tray = new TrayController(dispatcher, hotkeys, new ShellActions(), NullLogger<TrayController>.Instance, () => { }, () => null);
        try
        {
            tray.CaptureForeground();
            LeftHalf(tray).Command.ShouldNotBeNull().Execute(null);

            dispatcher.Received(1).Run(WindowCommand.LeftHalf);
            dispatcher.DidNotReceive().Run(Arg.Any<WindowCommand>(), Arg.Any<WindowIdentity>());
        }
        finally
        {
            tray.Dispose();
        }
    }

    [WpfFact]
    public void Menu_ActsOnTheWindowCapturedBeforeItOpened()
    {
        // Loadstone will not tile its own process, so the target has to already be someone else's
        // foreground window. The dispatcher is a substitute: the click does not move it.
        var hwnd = NativeMethods.User32.GetForegroundWindow();
        if (hwnd == 0 || !WindowStyles.IsManageable(hwnd, Environment.ProcessId))
        {
            Assert.Skip("no foreign manageable window is in the foreground");
        }

        var identity = Win32Window.For(hwnd).ShouldNotBeNull().Identity;
        var dispatcher = Dispatcher();
        using var hotkeys = Hotkeys(dispatcher);
        var tray = new TrayController(dispatcher, hotkeys, new ShellActions(), NullLogger<TrayController>.Instance, () => { }, () => identity);
        try
        {
            tray.CaptureForeground();
            LeftHalf(tray).Command.ShouldNotBeNull().Execute(null);

            dispatcher.Received(1).Run(WindowCommand.LeftHalf, identity);
            dispatcher.DidNotReceive().Run(WindowCommand.LeftHalf);
        }
        finally
        {
            tray.Dispose();
        }
    }

    [WpfFact]
    public void Menu_SkipsACapturedWindowThatHasSinceClosed()
    {
        var test = new TestWindow();
        var identity = Win32Window.For(test.Handle).ShouldNotBeNull().Identity;
        test.Dispose();

        var dispatcher = Dispatcher();
        using var hotkeys = Hotkeys(dispatcher);
        var tray = new TrayController(dispatcher, hotkeys, new ShellActions(), NullLogger<TrayController>.Instance, () => { }, () => identity);
        try
        {
            tray.CaptureForeground();
            LeftHalf(tray).Command.ShouldNotBeNull().Execute(null);

            dispatcher.DidNotReceive().Run(Arg.Any<WindowCommand>(), Arg.Any<WindowIdentity>());
            dispatcher.DidNotReceive().Run(Arg.Any<WindowCommand>());
        }
        finally
        {
            tray.Dispose();
        }
    }

    /// <summary>
    /// The smoke test's failure, with real windows: the user's window is in front, then the right-click
    /// on the tray icon hands the foreground to the hidden-icons flyout before the menu opens. Read
    /// then, GetForegroundWindow names the flyout, which is nobody's to tile, and every command beeped.
    /// The second test window plays the flyout by answering to its class name.
    /// </summary>
    [WpfFact]
    public void CaptureForeground_ActsOnTheWindowInFrontBeforeTheTrayTookTheForeground()
    {
        using var usersWindow = new TestWindow();
        using var flyout = new TestWindow();
        using var history = new ForegroundHistory(
            NullLogger.Instance,
            ownPid: 0,
            hwnd => hwnd == flyout.Handle ? WindowStyles.Snapshot(hwnd) with { ClassName = "TopLevelWindowForOverflowXamlIsland" } : WindowStyles.Snapshot(hwnd));
        history.Install().ShouldBeTrue();
        var dispatcher = Dispatcher();
        using var hotkeys = Hotkeys(dispatcher);
        var tray = new TrayController(dispatcher, hotkeys, new ShellActions(), NullLogger<TrayController>.Instance, () => { }, history: history);
        try
        {
            if (!usersWindow.TakeForeground() || !flyout.TakeForeground())
            {
                Assert.Skip("Windows would not give the test windows the foreground");
            }

            TestWindow.Pump();

            tray.CaptureForeground().ShouldBe(Win32Window.For(usersWindow.Handle).ShouldNotBeNull().Identity);
        }
        finally
        {
            tray.Dispose();
        }
    }

    /// <summary>
    /// The same capture without asking Windows for the foreground, which a test process is often
    /// refused: the history is installed and then told what took the foreground, so the controller's
    /// use of it is checked on every run.
    /// </summary>
    [WpfFact]
    public void CaptureForeground_AsksTheHistory_NotTheForeground()
    {
        using var usersWindow = new TestWindow();
        using var flyout = new TestWindow();
        using var history = new ForegroundHistory(
            NullLogger.Instance,
            ownPid: 0,
            hwnd => hwnd == flyout.Handle ? WindowStyles.Snapshot(hwnd) with { ClassName = "TopLevelWindowForOverflowXamlIsland" } : WindowStyles.Snapshot(hwnd));
        history.Install().ShouldBeTrue();
        history.Observe(usersWindow.Handle);
        history.Observe(flyout.Handle);
        var dispatcher = Dispatcher();
        using var hotkeys = Hotkeys(dispatcher);
        var tray = new TrayController(dispatcher, hotkeys, new ShellActions(), NullLogger<TrayController>.Instance, () => { }, history: history);
        try
        {
            tray.CaptureForeground().ShouldBe(Win32Window.For(usersWindow.Handle).ShouldNotBeNull().Identity);
        }
        finally
        {
            tray.Dispose();
        }
    }

    /// <summary>
    /// Start is what makes the fix live: without the hook installed, the capture falls back to the
    /// foreground as the menu opens, which is the tray, and every command beeps again. The icon itself
    /// is left out through its seam, so no test puts an icon in the user's tray.
    /// </summary>
    [WpfFact]
    public void Start_FollowsTheForeground_AndDisposeStops()
    {
        var history = new ForegroundHistory(NullLogger.Instance, ownPid: 0, WindowStyles.Snapshot);
        var dispatcher = Dispatcher();
        using var hotkeys = Hotkeys(dispatcher);
        var tray = new TrayController(dispatcher, hotkeys, new ShellActions(), NullLogger<TrayController>.Instance, () => { }, history: history, createIcon: () => { });
        try
        {
            history.IsInstalled.ShouldBeFalse();
            tray.Start();
            history.IsInstalled.ShouldBeTrue();
        }
        finally
        {
            tray.Dispose();
        }

        history.IsInstalled.ShouldBeFalse();
    }

    /// <summary>The controller the container builds carries a history of its own; without one the capture could only ever read the tray.</summary>
    [WpfFact]
    public void Constructor_GivesTheMenuAForegroundHistory()
    {
        var dispatcher = Dispatcher();
        using var hotkeys = Hotkeys(dispatcher);
        var tray = new TrayController(dispatcher, hotkeys, new ShellActions(), NullLogger<TrayController>.Instance);
        try
        {
            tray.History.ShouldNotBeNull();
        }
        finally
        {
            tray.Dispose();
        }
    }

    /// <summary>
    /// A command that beeps from the menu is explained only by the log: the line names the window that
    /// decided there was nothing to act on, and why - here a window minimized since.
    /// </summary>
    [WpfFact]
    public void CaptureForeground_WithNothingToActOn_LogsTheWindowThatDecidedAndWhy()
    {
        using var minimized = new TestWindow(state: System.Windows.WindowState.Minimized);
        using var history = new ForegroundHistory(NullLogger.Instance, ownPid: 0, WindowStyles.Snapshot);
        history.Install().ShouldBeTrue();
        history.Observe(minimized.Handle);
        var logger = new RecordingLogger<TrayController>();
        var dispatcher = Dispatcher();
        using var hotkeys = Hotkeys(dispatcher);
        var tray = new TrayController(dispatcher, hotkeys, new ShellActions(), logger, () => { }, history: history);
        try
        {
            tray.CaptureForeground().ShouldBeNull();

            var line = logger.Entries.Where(e => e.Values.ContainsKey("Why")).ShouldHaveSingleItem();
            line.Values["Hwnd"].ShouldBe(minimized.Handle);
            line.Values["Why"].ShouldBe("minimized");
        }
        finally
        {
            tray.Dispose();
        }
    }

    /// <summary>Without the hook the menu reads the foreground as it opens, which is the tray, and the log says so rather than blaming the history.</summary>
    [WpfFact]
    public void CaptureForeground_WithoutTheHook_SaysSo()
    {
        using var history = new ForegroundHistory(NullLogger.Instance, ownPid: 0, WindowStyles.Snapshot);
        var logger = new RecordingLogger<TrayController>();
        var dispatcher = Dispatcher();
        using var hotkeys = Hotkeys(dispatcher);
        var tray = new TrayController(dispatcher, hotkeys, new ShellActions(), logger, () => { }, history: history);
        try
        {
            tray.CaptureForeground();

            logger.Entries.ShouldContain(e => ((string)e.Values["{OriginalFormat}"]!).Contains("without the foreground hook"));
        }
        finally
        {
            tray.Dispose();
        }
    }

    private static ICommandDispatcher Dispatcher() => Substitute.For<ICommandDispatcher>();

    private static HotkeyCenter Hotkeys(ICommandDispatcher dispatcher)
    {
        var registrar = Substitute.For<IHotkeyRegistrar>();
        registrar.Register(Arg.Any<WindowCommand>(), Arg.Any<Shortcut>()).Returns(HotkeyStatus.Registered);
        var probe = Substitute.For<IKeyboardLayoutProbe>();
        probe.CharacterTypedBy(Arg.Any<Shortcut>()).Returns((string?)null);
        return new HotkeyCenter(registrar, new InMemorySettings(), dispatcher, probe, NullLogger<HotkeyCenter>.Instance);
    }

    private static MenuItem LeftHalf(TrayController tray) =>
        tray.Menu.Items.OfType<MenuItem>().Single(item => Equals(item.Header, "Left Half"));

    /// <summary>
    /// A policy or an ACL can refuse an HKCU read, and the theme lookup runs from the constructor,
    /// where a throw ends startup. The dark glyph is the fallback because it is the one that reads
    /// on the default taskbar.
    /// </summary>
    [Fact]
    public void IsTaskbarLight_FallsBackToDark_WhenThePersonalizeKeyCannotBeOpened()
    {
        TrayController.IsTaskbarLight(() => throw new SecurityException("refused")).ShouldBeFalse();
        TrayController.IsTaskbarLight(() => throw new UnauthorizedAccessException("refused")).ShouldBeFalse();
        TrayController.IsTaskbarLight(() => throw new IOException("refused")).ShouldBeFalse();
    }

    /// <summary>
    /// new Icon(stream) picks the frame nearest SM_CXICON, 32 px at 100 percent, so the 16, 20 and 24
    /// px frames tools/icons/generate.ps1 writes for the tray were never used and the shell scaled a
    /// larger one down. The tray wants the small-icon metric.
    /// </summary>
    [Fact]
    public void SmallTrayIcon_IsSizedByTheSmallIconMetric_NotTheLargeOne()
    {
        using var stream = File.OpenRead(Path.Combine(RepoFile.Root, "src", "Loadstone.UI", "Assets", "tray-dark.ico"));

        using var icon = TrayController.SmallTrayIcon(stream);

        var small = NativeMethods.User32.GetSystemMetrics(NativeConstants.SM_CXSMICON);
        var large = NativeMethods.User32.GetSystemMetrics(NativeConstants.SM_CXICON);
        small.ShouldBeGreaterThan(0);
        icon.Width.ShouldBeLessThan(large);
        // The nearest frame to the metric: exact at 100, 125, 150 and 200 percent, one step off at most.
        icon.Width.ShouldBeLessThanOrEqualTo(small + 4);
    }
}
