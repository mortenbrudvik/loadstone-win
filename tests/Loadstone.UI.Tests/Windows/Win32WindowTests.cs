using System.Windows;
using Loadstone.Core.Commands;
using Loadstone.Core.Geometry;
using Loadstone.UI.Windows;
using Shouldly;
using Xunit;
using Rect = Loadstone.Core.Geometry.Rect;

namespace Loadstone.UI.Tests.Windows;

public class Win32WindowTests
{
    [WpfFact]
    public void VisibleFrame_ExcludesTheInvisibleResizeBorders()
    {
        using var test = new TestWindow();
        var window = new Win32Window(test.Handle);

        var visible = window.Frame.ShouldNotBeNull();
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
        var window = new Win32Window(test.Handle);
        var target = new Rect(200, 150, 601, 451);

        window.SetFrame(target).ShouldBe(CommandOutcome.Moved);
        TestWindow.Pump();

        var landed = window.Frame.ShouldNotBeNull();
        Math.Abs(landed.X - target.X).ShouldBeLessThanOrEqualTo(1);
        Math.Abs(landed.Y - target.Y).ShouldBeLessThanOrEqualTo(1);
        Math.Abs(landed.Width - target.Width).ShouldBeLessThanOrEqualTo(1);
        Math.Abs(landed.Height - target.Height).ShouldBeLessThanOrEqualTo(1);
    }

    [WpfFact]
    public void SetFrame_FixedSizeWindow_KeepsItsSizeAndCentersInTheTarget()
    {
        using var test = new TestWindow(resizable: false);
        var window = new Win32Window(test.Handle);
        var before = window.Frame.ShouldNotBeNull();
        var target = new Rect(100, 100, 1001, 801);

        window.SetFrame(target).ShouldBe(CommandOutcome.Moved);
        TestWindow.Pump();

        var after = window.Frame.ShouldNotBeNull();
        after.Width.ShouldBe(before.Width);
        after.Height.ShouldBe(before.Height);
        var expected = Layout.Centered(before, target);
        Math.Abs(after.X - expected.X).ShouldBeLessThanOrEqualTo(1);
        Math.Abs(after.Y - expected.Y).ShouldBeLessThanOrEqualTo(1);
    }

    [WpfFact]
    public void SetFrame_MaximizedWindow_IsRestoredFirst()
    {
        using var test = new TestWindow(state: WindowState.Maximized);
        var window = new Win32Window(test.Handle);

        window.SetFrame(new Rect(200, 150, 601, 451)).ShouldBe(CommandOutcome.Moved);
        TestWindow.Pump();

        Loadstone.UI.Interop.NativeMethods.User32.IsZoomed(test.Handle).ShouldBeFalse();
        window.Frame.ShouldNotBeNull().Width.ShouldBeLessThan(700);
    }

    [WpfFact]
    public void DestroyedWindow_IsUnreadable()
    {
        var test = new TestWindow();
        var window = new Win32Window(test.Handle);
        test.Dispose();

        window.Frame.ShouldBeNull();
        window.SetFrame(new Rect(0, 0, 100, 100)).ShouldBe(CommandOutcome.FrameUnreadable);
    }

    [WpfFact]
    public void Identity_CarriesTheOwningProcess()
    {
        using var test = new TestWindow();
        var window = new Win32Window(test.Handle);
        window.Identity.ShouldBe(new Loadstone.Core.Windows.WindowIdentity(test.Handle, Environment.ProcessId));
    }

    [WpfFact]
    public void WindowLiveness_IsAlive_TracksTheWindow()
    {
        var test = new TestWindow();
        var identity = new Win32Window(test.Handle).Identity;
        WindowLiveness.IsAlive(identity).ShouldBeTrue();
        WindowLiveness.IsAlive(identity with { Pid = identity.Pid + 1 }).ShouldBeFalse();
        test.Dispose();
        WindowLiveness.IsAlive(identity).ShouldBeFalse();
    }
}
