using Loadstone.Core.Platform;
using Loadstone.Core.Tests.Settings;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using Xunit;

namespace Loadstone.Core.Tests.Platform;

public class NativeSnapTests
{
    private sealed class FakeNativeSnapFlags : INativeSnapFlags
    {
        public Dictionary<NativeSnapFlag, bool> Values { get; } = new()
        {
            [NativeSnapFlag.DockMoving] = true,
            [NativeSnapFlag.SnapSizing] = true,
        };

        public HashSet<NativeSnapFlag> Unreadable { get; } = [];
        public HashSet<NativeSnapFlag> Unwritable { get; } = [];
        public List<(NativeSnapFlag Flag, bool Value)> Writes { get; } = [];

        public bool? Read(NativeSnapFlag flag) => Unreadable.Contains(flag) ? null : Values.GetValueOrDefault(flag);

        public bool Write(NativeSnapFlag flag, bool value)
        {
            Writes.Add((flag, value));
            if (Unwritable.Contains(flag))
            {
                return false;
            }

            Values[flag] = value;
            return true;
        }
    }

    /// <summary>The crash marker's own store, keyed by flag name because that is the JSON boundary.</summary>
    private sealed class FakeNativeSnapMarker : INativeSnapMarker
    {
        /// <summary>What is parked, settable so a test can stand in for a previous run's leftovers.</summary>
        public Dictionary<string, bool>? Values { get; set; }

        /// <summary>What Write reports. Set false to stand in for a write that never reached the store.</summary>
        public bool WriteSucceeds { get; set; } = true;

        public IReadOnlyDictionary<string, bool>? Read() => Values;

        public bool Write(IReadOnlyDictionary<string, bool>? values)
        {
            if (!WriteSucceeds)
            {
                return false;
            }

            Values = values is null ? null : new Dictionary<string, bool>(values);
            return true;
        }
    }

    private readonly FakeNativeSnapFlags _native = new();
    private readonly FakeNativeSnapMarker _marker = new();
    private readonly InMemorySettings _settings = new();

    private NativeSnap NewNativeSnap() => new(_native, _marker, _settings, NullLogger<NativeSnap>.Instance);

    [Fact]
    public void Apply_On_CapturesThenTurnsBothFlagsOff_AndParksTheMarker()
    {
        var snap = NewNativeSnap();
        snap.Apply(dragSnappingEnabled: true);

        _native.Values[NativeSnapFlag.DockMoving].ShouldBeFalse();
        _native.Values[NativeSnapFlag.SnapSizing].ShouldBeFalse();
        _marker.Values.ShouldBe(new Dictionary<string, bool> { ["DockMoving"] = true, ["SnapSizing"] = true });
        snap.IsSuppressing.ShouldBeTrue();
    }

    [Fact]
    public void Apply_On_RemembersWhatTheUserHad_NotAlwaysTrue()
    {
        _native.Values[NativeSnapFlag.SnapSizing] = false;
        NewNativeSnap().Apply(dragSnappingEnabled: true);
        _marker.Values!["SnapSizing"].ShouldBeFalse();
    }

    [Fact]
    public void Restore_PutsTheCapturedValuesBack_AndClearsTheMarker()
    {
        _native.Values[NativeSnapFlag.SnapSizing] = false;
        var snap = NewNativeSnap();
        snap.Apply(dragSnappingEnabled: true);
        snap.Restore();

        _native.Values[NativeSnapFlag.DockMoving].ShouldBeTrue();
        _native.Values[NativeSnapFlag.SnapSizing].ShouldBeFalse();
        _marker.Values.ShouldBeNull();
        snap.IsSuppressing.ShouldBeFalse();
    }

    [Fact]
    public void Apply_Off_IsRestore()
    {
        var snap = NewNativeSnap();
        snap.Apply(dragSnappingEnabled: true);
        snap.Apply(dragSnappingEnabled: false);
        _native.Values[NativeSnapFlag.DockMoving].ShouldBeTrue();
        snap.IsSuppressing.ShouldBeFalse();
    }

