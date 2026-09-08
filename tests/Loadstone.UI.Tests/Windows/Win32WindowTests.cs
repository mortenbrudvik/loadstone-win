using System.Windows;
using Loadstone.Core.Commands;
using Loadstone.UI.Windows;
using Shouldly;
using Xunit;
using Rect = Loadstone.Core.Geometry.Rect;

namespace Loadstone.UI.Tests.Windows;

public class Win32WindowTests
{
    /// <summary>The window as the app would capture it: handle plus the pid read beside it.</summary>
    private static Win32Window Captured(TestWindow test) => Win32Window.For(test.Handle).ShouldNotBeNull();

    [WpfFact]
    public void VisibleFrame_ExcludesTheInvisibleResizeBorders()
    {
        using var test = new TestWindow();
        var window = Captured(test);

        var visible = window.ReadFrame().ShouldNotBeNull();
        Loadstone.UI.Interop.NativeMethods.User32.GetWindowRect(test.Handle, out var outerRect).ShouldBeTrue();
        var outer = Displays.ToRect(outerRect);

        visible.Width.ShouldBeLessThanOrEqualTo(outer.Width);
        visible.Height.ShouldBeLessThanOrEqualTo(outer.Height);
        var insets = Win32Window.Insets(outer, visible);
        insets.Left.ShouldBeGreaterThanOrEqualTo(0);
        insets.Top.ShouldBeGreaterThanOrEqualTo(0);
        insets.Right.ShouldBeGreaterThanOrEqualTo(0);
        insets.Bottom.ShouldBeGreaterThanOrEqualTo(0);
    }

    [WpfFact]
    public void SetFrame_LandsTheVisibleFrameOnTheTarget()
    {
        using var test = new TestWindow();
        var window = Captured(test);
        var target = new Rect(200, 150, 601, 451);

        window.SetFrame(target).ShouldBe(CommandOutcome.Moved);
        TestWindow.Pump();

        var landed = window.ReadFrame().ShouldNotBeNull();
        Math.Abs(landed.X - target.X).ShouldBeLessThanOrEqualTo(1);
        Math.Abs(landed.Y - target.Y).ShouldBeLessThanOrEqualTo(1);
        Math.Abs(landed.Width - target.Width).ShouldBeLessThanOrEqualTo(1);
        Math.Abs(landed.Height - target.Height).ShouldBeLessThanOrEqualTo(1);
    }

    [WpfFact]
    public void SetFrame_FixedSizeWindow_KeepsItsSizeAndCentersInTheTarget()
    {
        using var test = new TestWindow(resizable: false);
        var window = Captured(test);
        var before = window.ReadFrame().ShouldNotBeNull();
        var target = new Rect(100, 100, 1001, 801);

        window.SetFrame(target).ShouldBe(CommandOutcome.Moved);
        TestWindow.Pump();

        var after = window.ReadFrame().ShouldNotBeNull();
        after.Width.ShouldBe(before.Width);
        after.Height.ShouldBe(before.Height);
        var expected = new Rect(target.X + (target.Width - before.Width) / 2, target.Y + (target.Height - before.Height) / 2, before.Width, before.Height);
        Math.Abs(after.X - expected.X).ShouldBeLessThanOrEqualTo(1);
        Math.Abs(after.Y - expected.Y).ShouldBeLessThanOrEqualTo(1);
    }

    [WpfFact]
    public void SetFrame_FixedSizeWindowLargerThanTheTarget_KeepsItsSizeAndOverhangs()
    {
        using var test = new TestWindow(resizable: false);
        var window = Captured(test);
        var before = window.ReadFrame().ShouldNotBeNull();
        var target = new Rect(300, 200, 301, 201);

        window.SetFrame(target).ShouldBe(CommandOutcome.Moved);
        TestWindow.Pump();

        var after = window.ReadFrame().ShouldNotBeNull();
        after.Width.ShouldBe(before.Width);
        after.Height.ShouldBe(before.Height);
        var expected = new Rect(target.X + (target.Width - before.Width) / 2, target.Y + (target.Height - before.Height) / 2, before.Width, before.Height);
        Math.Abs(after.X - expected.X).ShouldBeLessThanOrEqualTo(1);
        Math.Abs(after.Y - expected.Y).ShouldBeLessThanOrEqualTo(1);
    }

