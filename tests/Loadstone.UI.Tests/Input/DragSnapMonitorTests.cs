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

        public CommandOutcome SetFrame(Rect frame)
        {
            Writes.Add(frame);
            Frame = frame;
            return CommandOutcome.Moved;
        }
    }

    private static readonly Display Primary = new(new Rect(0, 0, 1201, 901), new Rect(0, 0, 1201, 861), 96);
    private static readonly Rect Baseline = new(300, 300, 401, 301);

    private readonly FakeMoveSizeEvents _events = new();
    private readonly InMemorySettings _settings = new();
    private readonly ISnapPreview _preview = Substitute.For<ISnapPreview>();
    private readonly Dictionary<nint, FakeWindow> _windows = new();
    private Point _cursor = new(600, 450);

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
}