    [Fact]
    public void Restore_WithoutSuppression_DoesNothing()
    {
        NewNativeSnap().Restore();
        _native.Writes.ShouldBeEmpty();
    }

    [Fact]
    public void Apply_On_Twice_CapturesOnlyOnce()
    {
        var snap = NewNativeSnap();
        snap.Apply(dragSnappingEnabled: true);
        snap.Apply(dragSnappingEnabled: true);
        _marker.Values!["DockMoving"].ShouldBeTrue();
    }

    [Fact]
    public void RestoreThenApply_CapturesTheValuesCurrentAtThatTime()
    {
        var snap = NewNativeSnap();
        snap.Apply(dragSnappingEnabled: true);
        snap.Restore();
        _native.Values[NativeSnapFlag.DockMoving] = false; // the user changed it in between
        snap.Apply(dragSnappingEnabled: true);
        _marker.Values!["DockMoving"].ShouldBeFalse();
    }

    [Fact]
    public void Apply_On_AfterUncleanExit_RestoresTheMarkerBeforeCapturing()
    {
        // A previous run left both flags off and its marker behind.
        _native.Values[NativeSnapFlag.DockMoving] = false;
        _native.Values[NativeSnapFlag.SnapSizing] = false;
        _marker.Values = new Dictionary<string, bool> { ["DockMoving"] = true, ["SnapSizing"] = true };

        var snap = NewNativeSnap();
        snap.Apply(dragSnappingEnabled: true);

        // Captured the recovered values, not the leftover false ones, so a later restore is correct.
        _marker.Values.ShouldBe(new Dictionary<string, bool> { ["DockMoving"] = true, ["SnapSizing"] = true });
        snap.Restore();
        _native.Values[NativeSnapFlag.DockMoving].ShouldBeTrue();
        _native.Values[NativeSnapFlag.SnapSizing].ShouldBeTrue();
    }

    [Fact]
    public void Apply_On_UnreadableFlag_IsLeftAloneAndNeverWritten()
    {
        _native.Unreadable.Add(NativeSnapFlag.SnapSizing);
        var snap = NewNativeSnap();
        snap.Apply(dragSnappingEnabled: true);
        snap.Restore();

        _native.Writes.ShouldAllBe(w => w.Flag == NativeSnapFlag.DockMoving);
        _marker.Values.ShouldBeNull();
    }

    [Fact]
    public void Apply_On_UnwritableFlag_DoesNotThrow()
    {
        _native.Unwritable.Add(NativeSnapFlag.DockMoving);
        Should.NotThrow(() => NewNativeSnap().Apply(dragSnappingEnabled: true));
    }

    [Fact]
    public void Apply_On_AfterUncleanExit_WhenARecoveryWriteFails_KeepsTheStoredValueAsBaseline()
    {
        // Marker with what the user had
        _marker.Values = new Dictionary<string, bool> { ["DockMoving"] = true, ["SnapSizing"] = true };
        // Previous run left both flags off
        _native.Values[NativeSnapFlag.DockMoving] = false;
        _native.Values[NativeSnapFlag.SnapSizing] = false;
        // DockMoving write will fail during recovery
        _native.Unwritable.Add(NativeSnapFlag.DockMoving);

        var snap = NewNativeSnap();
        snap.Apply(dragSnappingEnabled: true);

        // The marker should have both values even though DockMoving write failed
        _marker.Values.ShouldBe(new Dictionary<string, bool> { ["DockMoving"] = true, ["SnapSizing"] = true });

        // Now let DockMoving be written
        _native.Unwritable.Remove(NativeSnapFlag.DockMoving);
        snap.Restore();

        // Both should be restored to true
        _native.Values[NativeSnapFlag.DockMoving].ShouldBeTrue();
        _native.Values[NativeSnapFlag.SnapSizing].ShouldBeTrue();
    }

