using Loadstone.UI.Interop;
using Loadstone.UI.Windows;
using Shouldly;
using Xunit;

namespace Loadstone.UI.Tests.Windows;

public class WindowStylesTests
{
    private static readonly WindowSnapshot Normal = new(
        IsWindow: true, IsVisible: true, IsCloaked: false, Pid: 4242, ClassName: "Notepad",
        Style: NativeConstants.WS_CAPTION | NativeConstants.WS_THICKFRAME, ExStyle: 0);

    [Fact]
    public void IsManageable_NormalWindow_IsTrue()
    {
        WindowStyles.IsManageable(Normal, ownPid: 1).ShouldBeTrue();
    }

    [Fact]
    public void IsManageable_OwnProcess_IsFalse()
    {
        WindowStyles.IsManageable(Normal, ownPid: 4242).ShouldBeFalse();
    }

    [Fact]
    public void IsManageable_NotAWindow_OrHidden_OrCloaked_IsFalse()
    {
        WindowStyles.IsManageable(Normal with { IsWindow = false }, 1).ShouldBeFalse();
        WindowStyles.IsManageable(Normal with { IsVisible = false }, 1).ShouldBeFalse();
        WindowStyles.IsManageable(Normal with { IsCloaked = true }, 1).ShouldBeFalse();
    }

    [Theory]
    [InlineData("Progman")]
    [InlineData("WorkerW")]
    [InlineData("Shell_TrayWnd")]
    [InlineData("Shell_SecondaryTrayWnd")]
    public void IsManageable_ShellClasses_AreFalse(string className)
    {
        WindowStyles.IsManageable(Normal with { ClassName = className }, 1).ShouldBeFalse();
    }

    [Fact]
    public void IsManageable_ToolWindow_IsFalse()
    {
        WindowStyles.IsManageable(Normal with { ExStyle = NativeConstants.WS_EX_TOOLWINDOW }, 1).ShouldBeFalse();
    }

    [Fact]
    public void IsManageable_NeedsACaptionOrASizingFrame()
    {
        WindowStyles.IsManageable(Normal with { Style = 0 }, 1).ShouldBeFalse();
        WindowStyles.IsManageable(Normal with { Style = NativeConstants.WS_CAPTION }, 1).ShouldBeTrue();
        WindowStyles.IsManageable(Normal with { Style = NativeConstants.WS_THICKFRAME }, 1).ShouldBeTrue();
        // WS_BORDER alone is only half of WS_CAPTION.
        WindowStyles.IsManageable(Normal with { Style = NativeConstants.WS_BORDER }, 1).ShouldBeFalse();
    }
}
