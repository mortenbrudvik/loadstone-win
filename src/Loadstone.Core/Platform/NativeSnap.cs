using Loadstone.Core.Settings;
using Microsoft.Extensions.Logging;

namespace Loadstone.Core.Platform;

/// <summary>
/// Windows snaps a window itself when you drag it to an edge, which fights Loadstone's own
/// snapping. While drag snapping is on this turns the dock-moving and snap-sizing flags off,
/// remembers what the user had, and puts it back on restore. The capture is parked in settings.json
/// for the length of the run, so a crash or force-quit is detected at the next launch and undone
/// before a new baseline is captured.
/// </summary>
public sealed class NativeSnap
{
    public const string DockMoving = "DockMoving";
    public const string SnapSizing = "SnapSizing";

    public static IReadOnlyList<string> ManagedFlags { get; } = [DockMoving, SnapSizing];

    private readonly INativeSnapSettings _native;
    private readonly ISettingsService _settings;
    private readonly ILogger<NativeSnap> _logger;

    /// <summary>The user's values before Loadstone touched them; null while not suppressing.</summary>
    private Dictionary<string, bool>? _captured;

    /// <summary>Whether <see cref="Follow"/> is subscribed, and the setting value it last acted on.</summary>
    private bool _following;
    private bool _dragSnapping;

    public NativeSnap(INativeSnapSettings native, ISettingsService settings, ILogger<NativeSnap> logger)
    {
        _native = native;
        _settings = settings;
        _logger = logger;
    }

    public bool IsSuppressing => _captured is not null;

    /// <summary>
    /// Applies the drag-snapping setting now and follows it from then on. Safe to call more than once.
    /// </summary>
    /// <remarks>
    /// The follow re-applies only when the setting actually flips. Suppress and Restore both write
    /// this class's own marker through the same settings service, and every write raises
    /// <see cref="ISettingsService.Changed"/>; reacting to those would re-enter <see cref="Apply"/>
    /// and re-suppress the flags <see cref="Restore"/> had just put back, on exit and while
    /// crashing alike.
    /// </remarks>
    public void Follow()
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

    /// <summary>Stops following the setting. Safe to call when not following.</summary>
    public void StopFollowing()
    {
        if (!_following)
        {
            return;
        }

        _following = false;
        _settings.Changed -= OnSettingsChanged;
    }

    /// <summary>Suppresses Windows' edge snapping while Loadstone's drag snapping is on, restores it while off.</summary>
    public void Apply(bool dragSnappingEnabled)
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

    /// <summary>Writes the captured values back and clears the marker. Safe to call when not suppressing.</summary>
    public void Restore()
    {
        if (_captured is null)
        {
            return;
        }

        foreach (var (flag, value) in _captured)
        {
            if (!_native.Write(flag, value))
            {
                _logger.LogError("could not restore Windows edge snapping flag {Flag} to {Value}", flag, value);
            }
        }

        _captured = null;
        _settings.Update(s => s.NativeSnapPreviousValues = null);
    }

    private void OnSettingsChanged(object? sender, EventArgs e)
    {
        var wanted = _settings.Current.DragSnappingEnabled;
        if (wanted == _dragSnapping)
        {
            return;
        }

        // Assign before applying, never after: Apply writes the marker, which raises Changed again,
        // and that re-entrant call has to see the new value or the flags flip straight back.
        _dragSnapping = wanted;
        Apply(wanted);
    }

    private void Suppress()
    {
        if (_captured is null)
        {
            var failedRecovery = RecoverFromUncleanExit();

            var captured = new Dictionary<string, bool>();
            foreach (var flag in ManagedFlags)
            {
                if (failedRecovery.TryGetValue(flag, out var value))
                {
                    // Recovery write failed; use the marker value instead of reading
                    captured[flag] = value;
                }
                else if (_native.Read(flag) is { } readValue)
                {
                    captured[flag] = readValue;
                }
                else
                {
                    _logger.LogWarning("could not read Windows edge snapping flag {Flag}; leaving it alone", flag);
                }
            }

            _captured = captured;
            // Park the capture where it outlives the process.
            _settings.Update(s => s.NativeSnapPreviousValues = new Dictionary<string, bool>(captured));
        }

        foreach (var flag in _captured.Keys)
        {
            if (!_native.Write(flag, false) || _native.Read(flag) != false)
            {
                _logger.LogError("could not turn off Windows edge snapping flag {Flag}; it will fight drag snapping", flag);
            }
        }
    }

    /// <summary>
    /// A run that crashed or was force-quit never reached Restore, so its marker is still parked in
    /// settings and the user's flags are still off. Put them back before capturing a new baseline:
    /// capturing first would record Loadstone's own false as "what the user had" and make the
    /// change permanent.
    /// </summary>
    private Dictionary<string, bool> RecoverFromUncleanExit()
    {
        var stored = _settings.Current.NativeSnapPreviousValues;
        var failedRecovery = new Dictionary<string, bool>();

        if (stored is null)
        {
            return failedRecovery;
        }

        if (stored.Count > 0)
        {
            _logger.LogWarning("restoring Windows edge snapping flags left behind by an unclean exit");
            foreach (var (flag, value) in stored)
            {
                if (!_native.Write(flag, value))
                {
                    _logger.LogError("could not restore Windows edge snapping flag {Flag} to {Value} after an unclean exit; keeping it as the baseline", flag, value);
                    failedRecovery[flag] = value;
                }
            }
        }

        _settings.Update(s => s.NativeSnapPreviousValues = null);
        return failedRecovery;
    }
}
