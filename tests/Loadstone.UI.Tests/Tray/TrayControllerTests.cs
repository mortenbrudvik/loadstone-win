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
    /// the icon reaches the tray and the static SystemEvents are hooked only in Start(), which no
    /// test calls. That is what lets the container smoke test resolve the controller at all.
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
