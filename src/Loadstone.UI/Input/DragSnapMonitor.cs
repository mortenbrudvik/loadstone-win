using System.Diagnostics;
using System.Windows;
using System.Windows.Threading;
using Loadstone.Core.Drag;
using Loadstone.Core.Geometry;
using Loadstone.Core.Settings;
using Loadstone.Core.Windows;
using Loadstone.UI.Interop;
using Loadstone.UI.Windows;
using Microsoft.Extensions.Logging;
using static Loadstone.UI.Interop.NativeMethods;
using Point = Loadstone.Core.Geometry.Point;

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
    private readonly Func<Point?> _cursor;
    private readonly Func<nint, IMovableWindow?> _windowFactory;
    private readonly DragSnapTracker<IMovableWindow> _tracker = new();
    private readonly DispatcherTimer _timer;
    private bool _started;
    private bool _cursorLost;

    /// <remarks>
    /// The snapshot is bound to this monitor's logger rather than taken through Displays' logger-less
    /// overload: it abandons the whole display list when a monitor cannot be read, and unbound that
    /// report reaches nobody - every drag stops snapping and the log says nothing at all.
    /// </remarks>
    public DragSnapMonitor(IMoveSizeEvents events, WindowDirector director, ISettingsService settings, ISnapPreview preview, ILogger<DragSnapMonitor> logger)
        : this(events, director, settings, preview, logger, Displays.Source(logger), CursorPosition, ManageableWindow)
    {
    }

    internal DragSnapMonitor(
        IMoveSizeEvents events,
        WindowDirector director,
        ISettingsService settings,
        ISnapPreview preview,
        ILogger<DragSnapMonitor> logger,
        Func<IReadOnlyList<Display>> displays,
        Func<Point?> cursor,
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
        _timer = new DispatcherTimer(TimeSpan.FromMilliseconds(16), DispatcherPriority.Input, (_, _) => Tick(), UiDispatcher());
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

    /// <summary>
    /// The dispatcher the 60 Hz timer has to tick on: <see cref="Tick"/> drives the WPF preview
    /// window. Application.Current is null under the test host, whose constructing thread is the UI
    /// thread anyway, so CurrentDispatcher stands in there. The assert is what makes the requirement
    /// explicit: this type is resolved on the UI thread from OnStartup today, and moving that
    /// registration behind a background resolve would otherwise leave drag snapping silently dead -
    /// a DispatcherTimer bound to a thread with no message pump never fires and reports nothing.
    /// </summary>
    private static Dispatcher UiDispatcher()
    {
        var dispatcher = Application.Current?.Dispatcher ?? Dispatcher.CurrentDispatcher;
        Debug.Assert(dispatcher == Dispatcher.CurrentDispatcher, "DragSnapMonitor must be constructed on the UI thread.");
        return dispatcher;
    }

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
        EndDrag();
        if (!_events.IsInstalled)
        {
            return;
        }

        _events.Uninstall();
        _logger.LogInformation("drag snapping off");
    }

    /// <summary>Forgets the drag in progress, if any: stop sampling, forget the window, take the preview off the screen.</summary>
    private void EndDrag()
    {
        _timer.Stop();
        _tracker.Reset();
        _preview.Hide();
    }

    private void OnStarted(nint hwnd)
    {
        try
        {
            _cursorLost = false;
            if (_windowFactory(hwnd) is not { } window || window.ReadFrame() is not { } baseline)
            {
                EndDrag();
                return;
            }

            _tracker.Begin(window, baseline, _displays());
            _timer.Start();
        }
        catch (Exception ex)
        {
            Abandon(ex);
        }
    }

    /// <summary>One cursor sample. Internal so the tests can drive it; the timer calls it at 60 Hz during a drag.</summary>
    internal void Tick()
    {
        if (_tracker.Window is not { } window)
        {
            _timer.Stop();
            return;
        }

        if (window.ReadFrame() is not { } frame)
        {
            // The window died or was closed with the mouse still down, so no MOVESIZEEND is ever
            // coming and this is the only place the drag can end. Without it the timer keeps firing
            // every 16 ms for the rest of the session against a window that will never answer again.
            EndDrag();
            return;
        }

        if (_tracker.Update(Cursor(), frame) is { } target)
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
        try
        {
            _timer.Stop();
            _preview.Hide();
            if (_tracker.Window is not { } window)
            {
                return;
            }

            if (window.Identity.Hwnd != hwnd)
            {
                _tracker.Reset();
                return;
            }

            if (_tracker.End(Cursor(), window.ReadFrame()) is not { } commit)
            {
                return;
            }

            var (dragged, target) = commit;
            var outcome = _director.Snap(target.Tile, dragged, target.Display);
            if (outcome.IsMoved)
            {
                _logger.LogDebug("snapped window {Hwnd} to {Tile}", dragged.Identity.Hwnd, target.Tile);
            }
            else
            {
                _logger.LogError("snap of window {Hwnd} to {Tile} failed: {Outcome}", dragged.Identity.Hwnd, target.Tile, outcome);
                Feedback.Beep();
            }
        }
        catch (Exception ex)
        {
            Abandon(ex);
        }
    }

    /// <summary>
    /// Both move-size handlers run on a WinEvent callback, which USER32 delivers from inside message
    /// retrieval rather than through a window proc: an exception let out of one would unwind into
    /// native frames, where DispatcherUnhandledException never sees it and the app's "log fatal,
    /// restore, die" policy never runs. Log it here instead and drop the drag, so the worst a bad
    /// drag costs is that one drag.
    /// </summary>
    private void Abandon(Exception ex)
    {
        _logger.LogError(ex, "the drag was abandoned");
        EndDrag();
    }

    /// <summary>
    /// A cursor sample, or null when Windows would not give one. Logged once per drag: the read fails
    /// for as long as another desktop holds the input, and the timer asks 60 times a second.
    /// </summary>
    private Point? Cursor()
    {
        var cursor = _cursor();
        if (cursor is null && !_cursorLost)
        {
            _cursorLost = true;
            _logger.LogWarning("the cursor position is unreadable, so this drag will not snap; another desktop holds the input (a UAC prompt, a locked workstation, or a session switch)");
        }

        return cursor;
    }

    /// <summary>
    /// The cursor in physical pixels, or null when GetCursorPos refused. It fails with
    /// ERROR_ACCESS_DENIED whenever the calling thread's desktop is not the input desktop - a UAC
    /// prompt, a lock, an RDP session switch, any of which can land mid-drag - and leaves the POINT
    /// at (0,0). That is not a sentinel but a real coordinate inside the primary display, so taking
    /// it would silently snap the dragged window to the top-left quarter.
    /// </summary>
    private static Point? CursorPosition() =>
        User32.GetCursorPos(out var point) ? new Point(point.X, point.Y) : null;

    /// <summary>The root of the window in the move loop, if Loadstone may manage it.</summary>
    private static IMovableWindow? ManageableWindow(nint hwnd) =>
        WindowStyles.ManageableRoot(hwnd, Environment.ProcessId) is { } root ? Win32Window.For(root) : null;
}
