using Loadstone.UI.Services;
using Shouldly;
using Xunit;

namespace Loadstone.UI.Tests.Services;

public class SingleInstanceTests
{
    [Fact]
    public void TryAcquire_SecondCallerGetsNothing_UntilTheFirstReleases()
    {
        using var first = SingleInstance.TryAcquire();
        first.ShouldNotBeNull();
        SingleInstance.TryAcquire().ShouldBeNull();
        first.ReleaseMutex();
        first.Dispose();

        using var again = SingleInstance.TryAcquire();
        again.ShouldNotBeNull();
        again.ReleaseMutex();
    }

    [Fact]
    public void SignalShowSettings_ReachesTheListener()
    {
        using var signalled = new ManualResetEventSlim();
        using var listener = SingleInstance.ListenForShowSettings(() => signalled.Set());

        SingleInstance.SignalShowSettings();

        signalled.Wait(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken).ShouldBeTrue();
    }
}
