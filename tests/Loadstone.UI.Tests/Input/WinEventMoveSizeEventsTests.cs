using Loadstone.UI.Input;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using Xunit;

namespace Loadstone.UI.Tests.Input;

public class WinEventMoveSizeEventsTests
{
    /// <summary>
    /// The real SetWinEventHook, with no drag: all this type owes its caller is that the hook is taken
    /// once and given back once. DragSnapMonitor drives both from a settings change, so an Install that
    /// took a second hook would leak one per write, and an Uninstall that fell over on an idle hook
    /// would take the settings change that turned drag snapping off down with it.
    /// </summary>
    [WpfFact]
    public void InstallAndUninstall_AreIdempotent()
    {
        var events = new WinEventMoveSizeEvents(NullLogger<WinEventMoveSizeEvents>.Instance);
        try
        {
            events.IsInstalled.ShouldBeFalse();

            events.Install().ShouldBeTrue();
            events.IsInstalled.ShouldBeTrue();

            events.Install().ShouldBeTrue();
            events.IsInstalled.ShouldBeTrue();
        }
        finally
        {
            // A WinEvent hook is process-wide, so it comes off however this test ends.
            events.Uninstall();
        }

        events.IsInstalled.ShouldBeFalse();

        // Uninstalling what is not installed is a no-op, not a failure.
        events.Uninstall();
        events.IsInstalled.ShouldBeFalse();
    }
}
