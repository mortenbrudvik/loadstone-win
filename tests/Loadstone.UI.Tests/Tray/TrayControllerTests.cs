using System.IO;
using System.Security;
using Loadstone.Core.Commands;
using Loadstone.UI.Input;
using Loadstone.UI.Interop;
using Loadstone.UI.Services;
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