    [Fact]
    public void Apply_On_AfterUncleanExit_LeavesAlone_AFlagTurnedOnBetweenTheRuns()
    {
        // The symptom: the previous run parked SnapSizing:false because that is what the user had,
        // was force-quit, and the user turned SnapSizing on in Windows Settings before the relaunch.
        // Recovery wrote the parked false straight back over that, the new baseline then read the
        // false it had just written, and parking that made the reversal permanent and re-applied at
        // every later launch. A parked flag reading true cannot be Loadstone's doing: the run that
        // parked it wrote false and never put it back.
        _marker.Values = new Dictionary<string, bool> { ["DockMoving"] = false, ["SnapSizing"] = false };
        _native.Values[NativeSnapFlag.DockMoving] = false; // still the false that run wrote
        _native.Values[NativeSnapFlag.SnapSizing] = true;  // turned on in Windows Settings since

        var snap = NewNativeSnap();
        snap.Apply(dragSnappingEnabled: true);

        // The user's choice, not the parked false, becomes the baseline.
        _marker.Values.ShouldBe(new Dictionary<string, bool> { ["DockMoving"] = false, ["SnapSizing"] = true });

        snap.Restore();

        _native.Values[NativeSnapFlag.SnapSizing].ShouldBeTrue();
        _native.Values[NativeSnapFlag.DockMoving].ShouldBeFalse();
    }

    [Fact]
    public void Restore_WhenAWriteFails_KeepsThatFlagParked_InsteadOfClearingTheMarker()
    {
        // The symptom: a restore whose write failed cleared the marker anyway, so the flag stayed
        // off with nothing left recording what the user had. Windows' edge snapping was gone and no
        // restart could work out what to put back. The recovery side is pinned by
        // Apply_On_AfterUncleanExit_WhenARecoveryWriteFails_KeepsTheStoredValueAsBaseline; the two
        // paths disagreeing was the bug.
        var snap = NewNativeSnap();
        snap.Apply(dragSnappingEnabled: true);
        _native.Unwritable.Add(NativeSnapFlag.DockMoving);

        snap.Restore();

        _marker.Values.ShouldBe(new Dictionary<string, bool> { ["DockMoving"] = true });
        _native.Values[NativeSnapFlag.SnapSizing].ShouldBeTrue();
        // Still off, so still suppressing: the next Restore has to try it again.
        snap.IsSuppressing.ShouldBeTrue();
    }

    [Fact]
    public void Restore_WhenAWriteFails_TheNextRunStillPutsTheUsersValueBack()
    {
        // The same symptom seen from the launch that has to clean it up: the parked value is the only
        // record of what the user had, so the next run must recover from it rather than read the
        // false Loadstone left behind and record that as the new baseline.
        var first = NewNativeSnap();
        first.Apply(dragSnappingEnabled: true);
        _native.Unwritable.Add(NativeSnapFlag.DockMoving);
        first.Restore();

        _native.Unwritable.Remove(NativeSnapFlag.DockMoving);
        var second = NewNativeSnap();
        second.Apply(dragSnappingEnabled: true);

        _marker.Values.ShouldBe(new Dictionary<string, bool> { ["DockMoving"] = true, ["SnapSizing"] = true });

        second.Restore();

        _native.Values[NativeSnapFlag.DockMoving].ShouldBeTrue();
        _native.Values[NativeSnapFlag.SnapSizing].ShouldBeTrue();
        _marker.Values.ShouldBeNull();
    }

