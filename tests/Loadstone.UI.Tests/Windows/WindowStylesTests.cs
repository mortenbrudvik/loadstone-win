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

    /// <summary>
    /// Symptom without the walk: a hotkey pressed after clicking inside a control moves the control's
    /// own HWND, so a toolbar or a document pane jumps to the left half while the app stays where it
    /// was. Every entry point hands over the handle it was given, and that handle is usually a child.
    /// </summary>
    [WpfFact]
    public void ManageableRoot_ChildHandle_ResolvesToTheTopLevelWindow()
    {
        using var test = new TestWindow();
        using var child = test.CreateChild();

        child.Handle.ShouldNotBe(test.Handle);
        WindowStyles.ManageableRoot(child.Handle, ownPid: -1).ShouldBe(test.Handle);
    }

    /// <summary>
    /// A root that fails the manageability rule comes back as null rather than as itself: the caller
    /// beeps instead of moving a window Loadstone has no business moving. A message-only window is the
    /// shape of Loadstone's own hotkey window — never visible, no caption, no sizing frame — and
    /// whichever handle GetAncestor answers for it, none of them is manageable.
    /// </summary>
    [WpfFact]
    public void ManageableRoot_UnmanageableWindow_IsNull()
    {
        using var messageOnly = TestWindow.CreateMessageOnlyWindow();

        WindowStyles.ManageableRoot(messageOnly.Handle, ownPid: -1).ShouldBeNull();
    }

    /// <summary>The live-handle overload answers the same as the snapshot one, including the own-process refusal.</summary>
    [WpfFact]
    public void IsManageable_LiveHandle_MatchesTheSnapshotRule()
    {
        using var test = new TestWindow();

        WindowStyles.IsManageable(test.Handle, ownPid: -1).ShouldBeTrue();
        WindowStyles.IsManageable(test.Handle, Environment.ProcessId).ShouldBeFalse();
    }
}
