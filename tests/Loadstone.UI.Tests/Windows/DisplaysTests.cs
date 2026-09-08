using Loadstone.UI.Windows;
using Microsoft.Extensions.Logging;
using Shouldly;
using Xunit;

namespace Loadstone.UI.Tests.Windows;

public class DisplaysTests
{
    [Fact]
    public void Snapshot_ReturnsThePrimaryDisplayFirst_WithSaneGeometry()
    {
        var displays = Displays.Snapshot();
        displays.Count.ShouldBeGreaterThan(0);

        var primary = displays[0];
        primary.Frame.Contains(new Loadstone.Core.Geometry.Point(0, 0)).ShouldBeTrue("the primary display's frame starts at the origin");
        primary.Dpi.ShouldBeGreaterThanOrEqualTo(96);

        foreach (var display in displays)
        {
            display.WorkArea.Width.ShouldBeLessThanOrEqualTo(display.Frame.Width);
            display.WorkArea.Height.ShouldBeLessThanOrEqualTo(display.Frame.Height);
            display.WorkArea.Left.ShouldBeGreaterThanOrEqualTo(display.Frame.Left);
            display.WorkArea.Top.ShouldBeGreaterThanOrEqualTo(display.Frame.Top);
        }
    }

    /// <summary>
    /// Primary first is a contract, not a coincidence of enumeration order: WindowDirector falls back
    /// to displays[0] for a window it cannot place, so a snapshot in whatever order Windows enumerated
    /// would drop that window onto a secondary monitor. Symptom on the desk: a tile command on a
    /// window Loadstone cannot locate throws it onto the wrong screen.
    /// </summary>
    /// <remarks>
    /// A runner with one display cannot exercise the ordering at all — every one-element list is
    /// already sorted — so on such a machine the assertions below do not run and this test proves
    /// nothing. It earns its keep on a multi-monitor desk.
    /// </remarks>
    [Fact]
    public void Snapshot_WithMoreThanOneDisplay_PutsThePrimaryFirst()
    {
        var displays = Displays.Snapshot();
        if (displays.Count < 2)
        {
            return;
        }

        // Windows puts the primary display's top-left corner at the origin and no other display can be
        // there, so which one is primary is decidable without asking Win32 a second time.
        displays[0].Frame.Left.ShouldBe(0);
        displays[0].Frame.Top.ShouldBe(0);
        displays.Skip(1).ShouldAllBe(d => d.Frame.Left != 0 || d.Frame.Top != 0);
    }

    /// <summary>
    /// A monitor whose GetMonitorInfo fails abandons the whole snapshot and says so in the log, because
    /// a silently shorter list is the more dangerous answer: the user's window lands on the left half
    /// of the *primary*, reported as moved, and Next Display becomes "only one display attached" on a
    /// two-monitor desk. That failure cannot be provoked from a test — there is no seam between
    /// Snapshot and EnumDisplayMonitors — so what is pinned here is the other half of the same
    /// promise: a healthy desk takes the quiet path and produces displays without logging a failure.
    /// </summary>
    [Fact]
    public void Snapshot_OnAHealthyDesk_ProducesDisplaysAndLogsNoFailure()
    {
        var logger = new RecordingLogger();

        var displays = Displays.Snapshot(logger);

        displays.Count.ShouldBeGreaterThan(0);
        logger.Entries.Where(e => e.Level >= LogLevel.Error).ShouldBeEmpty();
    }

    /// <summary>An ILogger that keeps what it was told, so a test can ask what was reported.</summary>
    private sealed class RecordingLogger : ILogger
    {
        public List<(LogLevel Level, string Message)> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            Entries.Add((logLevel, formatter(state, exception)));
        }
    }
}