    [Fact]
    public void Restore_AfterAPartialRestore_RetriesOnlyTheFlagThatIsStillOff()
    {
        var snap = NewNativeSnap();
        snap.Apply(dragSnappingEnabled: true);
        _native.Unwritable.Add(NativeSnapFlag.DockMoving);
        snap.Restore();

        _native.Unwritable.Remove(NativeSnapFlag.DockMoving);
        var writes = _native.Writes.Count;
        snap.Restore();

        // SnapSizing came back on the first attempt; only the flag still holding Loadstone's false is
        // written again, and putting it back is what finally clears the marker.
        _native.Writes.Skip(writes).ShouldAllBe(w => w.Flag == NativeSnapFlag.DockMoving);
        _native.Values[NativeSnapFlag.DockMoving].ShouldBeTrue();
        _marker.Values.ShouldBeNull();
        snap.IsSuppressing.ShouldBeFalse();
    }

    [Fact]
    public void Apply_On_AfterAPartialRestore_TurnsEveryFlagOffAgain_AndReParksTheWholeBaseline()
    {
        // The symptom: a restore whose write failed narrowed the captured set to the failures, so the
        // next suppress wrote only those flags off again. The one the user had turned back on stayed
        // on alongside Loadstone's drag snapping - the both-active state this class exists to prevent
        // - and nothing said so, because the "it will fight drag snapping" error only ever covers the
        // captured flags. The baseline is what the user had, which a failed write does not change.
        var snap = NewNativeSnap();
        snap.Apply(dragSnappingEnabled: true);

        _native.Values[NativeSnapFlag.SnapSizing] = true; // turned back on in Windows Settings mid-run
        _native.Unwritable.Add(NativeSnapFlag.DockMoving);
        snap.Apply(dragSnappingEnabled: false); // SnapSizing left alone, DockMoving fails to restore
        _native.Unwritable.Remove(NativeSnapFlag.DockMoving);

        snap.Apply(dragSnappingEnabled: true);

        _native.Values[NativeSnapFlag.DockMoving].ShouldBeFalse();
        _native.Values[NativeSnapFlag.SnapSizing].ShouldBeFalse();
        _marker.Values.ShouldBe(new Dictionary<string, bool> { ["DockMoving"] = true, ["SnapSizing"] = true });
        snap.IsSuppressing.ShouldBeTrue();

        snap.Restore();

        // And the whole baseline comes back, not just the flag the failed restore had left off.
        _native.Values[NativeSnapFlag.DockMoving].ShouldBeTrue();
        _native.Values[NativeSnapFlag.SnapSizing].ShouldBeTrue();
        _marker.Values.ShouldBeNull();
        snap.IsSuppressing.ShouldBeFalse();
    }

    [Fact]
    public void Apply_On_AfterAPartialRestore_WhenTheWholeBaselineCannotBeReParked_LeavesTheFlagsAlone()
    {
        var snap = NewNativeSnap();
        snap.Apply(dragSnappingEnabled: true);

        _native.Values[NativeSnapFlag.SnapSizing] = true;
        _native.Unwritable.Add(NativeSnapFlag.DockMoving);
        snap.Apply(dragSnappingEnabled: false);
        _native.Unwritable.Remove(NativeSnapFlag.DockMoving);

        _marker.WriteSucceeds = false;
        var writes = _native.Writes.Count;
        snap.Apply(dragSnappingEnabled: true);

        // Turning SnapSizing off while the marker still holds only DockMoving would leave a
        // force-quit with nothing recording that the user had SnapSizing on, and that loss is the
        // permanent one. Losing the suppression for this run is the cheap failure.
        _native.Writes.Count.ShouldBe(writes);
        _native.Values[NativeSnapFlag.SnapSizing].ShouldBeTrue();
        _marker.Values.ShouldBe(new Dictionary<string, bool> { ["DockMoving"] = true });
    }

