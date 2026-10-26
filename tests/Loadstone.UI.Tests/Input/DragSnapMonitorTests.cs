using Loadstone.Core.Commands;
using Loadstone.Core.Geometry;
using Loadstone.Core.Windows;
using Loadstone.UI.Input;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Shouldly;
using Xunit;

namespace Loadstone.UI.Tests.Input;

public class DragSnapMonitorTests
{
    private sealed class FakeMoveSizeEvents : IMoveSizeEvents
    {
        public bool InstallSucceeds { get; set; } = true;
        public int Installs { get; private set; }
        public int Uninstalls { get; private set; }
        public bool IsInstalled { get; private set; }
        public event Action<nint>? Started;
        public event Action<nint>? Ended;

        public bool Install()
        {
            Installs++;
            IsInstalled = InstallSucceeds;
            return InstallSucceeds;
        }

        public void Uninstall()
        {
            Uninstalls++;
            IsInstalled = false;
        }

        public void RaiseStarted(nint hwnd) => Started?.Invoke(hwnd);
        public void RaiseEnded(nint hwnd) => Ended?.Invoke(hwnd);
    }

    private sealed class FakeWindow(nint hwnd, Rect frame) : IMovableWindow
    {
        public WindowIdentity Identity { get; } = new(hwnd, 100);
        public Rect? Frame { get; set; } = frame;
        public List<Rect> Writes { get; } = [];

        /// <summary>What the next write answers. An elevated window or a dying one refuses the frame.</summary>
        public CommandOutcome NextOutcome { get; set; } = CommandOutcome.Moved;

        /// <summary>Null models a window that died mid-drag, so no MOVESIZEEND is ever coming.</summary>
        public Rect? ReadFrame() => Frame;

        public CommandOutcome SetFrame(Rect frame)
        {
            Writes.Add(frame);
            if (NextOutcome.IsMoved)
            {
                Frame = frame;
            }

            return NextOutcome;
        }

        /// <summary>A dragged window is never maximized by the time it is dropped, so the monitor's tests never see a placement.</summary>
        public WindowPlacement? ReadPlacement() => null;

        public CommandOutcome SetPlacement(WindowPlacement placement) => NextOutcome;
    }

    private static readonly Display Primary = new(new Rect(0, 0, 1201, 901), new Rect(0, 0, 1201, 861), 96);
    private static readonly Rect Baseline = new(300, 300, 401, 301);

    private readonly FakeMoveSizeEvents _events = new();

    // Drag snapping defaults off (Store consent policy), but this fixture is about the monitor, not
    // that default, and every test but one below is written against a hook that starts installed.
    private readonly InMemorySettings _settings = new() { Current = { DragSnappingEnabled = true } };
    private readonly ISnapPreview _preview = Substitute.For<ISnapPreview>();
    private readonly Dictionary<nint, FakeWindow> _windows = new();

    /// <summary>Null models GetCursorPos refusing, which it does whenever another desktop holds the input.</summary>
    private Point? _cursor = new Point(600, 450);

    private DragSnapMonitor NewMonitor() => new(
        _events,
        new WindowDirector(() => [Primary], _ => true),
        _settings,
        _preview,
        NullLogger<DragSnapMonitor>.Instance,
        () => [Primary],
        () => _cursor,
        hwnd => _windows.GetValueOrDefault(hwnd));

    private FakeWindow AddWindow(nint hwnd = 10)
    {
        var window = new FakeWindow(hwnd, Baseline);
        _windows[hwnd] = window;
        return window;
    }

    [WpfFact]
    public void Start_WithDragSnappingOn_InstallsTheHook()
    {
        using var monitor = NewMonitor();
        monitor.Start();
        _events.Installs.ShouldBe(1);
        monitor.IsMonitoring.ShouldBeTrue();
    }