    [WpfFact]
    public void SetFrame_MaximizedWindow_IsRestoredFirst()
    {
        using var test = new TestWindow(state: WindowState.Maximized);
        var window = Captured(test);

        window.SetFrame(new Rect(200, 150, 601, 451)).ShouldBe(CommandOutcome.Moved);
        TestWindow.Pump();

        Loadstone.UI.Interop.NativeMethods.User32.IsZoomed(test.Handle).ShouldBeFalse();
        window.ReadFrame().ShouldNotBeNull().Width.ShouldBeLessThan(700);
    }

    /// <summary>
    /// The other half of the same guard, and the one nothing covered. Symptom without it: a tile
    /// command aimed at a minimized window leaves it minimized and still reports success, because
    /// SetWindowPos on an iconic window writes the restored placement while the DWM frame keeps
    /// reading the minimized rectangle — so the user hears no beep and sees no window move.
    /// </summary>
    [WpfFact]
    public void SetFrame_MinimizedWindow_IsRestoredFirst()
    {
        using var test = new TestWindow(state: WindowState.Minimized);
        var window = Captured(test);
        var target = new Rect(200, 150, 601, 451);

        window.SetFrame(target).ShouldBe(CommandOutcome.Moved);
        TestWindow.Pump();

        Loadstone.UI.Interop.NativeMethods.User32.IsIconic(test.Handle).ShouldBeFalse();
        var landed = window.ReadFrame().ShouldNotBeNull();
        Math.Abs(landed.X - target.X).ShouldBeLessThanOrEqualTo(1);
        Math.Abs(landed.Y - target.Y).ShouldBeLessThanOrEqualTo(1);
        Math.Abs(landed.Width - target.Width).ShouldBeLessThanOrEqualTo(1);
        Math.Abs(landed.Height - target.Height).ShouldBeLessThanOrEqualTo(1);
    }

    [WpfFact]
    public void DestroyedWindow_IsUnreadable()
    {
        var test = new TestWindow();
        var window = Captured(test);
        test.Dispose();

        window.ReadFrame().ShouldBeNull();
        window.SetFrame(new Rect(0, 0, 100, 100)).ShouldBe(CommandOutcome.FrameUnreadable);
    }

    [WpfFact]
    public void Identity_CarriesTheOwningProcess()
    {
        using var test = new TestWindow();
        var window = Captured(test);
        window.Identity.ShouldBe(new Loadstone.Core.Windows.WindowIdentity(test.Handle, Environment.ProcessId));
    }

    /// <summary>
    /// Symptom of the alternative: a window whose owner cannot be read gets an identity with a guessed
    /// pid, every such window compares equal to every other in the restore ledger, and Restore hands
    /// back the frame of a window the user never touched. The handle here comes from a window this
    /// test owned and destroyed, never a literal.
    /// </summary>
    [WpfFact]
    public void For_WhenTheOwningProcessCannotBeRead_IsNull()
    {
        var test = new TestWindow();
        var handle = test.Handle;
        test.Dispose();

        Win32Window.ProcessIdOf(handle).ShouldBeNull();
        Win32Window.For(handle).ShouldBeNull();
    }

    [WpfFact]
    public void WindowLiveness_IsAlive_TracksTheWindow()
    {
        var test = new TestWindow();
        var identity = Captured(test).Identity;
        WindowLiveness.IsAlive(identity).ShouldBeTrue();
        WindowLiveness.IsAlive(identity with { Pid = identity.Pid + 1 }).ShouldBeFalse();
        test.Dispose();
        WindowLiveness.IsAlive(identity).ShouldBeFalse();
    }
}