    [Fact]
    public void Restore_WhenTheNarrowedMarkerCannotBeParked_KeepsTheWiderOne_AndStillClearsItLater()
    {
        var snap = NewNativeSnap();
        snap.Apply(dragSnappingEnabled: true);

        _native.Unwritable.Add(NativeSnapFlag.DockMoving);
        _marker.WriteSucceeds = false;
        snap.Restore();

        // The whole baseline is still parked, which is safe - SnapSizing is already back at the value
        // in it - so the retry has to cover the whole of it rather than only the flag that failed.
        _marker.Values.ShouldBe(new Dictionary<string, bool> { ["DockMoving"] = true, ["SnapSizing"] = true });
        snap.IsSuppressing.ShouldBeTrue();

        _native.Unwritable.Remove(NativeSnapFlag.DockMoving);
        _marker.WriteSucceeds = true;
        snap.Restore();

        _native.Values[NativeSnapFlag.DockMoving].ShouldBeTrue();
        _marker.Values.ShouldBeNull();
        snap.IsSuppressing.ShouldBeFalse();
    }

    [Fact]
    public void Restore_WhenTheMarkerCannotBeCleared_StillCountsAsRestored()
    {
        var snap = NewNativeSnap();
        snap.Apply(dragSnappingEnabled: true);
        _marker.WriteSucceeds = false;

        snap.Restore();

        // Every flag is back at the value the stale marker holds for it, so nothing is suppressed any
        // more and the next launch's recovery only writes back what is already there.
        _native.Values[NativeSnapFlag.DockMoving].ShouldBeTrue();
        _native.Values[NativeSnapFlag.SnapSizing].ShouldBeTrue();
        _marker.Values.ShouldBe(new Dictionary<string, bool> { ["DockMoving"] = true, ["SnapSizing"] = true });
        snap.IsSuppressing.ShouldBeFalse();
    }

    [Fact]
    public void Apply_On_WhenTheMarkerCannotBeParked_LeavesWindowsEdgeSnappingAlone()
    {
        // The symptom: turning the flags off with no marker parked meant a force-quit left Windows'
        // edge snapping off for good, because the next launch read Loadstone's own false and recorded
        // it as what the user had. Losing our suppression for one run is the cheap failure.
        _marker.WriteSucceeds = false;

        var snap = NewNativeSnap();
        snap.Apply(dragSnappingEnabled: true);

        // Nothing reached the flags at all, so in particular neither was turned off.
        _native.Writes.ShouldBeEmpty();
        _native.Values[NativeSnapFlag.DockMoving].ShouldBeTrue();
        _native.Values[NativeSnapFlag.SnapSizing].ShouldBeTrue();
        snap.IsSuppressing.ShouldBeFalse();
    }

    [Fact]
    public void Apply_On_WithTheMarkerAnOldBuildLeftInSettings_HonoursItThenClearsIt()
    {
        // An upgraded profile: the marker used to live in settings.json, and a build that ignored the
        // old home left the user's edge snapping off across the upgrade.
        _native.Values[NativeSnapFlag.DockMoving] = false;
        _native.Values[NativeSnapFlag.SnapSizing] = false;
        _settings.Current.NativeSnapPreviousValues = new Dictionary<string, bool> { ["DockMoving"] = true, ["SnapSizing"] = true };

        var snap = NewNativeSnap();
        snap.Apply(dragSnappingEnabled: true);

        // Honoured: recovered before the new baseline was captured, and parked in the new store.
        _marker.Values.ShouldBe(new Dictionary<string, bool> { ["DockMoving"] = true, ["SnapSizing"] = true });
        // Read once: the old home is cleared, in a single settings write, so no later launch sees it.
        _settings.Current.NativeSnapPreviousValues.ShouldBeNull();
        _settings.UpdateCount.ShouldBe(1);
    }

    [Fact]
    public void Apply_On_WithoutALegacyMarker_NeverWritesSettings()
    {
        var snap = NewNativeSnap();
        snap.Apply(dragSnappingEnabled: true);
        snap.Restore();

        // An ordinary launch must not touch settings.json at all: every write there raises Changed
        // and re-enters every subscriber.
        _settings.UpdateCount.ShouldBe(0);
    }