    [WpfFact]
    public void Start_WithDragSnappingOff_DoesNotInstall_AndFollowsTheSettingLater()
    {
        _settings.Current.DragSnappingEnabled = false;
        using var monitor = NewMonitor();
        monitor.Start();
        monitor.IsMonitoring.ShouldBeFalse();

        _settings.Update(s => s.DragSnappingEnabled = true);
        monitor.IsMonitoring.ShouldBeTrue();

        _settings.Update(s => s.DragSnappingEnabled = false);
        monitor.IsMonitoring.ShouldBeFalse();
        _events.Uninstalls.ShouldBe(1);
        _preview.Received().Hide();
    }

    [WpfFact]
    public void Start_WhenTheHookCannotBeInstalled_IsNotMonitoring()
    {
        _events.InstallSucceeds = false;
        using var monitor = NewMonitor();
        monitor.Start();
        monitor.IsMonitoring.ShouldBeFalse();
    }

    [WpfFact]
    public void Drag_ShowsThePreviewOnceTheWindowMovesIntoAZone_AndSnapsOnEnd()
    {
        var window = AddWindow();
        using var monitor = NewMonitor();
        monitor.Start();

        _events.RaiseStarted(10);
        monitor.Tick();
        _preview.DidNotReceive().Show(Arg.Any<Rect>());

        window.Frame = Baseline with { X = 3 };
        _cursor = new Point(3, 450);
        monitor.Tick();
        _preview.Received(1).Show(Tile.LeftHalf.Frame(Primary.WorkArea));

        _events.RaiseEnded(10);
        _preview.Received().Hide();
        window.Writes.ShouldBe([Tile.LeftHalf.Frame(Primary.WorkArea)]);
    }

    [WpfFact]
    public void Drag_EndingBackAtTheBaseline_DoesNotSnap()
    {
        var window = AddWindow();
        using var monitor = NewMonitor();
        monitor.Start();

        _events.RaiseStarted(10);
        window.Frame = Baseline with { X = 3 };
        _cursor = new Point(3, 450);
        monitor.Tick();
        window.Frame = Baseline;
        _events.RaiseEnded(10);

        window.Writes.ShouldBeEmpty();
    }

    [WpfFact]
    public void Drag_OfAWindowTheFactoryRejects_IsIgnored()
    {
        using var monitor = NewMonitor();
        monitor.Start();
        _events.RaiseStarted(99);
        _cursor = new Point(3, 450);
        monitor.Tick();
        _events.RaiseEnded(99);
        _preview.DidNotReceive().Show(Arg.Any<Rect>());
    }

    [WpfFact]
    public void Drag_EndEventForAnotherWindow_ResetsWithoutSnapping()
    {
        var window = AddWindow();
        using var monitor = NewMonitor();
        monitor.Start();

        _events.RaiseStarted(10);
        window.Frame = Baseline with { X = 3 };
        _cursor = new Point(3, 450);
        monitor.Tick();
        _events.RaiseEnded(11);

        window.Writes.ShouldBeEmpty();
        _events.RaiseEnded(10);
        window.Writes.ShouldBeEmpty();
    }

    [WpfFact]
    public void Stop_UninstallsAndHidesThePreview()
    {
        var monitor = NewMonitor();
        monitor.Start();
        monitor.Stop();
        _events.Uninstalls.ShouldBe(1);
        monitor.IsMonitoring.ShouldBeFalse();
        _preview.Received().Hide();
    }

    [WpfFact]
    public void Settings_AnUnrelatedWrite_DoesNotReinstallTheHook()
    {
        // Every ISettingsService.Changed subscriber has to be flip-detecting or idempotent, because
        // any write at all raises Changed. A monitor that re-applied on every notification would drop
        // the hook and take it again - and lose the drag in progress with it - whenever some other
        // setting was saved, which mid-drag means the drop stops snapping with nothing in the log.
        var window = AddWindow();
        using var monitor = NewMonitor();
        monitor.Start();

        _events.RaiseStarted(10);
        window.Frame = Baseline with { X = 3 };
        _cursor = new Point(3, 450);
        monitor.Tick();

        _settings.Update(s => s.Shortcuts["leftHalf"] = "Ctrl+Alt+Shift+Left");

        _events.Installs.ShouldBe(1);
        _events.Uninstalls.ShouldBe(0);
        monitor.IsMonitoring.ShouldBeTrue();

        // And the drag survived it: the tracker still holds the window it armed on.
        monitor.Tick();
        _preview.Received(2).Show(Tile.LeftHalf.Frame(Primary.WorkArea));
    }

