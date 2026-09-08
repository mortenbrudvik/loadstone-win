using Loadstone.Core.Commands;
using Loadstone.Core.Geometry;
using Loadstone.Core.Windows;
using Loadstone.UI.Windows;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using Xunit;

namespace Loadstone.UI.Tests.Windows;

public class CommandDispatcherTests
{
    private int _beeps;
    private nint? _focused;

    private CommandDispatcher NewDispatcher() => new(
        new WindowDirector(Displays.Snapshot, WindowLiveness.IsAlive),
        NullLogger<CommandDispatcher>.Instance,
        () => _focused,
        () => _beeps++);

    /// <summary>
    /// The identity the tray path captures: the handle together with the pid read beside it, while the
    /// window is still alive. The dispatcher's second overload takes exactly this and no longer
    /// re-derives the pid, which is what stops a recycled handle being treated as the same window.
    /// </summary>
    private static WindowIdentity Captured(TestWindow test) => Win32Window.For(test.Handle).ShouldNotBeNull().Identity;

    [WpfFact]
    public void Run_WithACapturedIdentity_MovesTheWindow_Silently()
    {
        using var test = new TestWindow();
        var dispatcher = NewDispatcher();

        dispatcher.Run(WindowCommand.LeftHalf, Captured(test));
        TestWindow.Pump();

        var displays = Displays.Snapshot();
        var frame = Win32Window.VisibleFrame(test.Handle).ShouldNotBeNull();
        var display = ScreenGeometry.DisplayContaining(frame, displays).ShouldNotBeNull();
        var expected = Tile.LeftHalf.Frame(display.WorkArea);
        Math.Abs(frame.X - expected.X).ShouldBeLessThanOrEqualTo(1);
        Math.Abs(frame.Width - expected.Width).ShouldBeLessThanOrEqualTo(1);
        _beeps.ShouldBe(0);
    }

    [WpfFact]
    public void Run_Restore_BeepsWhenThereIsNothingToRestore()
    {
        using var test = new TestWindow();
        var dispatcher = NewDispatcher();

        dispatcher.Run(WindowCommand.Restore, Captured(test));
        _beeps.ShouldBe(1);
    }

    [WpfFact]
    public void Run_RestoreAfterATile_ReturnsTheWindowSilently()
    {
        using var test = new TestWindow();
        var before = Win32Window.VisibleFrame(test.Handle).ShouldNotBeNull();
        var target = Captured(test);
        var dispatcher = NewDispatcher();

        dispatcher.Run(WindowCommand.TopRight, target);
        TestWindow.Pump();
        dispatcher.Run(WindowCommand.Restore, target);
        TestWindow.Pump();

        var after = Win32Window.VisibleFrame(test.Handle).ShouldNotBeNull();
        Math.Abs(after.X - before.X).ShouldBeLessThanOrEqualTo(1);
        Math.Abs(after.Y - before.Y).ShouldBeLessThanOrEqualTo(1);
        _beeps.ShouldBe(0);
    }

    [Fact]
    public void Run_WithoutAFocusedWindow_BeepsAndDoesNotThrow()
    {
        _focused = null;
        var dispatcher = NewDispatcher();
        Should.NotThrow(() => dispatcher.Run(WindowCommand.LeftHalf));
        _beeps.ShouldBe(1);
    }

    [WpfFact]
    public void Run_UsesTheFocusedWindowResolver()
    {
        using var test = new TestWindow();
        _focused = test.Handle;
        var dispatcher = NewDispatcher();

        dispatcher.Run(WindowCommand.Maximize);
        TestWindow.Pump();

        var frame = Win32Window.VisibleFrame(test.Handle).ShouldNotBeNull();
        var display = ScreenGeometry.DisplayContaining(frame, Displays.Snapshot()).ShouldNotBeNull();
        Math.Abs(frame.Width - display.WorkArea.Width).ShouldBeLessThanOrEqualTo(1);
        _beeps.ShouldBe(0);
    }

    /// <summary>
    /// The resolver can hand back a window that died between the hotkey and the command. Its owning
    /// process no longer reads, so there is no honest identity to record; the command must beep rather
    /// than enter the restore ledger under a guessed pid, where it would compare equal to every other
    /// unreadable window and Restore would move a stranger. The handle comes from a window this test
    /// owned and destroyed, never a literal: any value picked out of the air may belong to a window on
    /// the user's desktop.
    /// </summary>
    [WpfFact]
    public void Run_WhenTheFocusedWindowsProcessCannotBeRead_BeepsAndDoesNotThrow()
    {
        var test = new TestWindow();
        _focused = test.Handle;
        test.Dispose();

        var dispatcher = NewDispatcher();
        Should.NotThrow(() => dispatcher.Run(WindowCommand.LeftHalf));
        _beeps.ShouldBe(1);
    }

    /// <summary>
    /// The identity is captured while the window lives and used after it is gone — the tray menu's own
    /// shape, where the user's window can close while the menu is open. The handle comes from a window
    /// this test owned and destroyed, never a literal: any value picked out of the air may belong to a
    /// window on the user's desktop, and the command would move it.
    /// </summary>
    [WpfFact]
    public void Run_WithAnIdentityWhoseWindowIsGone_BeepsAndDoesNotThrow()
    {
        var test = new TestWindow();
        var destroyed = Captured(test);
        test.Dispose();

        var dispatcher = NewDispatcher();
        Should.NotThrow(() => dispatcher.Run(WindowCommand.LeftHalf, destroyed));
        _beeps.ShouldBe(1);
    }
}
