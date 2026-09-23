using System.Text.RegularExpressions;
using Shouldly;
using Xunit;

namespace Loadstone.UI.Tests;

/// <summary>
/// Startup behaviour that no test can exercise by constructing <c>App</c>: it is the WPF
/// <see cref="System.Windows.Application"/> and the real <c>OnStartup</c> takes the single-instance
/// mutex. These pin the source instead.
/// </summary>
public sealed class AppStartupTests
{
    /// <summary>
    /// Settings is opened by the tray, a left-click on the icon, or a second launch. Startup used to
    /// call <c>ShowSettings()</c> when settings.json was missing, which is every launch until the user
    /// first changes a setting — a window the user did not ask for, on a tray-only app.
    /// </summary>
    [Fact]
    public void App_DoesNotInvokeShowSettingsFromStartup()
    {
        var text = RepoFile.Text("src/Loadstone.UI/App.xaml.cs");

        Regex.Matches(text, @"ShowSettings\s*\(\s*\)\s*;").Count.ShouldBe(0);
    }

    [Fact]
    public void App_GivesWindowsSnapBackWhenTheHookIsNotInstalled()
    {
        var body = Method(RepoFile.Text("src/Loadstone.UI/App.xaml.cs"), "private void SyncNativeSnapToDragMonitor()");

        body.ShouldContain("DragSnappingEnabled");
        body.ShouldContain("IsMonitoring");
        var stop = body.IndexOf("StopFollowing()", StringComparison.Ordinal);
        var restore = body.IndexOf("Restore()", StringComparison.Ordinal);
        stop.ShouldBeGreaterThanOrEqualTo(0);
        restore.ShouldBeGreaterThan(stop);
    }

    [Fact]
    public void App_OpensTheShowSettingsEventBeforeTheMutex_AndHandsOffForegroundBeforeSignaling()
    {
        var body = Method(RepoFile.Text("src/Loadstone.UI/App.xaml.cs"), "private EventWaitHandle? TryBecomeTheOnlyInstance()");
        var open = body.IndexOf("OpenShowSettingsEvent()", StringComparison.Ordinal);
        var acquire = body.IndexOf("TryAcquire()", StringComparison.Ordinal);
        var allow = body.IndexOf("AllowSetForegroundWindow(", StringComparison.Ordinal);
        var signal = body.IndexOf(".Set()", StringComparison.Ordinal);

        open.ShouldBeGreaterThanOrEqualTo(0);
        acquire.ShouldBeGreaterThan(open);
        allow.ShouldBeGreaterThan(acquire);
        signal.ShouldBeGreaterThan(allow);
    }

    [Fact]
    public void App_ShowsTheSplashOnlyAfterTheSingleInstanceHandshake()
    {
        var body = Method(RepoFile.Text("src/Loadstone.UI/App.xaml.cs"), "protected override void OnStartup(StartupEventArgs e)");
        var handshake = body.IndexOf("TryBecomeTheOnlyInstance()", StringComparison.Ordinal);
        var splash = body.IndexOf("StartupSplash.Show()", StringComparison.Ordinal);

        handshake.ShouldBeGreaterThanOrEqualTo(0);
        splash.ShouldBeGreaterThan(handshake);
    }

    [Fact]
    public void App_RestoresEdgeSnappingOnExitAndOnSessionEnd()
    {
        var text = RepoFile.Text("src/Loadstone.UI/App.xaml.cs");
        Method(text, "protected override void OnExit(ExitEventArgs e)").ShouldContain("Restore()");
        Method(text, "private void RestoreOnSessionEnd(").ShouldContain("RestoreNativeSnap");
    }

    private static string Method(string source, string signature)
    {
        var at = source.IndexOf(signature, StringComparison.Ordinal);
        at.ShouldBeGreaterThanOrEqualTo(0, signature);
        var open = source.IndexOf('{', at);
        var depth = 0;
        for (var i = open; i < source.Length; i++)
        {
            if (source[i] == '{')
            {
                depth++;
            }
            else if (source[i] == '}')
            {
                depth--;
                if (depth == 0)
                {
                    return source[open..(i + 1)];
                }
            }
        }

        throw new InvalidOperationException(signature);
    }
}
