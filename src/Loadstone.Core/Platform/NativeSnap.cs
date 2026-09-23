using Loadstone.Core.Settings;
using Microsoft.Extensions.Logging;

namespace Loadstone.Core.Platform;

/// <summary>
/// Windows snaps a window itself when you drag it to an edge, which fights Loadstone's own
/// snapping. While drag snapping is on this turns the dock-moving and snap-sizing flags off,
/// remembers what the user had, and puts it back on restore. The capture is parked in
/// <see cref="INativeSnapMarker"/> for the length of the run, so a crash or force-quit is detected
/// at the next launch and undone before a new baseline is captured.
/// </summary>
/// <remarks>
/// Every failure path here bends the same way: keep the marker. It is the only record of what the
/// user had, and a run that drops it turns a temporary loss of Windows' edge snapping into a
/// permanent one - the next launch reads Loadstone's own false and records that as the baseline, and
/// no restart or uninstall can work out what the value should have been.
/// </remarks>
public sealed class NativeSnap
{
    /// <summary>
    /// Every flag this class manages; adding a <see cref="NativeSnapFlag"/> is all it takes to manage one more.
    /// Read-only for the same reason as <see cref="Commands.WindowCommands.All"/>: Enum.GetValues hands back
    /// a plain array, and a caller who cast it back could overwrite the list for every other caller.
    /// </summary>
    public static IReadOnlyList<NativeSnapFlag> ManagedFlags { get; } = Array.AsReadOnly(Enum.GetValues<NativeSnapFlag>());

    private readonly INativeSnapFlags _native;
    private readonly INativeSnapMarker _marker;
    private readonly ISettingsService _settings;
    private readonly ILogger<NativeSnap> _logger;

    /// <summary>The user's values before Loadstone touched them; null while not suppressing.</summary>
    /// <remarks>
    /// It stays the whole baseline across a partial restore. "What the user had" is still true about
    /// a flag whose restore write failed, and narrowing it there once meant a later re-suppress wrote
    /// only the failures back off, leaving the rest on alongside Loadstone's own drag snapping.
    /// </remarks>
    private Dictionary<NativeSnapFlag, bool>? _captured;

    /// <summary>
    /// The flags a partial <see cref="Restore"/> could not put back, so they are still off and are
    /// all the marker holds; null while the marker holds the whole of <see cref="_captured"/>.
    /// </summary>
    private Dictionary<NativeSnapFlag, bool>? _unrestored;

    /// <summary>Whether <see cref="Follow"/> is subscribed, and the setting value it last acted on.</summary>
    private bool _following;
    private bool _dragSnapping;

    /// <summary>
    /// Every entry point takes this. The hot path never contends - every production caller is on the
    /// UI thread - but the crash path is not: App restores from the faulting thread when the
    /// dispatcher cannot be reached in time, and this is what keeps that restore from interleaving
    /// with a Changed handler still running on the UI thread. Reentrant by design, because
    /// <see cref="Suppress"/> clears the legacy marker through <see cref="ISettingsService.Update"/>,
    /// whose Changed reaches <see cref="OnSettingsChanged"/> on the same thread.
    /// </summary>
    private readonly Lock _lock = new();

    public NativeSnap(
        INativeSnapFlags native,
        INativeSnapMarker marker,
        ISettingsService settings,
        ILogger<NativeSnap> logger)
    {
        _native = native;
        _marker = marker;
        _settings = settings;
        _logger = logger;
    }

    /// <summary>
    /// Whether Loadstone is holding the user's values with at least one flag turned off for them. A
    /// partial restore leaves this true: the flags whose write failed are still off and still parked.
    /// </summary>
    public bool IsSuppressing => _captured is not null;

