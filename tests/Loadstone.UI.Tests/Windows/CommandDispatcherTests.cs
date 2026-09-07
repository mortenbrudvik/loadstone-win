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

    [WpfFact]
    public void Run_WithHandle_MovesTheWindow_Silently()
    {
        using var test = new TestWindow();
        var dispatcher = NewDispatcher();

        dispatcher.Run(WindowCommand.LeftHalf, test.Handle);
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

        dispatcher.Run(WindowCommand.Restore, test.Handle);
        _beeps.ShouldBe(1);
    }

    [WpfFact]
    public void Run_RestoreAfterATile_ReturnsTheWindowSilently()
    {
        using var test = new TestWindow();
        var before = Win32Window.VisibleFrame(test.Handle).ShouldNotBeNull();
        var dispatcher = NewDispatcher();

        dispatcher.Run(WindowCommand.TopRight, test.Handle);
        TestWindow.Pump();
        dispatcher.Run(WindowCommand.Restore, test.Handle);
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

    [Fact]
    public void Run_DestroyedHandle_BeepsAndDoesNotThrow()
    {
        var dispatcher = NewDispatcher();
        Should.NotThrow(() => dispatcher.Run(WindowCommand.LeftHalf, hwnd: 0x1234));
        _beeps.ShouldBe(1);
    }
}
