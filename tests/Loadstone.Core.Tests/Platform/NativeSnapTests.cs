using Loadstone.Core.Platform;
using Loadstone.Core.Tests.Settings;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using Xunit;

namespace Loadstone.Core.Tests.Platform;

public class NativeSnapTests
{
    private sealed class FakeNativeSnapSettings : INativeSnapSettings
    {
        public Dictionary<string, bool> Values { get; } = new() { ["DockMoving"] = true, ["SnapSizing"] = true };
        public HashSet<string> Unreadable { get; } = [];
        public HashSet<string> Unwritable { get; } = [];
        public List<(string Flag, bool Value)> Writes { get; } = [];

        public bool? Read(string flag) => Unreadable.Contains(flag) ? null : Values.GetValueOrDefault(flag);

        public bool Write(string flag, bool value)
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

    private readonly FakeNativeSnapSettings _native = new();
    private readonly InMemorySettings _settings = new();

    private NativeSnap NewNativeSnap() => new(_native, _settings, NullLogger<NativeSnap>.Instance);

    [Fact]
    public void Apply_On_CapturesThenTurnsBothFlagsOff_AndParksTheMarker()
    {
        var snap = NewNativeSnap();
        snap.Apply(dragSnappingEnabled: true);

        _native.Values["DockMoving"].ShouldBeFalse();
        _native.Values["SnapSizing"].ShouldBeFalse();
        _settings.Current.NativeSnapPreviousValues.ShouldBe(new Dictionary<string, bool> { ["DockMoving"] = true, ["SnapSizing"] = true });
        snap.IsSuppressing.ShouldBeTrue();
    }

    [Fact]
    public void Apply_On_RemembersWhatTheUserHad_NotAlwaysTrue()
    {
        _native.Values["SnapSizing"] = false;
        NewNativeSnap().Apply(dragSnappingEnabled: true);
        _settings.Current.NativeSnapPreviousValues!["SnapSizing"].ShouldBeFalse();
    }

    [Fact]
    public void Restore_PutsTheCapturedValuesBack_AndClearsTheMarker()
    {
        _native.Values["SnapSizing"] = false;
        var snap = NewNativeSnap();
        snap.Apply(dragSnappingEnabled: true);
        snap.Restore();

        _native.Values["DockMoving"].ShouldBeTrue();
        _native.Values["SnapSizing"].ShouldBeFalse();
        _settings.Current.NativeSnapPreviousValues.ShouldBeNull();
        snap.IsSuppressing.ShouldBeFalse();
    }

    [Fact]
    public void Apply_Off_IsRestore()
    {
        var snap = NewNativeSnap();
        snap.Apply(dragSnappingEnabled: true);
        snap.Apply(dragSnappingEnabled: false);
        _native.Values["DockMoving"].ShouldBeTrue();
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
        _settings.Current.NativeSnapPreviousValues!["DockMoving"].ShouldBeTrue();
    }

    [Fact]
    public void RestoreThenApply_CapturesTheValuesCurrentAtThatTime()
    {
        var snap = NewNativeSnap();
        snap.Apply(dragSnappingEnabled: true);
        snap.Restore();
        _native.Values["DockMoving"] = false; // the user changed it in between
        snap.Apply(dragSnappingEnabled: true);
        _settings.Current.NativeSnapPreviousValues!["DockMoving"].ShouldBeFalse();
    }

    [Fact]
    public void Apply_On_AfterUncleanExit_RestoresTheMarkerBeforeCapturing()
    {
        // A previous run left both flags off and its marker behind.
        _native.Values["DockMoving"] = false;
        _native.Values["SnapSizing"] = false;
        _settings.Current.NativeSnapPreviousValues = new Dictionary<string, bool> { ["DockMoving"] = true, ["SnapSizing"] = true };

        var snap = NewNativeSnap();
        snap.Apply(dragSnappingEnabled: true);

        // Captured the recovered values, not the leftover false ones, so a later restore is correct.
        _settings.Current.NativeSnapPreviousValues.ShouldBe(new Dictionary<string, bool> { ["DockMoving"] = true, ["SnapSizing"] = true });
        snap.Restore();
        _native.Values["DockMoving"].ShouldBeTrue();
        _native.Values["SnapSizing"].ShouldBeTrue();
    }

    [Fact]
    public void Apply_On_UnreadableFlag_IsLeftAloneAndNeverWritten()
    {
        _native.Unreadable.Add("SnapSizing");
        var snap = NewNativeSnap();
        snap.Apply(dragSnappingEnabled: true);
        snap.Restore();

        _native.Writes.ShouldAllBe(w => w.Flag == "DockMoving");
        _settings.Current.NativeSnapPreviousValues.ShouldBeNull();
    }

    [Fact]
    public void Apply_On_UnwritableFlag_DoesNotThrow()
    {
        _native.Unwritable.Add("DockMoving");
        Should.NotThrow(() => NewNativeSnap().Apply(dragSnappingEnabled: true));
    }

    [Fact]
    public void Apply_On_AfterUncleanExit_WhenARecoveryWriteFails_KeepsTheStoredValueAsBaseline()
    {
        // Marker with what the user had
        _settings.Current.NativeSnapPreviousValues = new Dictionary<string, bool> { ["DockMoving"] = true, ["SnapSizing"] = true };
        // Previous run left both flags off
        _native.Values["DockMoving"] = false;
        _native.Values["SnapSizing"] = false;
        // DockMoving write will fail during recovery
        _native.Unwritable.Add("DockMoving");

        var snap = NewNativeSnap();
        snap.Apply(dragSnappingEnabled: true);

        // The marker should have both values even though DockMoving write failed
        _settings.Current.NativeSnapPreviousValues.ShouldBe(new Dictionary<string, bool> { ["DockMoving"] = true, ["SnapSizing"] = true });

        // Now let DockMoving be written
        _native.Unwritable.Remove("DockMoving");
        snap.Restore();

        // Both should be restored to true
        _native.Values["DockMoving"].ShouldBeTrue();
        _native.Values["SnapSizing"].ShouldBeTrue();
    }

    [Fact]
    public void ManagedFlags_AreDockMovingAndSnapSizing()
    {
        NativeSnap.ManagedFlags.ShouldBe(["DockMoving", "SnapSizing"]);
    }
}