    /// <summary>
    /// Applies the drag-snapping setting now and follows it from then on. Safe to call more than once.
    /// </summary>
    /// <remarks>
    /// The follow re-applies only when the setting actually flips. The marker now has its own store,
    /// so parking it no longer raises <see cref="ISettingsService.Changed"/> and most of the
    /// re-entrancy this guard was written for is gone; it stays because clearing the marker's old
    /// home still raises Changed once per upgraded profile, and because the failure it prevents -
    /// re-suppressing on top of the values <see cref="Restore"/> has just put back, so the next
    /// capture records Loadstone's own false - is not recoverable.
    /// </remarks>
    public void Follow()
    {
        lock (_lock)
        {
            if (_following)
            {
                return;
            }

            _following = true;
            _dragSnapping = _settings.Current.DragSnappingEnabled;
            Apply(_dragSnapping);
            _settings.Changed += OnSettingsChanged;
        }
    }

    /// <summary>Stops following the setting. Safe to call when not following.</summary>
    public void StopFollowing()
    {
        lock (_lock)
        {
            if (!_following)
            {
                return;
            }

            _following = false;
            _settings.Changed -= OnSettingsChanged;
        }
    }

    /// <summary>Suppresses Windows' edge snapping while Loadstone's drag snapping is on, restores it while off.</summary>
    public void Apply(bool dragSnappingEnabled)
    {
        lock (_lock)
        {
            if (dragSnappingEnabled)
            {
                Suppress();
            }
            else
            {
                Restore();
            }
        }
    }

    /// <summary>Writes the captured values back and clears the marker. Safe to call when not suppressing.</summary>
    /// <remarks>
    /// A flag whose write fails stays off, stays parked and stays the only thing this run still has
    /// to put back, so the next attempt - a later Restore or the next launch's recovery - tries it
    /// again. Clearing the marker on a failed write would leave that flag off with nothing recording
    /// what it used to be, which is exactly the state recovery exists to undo. The baseline itself is
    /// never narrowed: see <see cref="_captured"/>.
    /// </remarks>
    public void Restore()
    {
        lock (_lock)
        {
            var captured = _captured;
            if (captured is null)
            {
                // Drag snapping is off, or this process never captured. A marker can still be sitting
                // there from a run that died after the setting was saved off and before the flags were
                // put back — or from a restore write that failed. Reading it only on the way to
                // suppressing would leave Windows' edge snapping off for good.
                RecoverWithoutSuppressing();
                return;
            }

            // Only the flags still holding Loadstone's false: an earlier partial restore put the rest back.
            var unrestored = new Dictionary<NativeSnapFlag, bool>();
            foreach (var (flag, value) in _unrestored ?? captured)
            {
                // The user can turn a flag back on in Windows Settings mid-run. It still holds the false
                // Loadstone wrote only if they did not, so anything else is their choice, not ours to
                // overwrite. An unreadable flag is restored: we cannot tell, and putting back what we
                // took is the promise.
                if (_native.Read(flag) is true)
                {
                    _logger.LogInformation("leaving Windows edge snapping flag {Flag} alone; it was turned back on during the run", flag);
                    continue;
                }

                if (!PutBack(flag, value))
                {
                    unrestored[flag] = value;
                }
            }

            if (unrestored.Count == 0)
            {
                if (!_marker.Write(null))
                {
                    // Nothing is suppressed any more either way, so this is the one place a failed marker
                    // write costs nothing: every flag is back at the value the stale marker holds for it,
                    // so the next launch's recovery only ever writes back what is already there.
                    _logger.LogError("the crash marker could not be cleared; the next launch will recover from values that are already in place");
                }

                _captured = null;
                _unrestored = null;
                return;
            }

            if (!_marker.Write(Park(unrestored)))
            {
                // The wider marker this narrowing would have replaced is still parked, which is safe -
                // the extra flags are already back at the values in it - but _unrestored must not claim
                // otherwise, or a later Restore would leave those extras parked with nothing to clear them.
                _logger.LogError("the crash marker could not be narrowed to the flags that are still off; leaving the wider one parked");
                _unrestored = null;
                return;
            }

            _unrestored = unrestored;
        }
    }

    private void OnSettingsChanged(object? sender, EventArgs e)
    {
        lock (_lock)
        {
            var wanted = _settings.Current.DragSnappingEnabled;
            if (wanted == _dragSnapping)
            {
                return;
            }

            // Assign before applying, never after: a re-entrant call has to see the new value or the
            // flags flip straight back.
            _dragSnapping = wanted;
            Apply(wanted);
        }
    }