    [WpfFact]
    public void Tick_WhenTheDraggedWindowDies_EndsTheDrag()
    {
        // The regression: a window closed with the mouse still down means MOVESIZEEND never arrives,
        // so this sample is the only place the drag can end. Without it the 60 Hz timer went on firing
        // every 16 ms for the rest of the session, asking a window that would never answer again.
        var window = AddWindow();
        using var monitor = NewMonitor();
        monitor.Start();

        _events.RaiseStarted(10);
        window.Frame = Baseline with { X = 3 };
        _cursor = new Point(3, 450);
        monitor.Tick();
        _preview.Received(1).Show(Tile.LeftHalf.Frame(Primary.WorkArea));

        window.Frame = null;
        monitor.Tick();
        _preview.Received().Hide();

        // Ended, not merely skipped: the tracker forgot the window, so a later sample previews nothing
        // and the MOVESIZEEND that is never coming could move nothing if it did arrive.
        window.Frame = Baseline with { X = 3 };
        monitor.Tick();
        _preview.Received(1).Show(Arg.Any<Rect>());
        _events.RaiseEnded(10);
        window.Writes.ShouldBeEmpty();
    }

    [WpfFact]
    public void Drag_WithAnUnreadableCursor_ShowsNoPreview_AndCommitsNothing()
    {
        // GetCursorPos refuses while another desktop holds the input - a UAC prompt, a lock, an RDP
        // session switch - and leaves the POINT at (0,0). That is not a sentinel but a real coordinate
        // inside the primary display, so a monitor that took it would snap the drag to Tile.TopLeft.
        var window = AddWindow();
        using var monitor = NewMonitor();
        monitor.Start();

        _events.RaiseStarted(10);
        window.Frame = Baseline with { X = 3 };
        _cursor = null;
        monitor.Tick();

        _preview.DidNotReceive().Show(Arg.Any<Rect>());

        _events.RaiseEnded(10);
        window.Writes.ShouldBeEmpty();
    }

    [WpfFact]
    public void Drag_WhenTheWindowRefusesTheFrame_LeavesItWhereItWasAndKeepsMonitoring()
    {
        // An elevated window, or one being closed as the drop lands, refuses SetWindowPos. The drop is
        // then a no-op the user has to be told about - the app's one beep - and the drag after it has
        // to work, so the refusal may not leave the monitor holding a dead window.
        var window = AddWindow();
        window.NextOutcome = CommandOutcome.Rejected(5);
        using var monitor = NewMonitor();
        monitor.Start();

        _events.RaiseStarted(10);
        window.Frame = Baseline with { X = 3 };
        _cursor = new Point(3, 450);
        monitor.Tick();
        _events.RaiseEnded(10);

        window.Writes.ShouldBe([Tile.LeftHalf.Frame(Primary.WorkArea)]);
        window.Frame.ShouldBe(Baseline with { X = 3 });
        _preview.Received().Hide();
        monitor.IsMonitoring.ShouldBeTrue();

        // The next drag still snaps, this time with a window that accepts the frame. It starts from
        // where the refused drop left it, so the drag has to move it somewhere else to arm at all.
        window.NextOutcome = CommandOutcome.Moved;
        _events.RaiseStarted(10);
        window.Frame = Baseline with { X = 5 };
        monitor.Tick();
        _events.RaiseEnded(10);
        window.Frame.ShouldBe(Tile.LeftHalf.Frame(Primary.WorkArea));
    }
}
