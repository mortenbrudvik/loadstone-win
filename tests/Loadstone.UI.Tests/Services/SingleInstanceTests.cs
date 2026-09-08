using Loadstone.UI.Services;
using Shouldly;
using Xunit;

namespace Loadstone.UI.Tests.Services;

public class SingleInstanceTests
{
    // Never the production names: those belong to a Loadstone the user may be running right now.
    private readonly string _name = "Loadstone-Tests-" + Guid.NewGuid().ToString("N");

    [Fact]
    public void TryAcquire_SecondCallerGetsNothing_UntilTheFirstReleases()
    {
        using var first = SingleInstance.TryAcquire(_name);
        first.ShouldNotBeNull();
        SingleInstance.TryAcquire(_name).ShouldBeNull();
        first.ReleaseMutex();
        first.Dispose();

        using var again = SingleInstance.TryAcquire(_name);
        again.ShouldNotBeNull();
        again.ReleaseMutex();
    }

    [Fact]
    public void SignalShowSettings_ReachesTheListener()
    {
        using var signalled = new ManualResetEventSlim();
        using var listener = SingleInstance.ListenForShowSettings(() => signalled.Set(), _name);

        SingleInstance.SignalShowSettings(_name);

        signalled.Wait(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken).ShouldBeTrue();
    }

    [Fact]
    public void TheProductionNames_AreWhatTheAppUses()
    {
        SingleInstance.DefaultMutexName.ShouldBe("Loadstone-SingleInstance");
        SingleInstance.DefaultEventName.ShouldBe("Loadstone-ShowSettings");
    }
}