    private void Suppress()
    {
        var captured = _captured;
        if (captured is null)
        {
            captured = CaptureBaseline();
            if (captured is null)
            {
                // The marker did not reach its store. Turning the flags off anyway would leave a
                // force-quit with nothing recording what the user had, and that loss is permanent;
                // leaving Windows' edge snapping on merely means it fights ours for this run.
                return;
            }

            _captured = captured;

            // Only now that the new store holds the marker: see ClearLegacyMarker.
            ClearLegacyMarker();
        }
        else if (_unrestored is not null)
        {
            // A partial restore put some flags back and narrowed the marker to the rest. We are about
            // to turn the whole baseline off again, so the whole baseline has to be parked again
            // first: parking it after the writes would leave a force-quit in between with a flag off
            // and nothing recording what the user had it at.
            if (!_marker.Write(Park(captured)))
            {
                _logger.LogError("the crash marker could not be re-parked; leaving Windows edge snapping as it is rather than turning flags off with no record of what they were");
                return;
            }

            _unrestored = null;
        }

        foreach (var flag in captured.Keys)
        {
            if (!_native.Write(flag, false) || _native.Read(flag) != false)
            {
                _logger.LogError("could not turn off Windows edge snapping flag {Flag}; it will fight drag snapping", flag);
            }
        }
    }

    /// <summary>
    /// Recovers from an unclean exit, works out what the user has now, and parks it in one write.
    /// Null when the marker could not be parked, which is the caller's cue to leave the flags alone.
    /// </summary>
    /// <remarks>
    /// Recovery deliberately does not clear the marker itself: the new baseline replaces it in a
    /// single write, so there is never a moment where a flag is off and nothing on disk says so.
    /// </remarks>
    private Dictionary<NativeSnapFlag, bool>? CaptureBaseline()
    {
        var unrecovered = RecoverFromUncleanExit(out var unreadable);
        if (unreadable)
        {
            // The file is still the only record. Reading the live flags here would record the false
            // a crashed run left behind, and writing that over the marker would make it permanent.
            _logger.LogError("the crash marker could not be read; leaving Windows edge snapping alone for this run");
            return null;
        }

        var baseline = new Dictionary<NativeSnapFlag, bool>();
        foreach (var flag in ManagedFlags)
        {
            if (unrecovered.TryGetValue(flag, out var parked))
            {
                // The recovery write failed, so the flag still holds the previous run's false.
                // Reading it now would record that as the user's value; the marker is the only truth
                // left about this flag.
                baseline[flag] = parked;
            }
            else if (_native.Read(flag) is { } value)
            {
                baseline[flag] = value;
            }
            else
            {
                _logger.LogWarning("could not read Windows edge snapping flag {Flag}; leaving it alone", flag);
            }
        }

        if (!_marker.Write(baseline.Count > 0 ? Park(baseline) : null))
        {
            _logger.LogError("the crash marker could not be parked; leaving Windows edge snapping alone for this run");
            return null;
        }

        return baseline;
    }

    /// <summary>Puts a parked marker back without capturing a baseline and without turning anything off.</summary>
    private void RecoverWithoutSuppressing()
    {
        var unrecovered = RecoverFromUncleanExit(out var unreadable);
        if (unreadable)
        {
            return;
        }

        if (unrecovered.Count == 0)
        {
            if (!_marker.Write(null))
            {
                // Every flag is already back, so the next launch's recovery only rewrites what is there.
                _logger.LogError("the crash marker could not be cleared after recovery; the next launch will recover from values that are already in place");
                return;
            }

            ClearLegacyMarker();
            return;
        }

        if (!_marker.Write(Park(unrecovered)))
        {
            _logger.LogError("the crash marker could not be narrowed to the flags that are still off; leaving the wider one parked");
        }
    }

