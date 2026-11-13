using System.Security;
using Loadstone.UI.Services;
using Microsoft.Extensions.Logging;
using Shouldly;
using Xunit;

namespace Loadstone.UI.Tests.Services;

public class StartupSplashTests
{
    private static readonly TimeSpan TwoSeconds = TimeSpan.FromSeconds(2);

    /// <summary>
    /// The splash stays up for its minimum however quickly startup finished. The clamp is the part
    /// that breaks something: a startup slower than the minimum leaves a negative remainder, and a
    /// timer handed one never fires, so the splash would never close.
    /// </summary>
    [Theory]
    [InlineData(0, 2000)]
    [InlineData(400, 1600)]
    [InlineData(2000, 0)]
    [InlineData(5000, 0)]
    public void RemainingDelay_WithAnUntouchedSplash_TopsUpToTheMinimum_AndNeverGoesNegative(int shownMs, int expectedMs)
    {
        StartupSplash.RemainingDelay(TimeSpan.FromMilliseconds(shownMs), sinceInteraction: null, TwoSeconds)
            .ShouldBe(TimeSpan.FromMilliseconds(expectedMs));
    }

    /// <summary>
    /// A pointer playing with the splash holds it for a moment after its last move, whichever of the
    /// two waits is longer, and then lets go: the hold is measured from the last interaction, so a
    /// pointer that simply stays where it is cannot keep the splash up.
    /// </summary>
    [Theory]
    [InlineData(500, 100, 1500)]   // early on, the minimum is still the longer wait
    [InlineData(1900, 100, 1400)]  // near the end of the minimum, the hold takes over
    [InlineData(6000, 0, 1500)]    // long past the minimum, a click starts a fresh hold
    [InlineData(6000, 1500, 0)]    // and the hold runs out
    [InlineData(6000, 9000, 0)]
    public void RemainingDelay_AfterAnInteraction_WaitsForTheLongerOfTheTwo(int shownMs, int sinceMs, int expectedMs)
    {
        StartupSplash.RemainingDelay(TimeSpan.FromMilliseconds(shownMs), TimeSpan.FromMilliseconds(sinceMs), TwoSeconds)
            .ShouldBe(TimeSpan.FromMilliseconds(expectedMs));
    }

    /// <summary>
    /// A policy or an ACL can refuse the Personalize key, and Show runs before there is a logger. The
    /// tray's identical read is guarded; this one was not, so a refused read cost the whole splash
    /// where it should cost only the theme. Dark is the fallback, as it is for a value that is not there.
    /// </summary>
    [Fact]
    public void IsAppThemeLight_WhenTheKeyCannotBeOpened_FallsBackToDark()
    {
        var light = true;

        Should.NotThrow(() => light = StartupSplash.IsAppThemeLight(() => throw new SecurityException("refused by policy")));

        light.ShouldBeFalse();
    }

    /// <summary>
    /// The whole life of the splash, on the thread it really runs on: Show returns before the window
    /// exists, the window is built and shown on its own dispatcher, and CloseAsync completes once it
    /// has gone, with nothing to warn about. This thread never pumps a message, which is the point:
    /// during startup the app's thread does not either.
    /// </summary>
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task Show_ThenCloseAsync_RunsTheSplashOnItsOwnThread_AndLeavesNothingToWarnAbout(bool light, bool animate)
    {
        var logger = new RecordingLogger<StartupSplashTests>();

        var splash = StartupSplash.Show(light, animate, minimumShown: TimeSpan.FromMilliseconds(150));
        await splash.CloseAsync(logger).WaitAsync(TimeSpan.FromSeconds(20), TestContext.Current.CancellationToken);

        logger.Entries.Where(entry => entry.Level >= LogLevel.Warning).ShouldBeEmpty();
    }

    /// <summary>CloseAsync is discarded by its caller, so a second call - or one after the window has gone - must also simply complete.</summary>
    [Fact]
    public async Task CloseAsync_AfterTheSplashHasGone_CompletesQuietly()
    {
        var logger = new RecordingLogger<StartupSplashTests>();
        var splash = StartupSplash.Show(light: false, animate: false, minimumShown: TimeSpan.Zero);
        await splash.CloseAsync(logger).WaitAsync(TimeSpan.FromSeconds(20), TestContext.Current.CancellationToken);

        await splash.CloseAsync(logger).WaitAsync(TimeSpan.FromSeconds(20), TestContext.Current.CancellationToken);

        logger.Entries.Where(entry => entry.Level >= LogLevel.Warning).ShouldBeEmpty();
    }
}