    [Fact]
    public void Apply_On_WithAnUnknownFlagInTheMarker_DropsIt_InsteadOfReParkingItForever()
    {
        // The symptom: a name this build does not know could not be written back, so recovery failed
        // for it, the failure carried it into the new baseline, and the baseline was parked again -
        // an error logged at every launch for the rest of the profile's life.
        _marker.Values = new Dictionary<string, bool> { ["DockMoving"] = true, ["SnapLayouts"] = true };
        _native.Values[NativeSnapFlag.DockMoving] = false;

        NewNativeSnap().Apply(dragSnappingEnabled: true);

        _marker.Values.ShouldBe(new Dictionary<string, bool> { ["DockMoving"] = true, ["SnapSizing"] = true });
        _native.Writes.ShouldAllBe(w => w.Flag == NativeSnapFlag.DockMoving || w.Flag == NativeSnapFlag.SnapSizing);
    }

    [Fact]
    public void Restore_LeavesAlone_AFlagTheUserTurnedBackOnDuringTheRun()
    {
        // The symptom: the user turned Windows' edge snapping back on in Settings while Loadstone was
        // running, and quitting Loadstone turned it off again - writing a captured false over a
        // choice the user had made since.
        _native.Values[NativeSnapFlag.SnapSizing] = false;
        var snap = NewNativeSnap();
        snap.Apply(dragSnappingEnabled: true);

        _native.Values[NativeSnapFlag.SnapSizing] = true; // changed in Windows Settings mid-run
        var writes = _native.Writes.Count;

        snap.Restore();

        _native.Values[NativeSnapFlag.SnapSizing].ShouldBeTrue();
        _native.Writes.Skip(writes).ShouldAllBe(w => w.Flag == NativeSnapFlag.DockMoving);
        _native.Values[NativeSnapFlag.DockMoving].ShouldBeTrue();
        _marker.Values.ShouldBeNull();
        snap.IsSuppressing.ShouldBeFalse();
    }

    [Fact]
    public void Follow_AppliesTheSettingStraightAway()
    {
        var snap = NewNativeSnap();
        snap.Follow();

        _native.Values[NativeSnapFlag.DockMoving].ShouldBeFalse();
        _native.Values[NativeSnapFlag.SnapSizing].ShouldBeFalse();
        snap.IsSuppressing.ShouldBeTrue();
    }

    [Fact]
    public void Follow_WithDragSnappingOff_LeavesWindowsEdgeSnappingAlone()
    {
        _settings.Current.DragSnappingEnabled = false;
        var snap = NewNativeSnap();
        snap.Follow();

        _native.Writes.ShouldBeEmpty();
        _native.Values[NativeSnapFlag.DockMoving].ShouldBeTrue();
        _marker.Values.ShouldBeNull();
        snap.IsSuppressing.ShouldBeFalse();
    }

    [Fact]
    public void Follow_Restore_DoesNotReSuppress()
    {
        // The regression. The marker used to be parked through the same settings service, and every
        // write there raises Changed. A follower that re-applied on any change captured the values
        // Restore had just put back and turned both flags off again, so the user lost Windows' edge
        // snapping on every quit and after every crash. The marker has its own store now, but the
        // guard still has to hold - see the legacy-marker test below, which still raises Changed.
        var snap = NewNativeSnap();
        snap.Follow();

        snap.Restore();

        _native.Values[NativeSnapFlag.DockMoving].ShouldBeTrue();
        _native.Values[NativeSnapFlag.SnapSizing].ShouldBeTrue();
        _marker.Values.ShouldBeNull();
        snap.IsSuppressing.ShouldBeFalse();
    }