    /// <summary>
    /// A run that crashed or was force-quit never reached Restore, so its marker is still parked and
    /// the user's flags are still off. Put them back before capturing a new baseline: capturing
    /// first would record Loadstone's own false as "what the user had" and make the change permanent.
    /// A flag that reads true is left alone — the user has had the whole gap between the two runs to
    /// turn it back on, and writing the parked false over that would make their change permanent.
    /// </summary>
    /// <returns>The flags whose recovery write failed and so still hold the wrong value.</returns>
    private Dictionary<NativeSnapFlag, bool> RecoverFromUncleanExit(out bool unreadable)
    {
        var stored = ReadMarker(out unreadable);
        var unrecovered = new Dictionary<NativeSnapFlag, bool>();
        if (unreadable || stored.Count == 0)
        {
            return unrecovered;
        }

        _logger.LogWarning("restoring Windows edge snapping flags left behind by an unclean exit");
        foreach (var (flag, value) in stored)
        {
            // The same guard as Restore, and safer here: the run that parked this wrote false by
            // construction and never put it back, so a flag reading true was turned on by someone
            // other than Loadstone - and had the whole gap between the two runs to be. Skipping it
            // only ever changes anything when the parked value is false; the ordinary case, a parked
            // true over the false the crash left behind, still writes.
            if (_native.Read(flag) is true)
            {
                _logger.LogInformation("leaving Windows edge snapping flag {Flag} alone; it was turned back on since the run that parked it", flag);
                continue;
            }

            if (!PutBack(flag, value))
            {
                unrecovered[flag] = value;
            }
        }

        return unrecovered;
    }

    /// <summary>
    /// Writes <paramref name="value"/> and keeps it only when a following read agrees. A write that
    /// reports success without changing the flag — the way a BOOL passed in the wrong parameter
    /// does — would otherwise drop the only record of what the user had.
    /// </summary>
    private bool PutBack(NativeSnapFlag flag, bool value)
    {
        var written = _native.Write(flag, value);
        var read = _native.Read(flag);
        if (written && read == value)
        {
            return true;
        }

        _logger.LogError(
            "could not restore Windows edge snapping flag {Flag} to {Value}; the write returned {Written} and the flag now reads {Read}; keeping it parked",
            flag, value, written, read);
        return false;
    }

    /// <summary>The parked values, narrowed to the flags this build knows.</summary>
    /// <remarks>
    /// Dropping an unrecognised name is not tidiness. It cannot be written back, so recovery would
    /// fail for it, the failure would carry it into the new baseline, and the baseline is parked
    /// again — an error logged at every launch for the rest of the profile's life.
    /// </remarks>
    private Dictionary<NativeSnapFlag, bool> ReadMarker(out bool unreadable)
    {
        var read = _marker.Read();
        if (read.Unreadable)
        {
            // Do not fall back to the legacy key and then write over the file that could not be read.
            unreadable = true;
            return [];
        }

        unreadable = false;
        var stored = read.Values ?? _settings.Current.NativeSnapPreviousValues;
        var known = new Dictionary<NativeSnapFlag, bool>();
        if (stored is null)
        {
            return known;
        }

        foreach (var (name, value) in stored)
        {
            if (Enum.TryParse<NativeSnapFlag>(name, ignoreCase: true, out var flag) && Enum.IsDefined(flag))
            {
                known[flag] = value;
            }
            else
            {
                _logger.LogWarning("ignoring unknown flag {Flag} in the crash marker", name);
            }
        }

        return known;
    }

    /// <summary>
    /// The marker used to live in settings.json. Clear that copy only once the new store holds it:
    /// clearing first would leave an unclean exit in between with the flags off and nothing parked
    /// anywhere. Guarded so an ordinary launch neither writes settings.json nor raises Changed.
    /// </summary>
    private void ClearLegacyMarker()
    {
        if (_settings.Current.NativeSnapPreviousValues is not null)
        {
            _settings.Update(s => s.NativeSnapPreviousValues = null);
        }
    }

    /// <summary>The marker is JSON keyed by flag name; the enum stops at that boundary.</summary>
    private static Dictionary<string, bool> Park(IReadOnlyDictionary<NativeSnapFlag, bool> values) =>
        values.ToDictionary(entry => entry.Key.ToString(), entry => entry.Value);
}
