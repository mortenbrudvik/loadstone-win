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

    [Fact]
    public void Resolve_NeverReturnsAWindowOfTheOwnProcess()
    {
        // Whatever is in the foreground while the tests run, it is not this test host.
        var resolved = FocusedWindow.Resolve();
        if (resolved is { } hwnd)
        {
            Win32Window.ProcessIdOf(hwnd).ShouldNotBe(Environment.ProcessId);
        }
    }
}