    [Fact]
    public void Follow_WhenClearingTheLegacyMarkerRaisesChanged_DoesNotReApply()
    {
        // The same regression on the one path that still raises Changed from inside a capture. The
        // toggle has to flip while following for it to bite: Follow subscribes after its first Apply,
        // so only a later turn-on clears the marker's old home with the handler attached.
        _settings.Current.DragSnappingEnabled = false;
        _settings.Current.NativeSnapPreviousValues = new Dictionary<string, bool> { ["DockMoving"] = true, ["SnapSizing"] = true };
        _native.Values[NativeSnapFlag.DockMoving] = false;
        _native.Values[NativeSnapFlag.SnapSizing] = false;

        var snap = NewNativeSnap();
        snap.Follow(); // subscribed, nothing captured yet

        _settings.Update(s => s.DragSnappingEnabled = true);

        // Two writes to recover the flags, two to turn them off. The re-entrant Changed must not add
        // a third pair, and the marker must hold the recovered values rather than Loadstone's false.
        _native.Writes.Count.ShouldBe(4);
        _marker.Values.ShouldBe(new Dictionary<string, bool> { ["DockMoving"] = true, ["SnapSizing"] = true });
        _settings.Current.NativeSnapPreviousValues.ShouldBeNull();
        _native.Values[NativeSnapFlag.DockMoving].ShouldBeFalse();
        _native.Values[NativeSnapFlag.SnapSizing].ShouldBeFalse();
        snap.IsSuppressing.ShouldBeTrue();
    }

    [Fact]
    public void Follow_AppliesAgain_WhenTheSettingActuallyFlips()
    {
        var snap = NewNativeSnap();
        snap.Follow();
        snap.IsSuppressing.ShouldBeTrue();

        _settings.Update(s => s.DragSnappingEnabled = false);

        _native.Values[NativeSnapFlag.DockMoving].ShouldBeTrue();
        _native.Values[NativeSnapFlag.SnapSizing].ShouldBeTrue();
        _marker.Values.ShouldBeNull();
        snap.IsSuppressing.ShouldBeFalse();

        _settings.Update(s => s.DragSnappingEnabled = true);

        _native.Values[NativeSnapFlag.DockMoving].ShouldBeFalse();
        _native.Values[NativeSnapFlag.SnapSizing].ShouldBeFalse();
        snap.IsSuppressing.ShouldBeTrue();
    }

    [Fact]
    public void Follow_IgnoresSettingsChanges_ThatAreNotTheDragSnappingToggle()
    {
        var snap = NewNativeSnap();
        snap.Follow();
        var writes = _native.Writes.Count;

        _settings.Update(s => s.Shortcuts["leftHalf"] = "Ctrl+Alt+Shift+Left");

        _native.Writes.Count.ShouldBe(writes);
        _marker.Values.ShouldBe(new Dictionary<string, bool> { ["DockMoving"] = true, ["SnapSizing"] = true });
        snap.IsSuppressing.ShouldBeTrue();
    }

    [Fact]
    public void Follow_Twice_AppliesAndSubscribesOnlyOnce()
    {
        var snap = NewNativeSnap();
        snap.Follow();
        var writes = _native.Writes.Count;
        var updates = _settings.UpdateCount;

        snap.Follow();

        _native.Writes.Count.ShouldBe(writes);
        _settings.UpdateCount.ShouldBe(updates);
    }

    [Fact]
    public void StopFollowing_LeavesTheSettingUnwatched()
    {
        var snap = NewNativeSnap();
        snap.Follow();
        snap.StopFollowing();

        _settings.Update(s => s.DragSnappingEnabled = false);

        _native.Values[NativeSnapFlag.DockMoving].ShouldBeFalse();
        snap.IsSuppressing.ShouldBeTrue();
    }

    [Fact]
    public void StopFollowing_WithoutFollowing_DoesNothing()
    {
        Should.NotThrow(() => NewNativeSnap().StopFollowing());
    }

    [Fact]
    public void ManagedFlags_AreDockMovingAndSnapSizing()
    {
        NativeSnap.ManagedFlags.ShouldBe([NativeSnapFlag.DockMoving, NativeSnapFlag.SnapSizing]);
    }
}
