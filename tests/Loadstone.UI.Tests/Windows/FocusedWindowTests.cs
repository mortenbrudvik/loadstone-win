using Loadstone.UI.Windows;
using Shouldly;
using Xunit;

namespace Loadstone.UI.Tests.Windows;

public class FocusedWindowTests
{
    [WpfFact]
    public void Snapshot_OfARealWindow_IsManageableForAnotherProcess_ButNotForItsOwn()
    {
        using var test = new TestWindow();
        var snapshot = WindowStyles.Snapshot(test.Handle);

        snapshot.IsWindow.ShouldBeTrue();
        snapshot.IsVisible.ShouldBeTrue();
        snapshot.Pid.ShouldBe(Environment.ProcessId);
        WindowStyles.IsManageable(snapshot, ownPid: -1).ShouldBeTrue();
        WindowStyles.IsManageable(snapshot, ownPid: Environment.ProcessId).ShouldBeFalse();
    }

    /// <summary>
    /// Symptom this guards: a hotkey pressed while Loadstone's own Settings window is in front tiles
    /// that window instead of doing nothing.
    /// </summary>
    /// <remarks>
    /// This test used to read the real foreground window and assert inside `if (resolved is { } hwnd)`,
    /// so on a desk with nothing manageable in front — the normal state while the suite runs — it went
    /// green having asserted nothing at all. It now judges a window the test owns and put in front,
    /// through the ownPid overload, so the assertion cannot be skipped past. When Windows refuses the
    /// foreground to a background test host the scenario does not exist on that machine, and the test
    /// says so out loud rather than passing on an empty branch.
    /// </remarks>
    [WpfFact]
    public void Resolve_WhenTheForegroundWindowBelongsToTheOwnProcess_IsNull()
    {
        using var test = new TestWindow();
        Assert.SkipUnless(test.TakeForeground(), "Windows kept the foreground away from the test host, so nothing of ours is in front to refuse");

        // Both directions on the same window, so the null below is the rule at work and not an empty
        // desktop: any other process may manage this window, its owner may not.
        FocusedWindow.Resolve(ownPid: -1).ShouldBe(test.Handle);
        FocusedWindow.Resolve(Environment.ProcessId).ShouldBeNull();
    }

    /// <summary>
    /// The ancestor walk. Symptom without it: a hotkey pressed after clicking inside a control moves
    /// that control's HWND — a toolbar or a document pane jumps to the left half while the app itself
    /// stays put.
    /// </summary>
    /// <remarks>
    /// Windows reports the top-level window from GetForegroundWindow even while a child holds focus, so
    /// what this pins at the FocusedWindow seam is that a live child inside the focused window never
    /// comes back from Resolve. The walk itself — child handle in, root handle out — is pinned
    /// directly by WindowStylesTests.ManageableRoot_ChildHandle_ResolvesToTheTopLevelWindow.
    /// </remarks>
    [WpfFact]
    public void Resolve_WithAChildHwndInTheFocusedWindow_ReturnsTheTopLevelWindow()
    {
        using var test = new TestWindow();
        using var child = test.CreateChild();
        Assert.SkipUnless(test.TakeForeground(), "Windows kept the foreground away from the test host, so there is no focused window to walk up from");

        var resolved = FocusedWindow.Resolve(ownPid: -1);
        resolved.ShouldBe(test.Handle);
        resolved.ShouldNotBe(child.Handle);
    }
}
