using System.Windows.Threading;
using Loadstone.Core.Drag;
using Loadstone.Core.Geometry;
using Loadstone.Core.Settings;
using Loadstone.Core.Windows;
using Loadstone.UI.Interop;
using Loadstone.UI.Windows;
using Microsoft.Extensions.Logging;
using static Loadstone.UI.Interop.NativeMethods;

namespace Loadstone.UI.Input;

/// <summary>
/// Snaps a window dragged to a screen edge. Listens to the OS move-loop events while drag snapping
/// is on, samples the cursor at 60 Hz during a drag, previews the tile, and commits after the OS
/// has finished the drop so Loadstone's frame is the last write. The decisions live in
/// <see cref="DragSnapTracker{TWindow}"/>.
/// </summary>
internal sealed class DragSnapMonitor : IDisposable
{
    private readonly IMoveSizeEvents _events;
    private readonly WindowDirector _director;
    private readonly ISettingsService _settings;
    private readonly ISnapPreview _preview;
    private readonly ILogger<DragSnapMonitor> _logger;
    private readonly Func<IReadOnlyList<Display>> _displays;
    private readonly Func<Point> _cursor;
    private readonly Func<nint, IMovableWindow?> _windowFactory;
    private readonly DragSnapTracker<IMovableWindow> _tracker = new();
    private readonly DispatcherTimer _timer;
    private bool _started;

    public DragSnapMonitor(IMoveSizeEvents events, WindowDirector director, ISettingsService settings, ISnapPreview preview, ILogger<DragSnapMonitor> logger)
        : this(events, director, settings, preview, logger, Displays.Snapshot, CursorPosition, ManageableWindow)
    {
    }

    internal DragSnapMonitor(
        IMoveSizeEvents events,
        WindowDirector director,
        ISettingsService settings,
        ISnapPreview preview,
        ILogger<DragSnapMonitor> logger,
        Func<IReadOnlyList<Display>> displays,
        Func<Point> cursor,
        Func<nint, IMovableWindow?> windowFactory)
    {
        _events = events;
        _director = director;
        _settings = settings;
        _preview = preview;
        _logger = logger;
        _displays = displays;
        _cursor = cursor;
        _windowFactory = windowFactory;
        _timer = new DispatcherTimer(TimeSpan.FromMilliseconds(16), DispatcherPriority.Input, (_, _) => Tick(), Dispatcher.CurrentDispatcher);
        _timer.Stop();
    }

    /// <summary>True while the hook is installed: drag snapping is on and the system accepted the hook.</summary>
    public bool IsMonitoring => _events.IsInstalled;

    /// <summary>Installs the hook while drag snapping is on and removes it while it is off, following the setting from then on.</summary>
    public void Start()
    {
        if (_started)
        {
            return;
        }

        _started = true;
        _events.Started += OnStarted;
        _events.Ended += OnEnded;
        _settings.Changed += OnSettingsChanged;
        Follow();
    }

    public void Stop()
    {
        if (!_started)
        {
            return;
        }

        _started = false;
        _settings.Changed -= OnSettingsChanged;
        _events.Started -= OnStarted;
        _events.Ended -= OnEnded;
        Uninstall();
    }

    public void Dispose() => Stop();

    private void OnSettingsChanged(object? sender, EventArgs e) => Follow();

    private void Follow()
    {
        if (_settings.Current.DragSnappingEnabled)
        {
            Install();
        }
        else
        {
            Uninstall();
        }
    }

    private void Install()
    {
        if (_events.IsInstalled)
        {
            return;
        }

        if (!_events.Install())
        {
            _logger.LogError("could not install the move-size event hook; drag snapping is off");
            return;
        }

        _logger.LogInformation("drag snapping on");
    }

    private void Uninstall()
    {
        _timer.Stop();
        _tracker.Reset();
        _preview.Hide();
        if (!_events.IsInstalled)
        {
            return;
        }

        _events.Uninstall();
        _logger.LogInformation("drag snapping off");
    }

    private void OnStarted(nint hwnd)
    {
        if (_windowFactory(hwnd) is not { } window || window.Frame is not { } baseline)
        {
            _tracker.Reset();
            _timer.Stop();
            return;
        }

        _tracker.Begin(window, baseline, _displays());
        _timer.Start();
    }

    /// <summary>One cursor sample. Public to tests; the timer calls it at 60 Hz during a drag.</summary>
    internal void Tick()
    {
        if (!_tracker.IsDragging || _tracker.Window is not { } window)
        {
            _timer.Stop();
            return;
        }

        if (_tracker.Update(_cursor(), window.Frame) is { } target)
        {
            _preview.Show(target.Tile.Frame(target.Display.WorkArea));
        }
        else
        {
            _preview.Hide();
        }
    }

    private void OnEnded(nint hwnd)
    {
        _timer.Stop();
        _preview.Hide();
        if (!_tracker.IsDragging || _tracker.Window is not { } window)
        {
            return;
        }

        if (window.Identity.Hwnd != hwnd)
        {
            _tracker.Reset();
            return;
        }

        if (_tracker.End(_cursor(), window.Frame) is not var (dragged, target))
        {
            return;
        }

        var outcome = _director.Snap(target.Tile, dragged, target.Display);
        if (outcome.IsMoved)
        {
            _logger.LogDebug("snapped window {Hwnd} to {Tile}", dragged.Identity.Hwnd, target.Tile);
        }
        else
        {
            _logger.LogError("snap of window {Hwnd} to {Tile} failed: {Outcome}", dragged.Identity.Hwnd, target.Tile, outcome);
            User32.MessageBeep(NativeConstants.MB_OK);
        }
    }

    private static Point CursorPosition()
    {
        User32.GetCursorPos(out var point);
        return new Point(point.X, point.Y);
    }

    /// <summary>The root of the window in the move loop, if Loadstone may manage it.</summary>
    private static IMovableWindow? ManageableWindow(nint hwnd)
    {
        var root = User32.GetAncestor(hwnd, NativeConstants.GA_ROOT);
        if (root == 0)
        {
            root = hwnd;
        }

        return WindowStyles.IsManageable(WindowStyles.Snapshot(root), Environment.ProcessId) ? new Win32Window(root) : null;
    }
}
