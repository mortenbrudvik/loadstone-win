using Loadstone.Core.Settings;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using Xunit;

namespace Loadstone.Core.Tests.Settings;

public sealed class SettingsServiceTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "loadstone-tests", Guid.NewGuid().ToString("N"));
    private string SettingsPath => Path.Combine(_directory, "settings.json");

    private SettingsService NewService() => new(SettingsPath, NullLogger<SettingsService>.Instance);

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    [Fact]
    public void Load_MissingFile_GivesDefaults()
    {
        var service = NewService();
        service.Current.DragSnappingEnabled.ShouldBeFalse();
        service.Current.RestoreOneMoveAtATime.ShouldBeFalse();
        service.Current.Shortcuts.ShouldBeEmpty();
        service.Current.NativeSnapPreviousValues.ShouldBeNull();
        File.Exists(SettingsPath).ShouldBeFalse();
    }

    /// <summary>
    /// First run is "no file was there to load" and nothing else. A persisted flag would be a second
    /// source of truth for something the file's own existence already answers, and would have to be
    /// written before the user had consented to anything.
    /// </summary>
    [Fact]
    public void IsFirstRun_WithNoFileOnDisk_IsTrue()
    {
        NewService().IsFirstRun.ShouldBeTrue();
    }

    [Fact]
    public void IsFirstRun_OnceTheFileExists_IsFalse()
    {
        NewService().Update(s => s.DragSnappingEnabled = true).ShouldBeTrue();

        NewService().IsFirstRun.ShouldBeFalse();
    }

    /// <summary>
    /// Off until asked. Loadstone turns off a Windows setting when this is on, and Store policy
    /// requires consent before an app changes one — turning this on is that consent.
    /// </summary>
    [Fact]
    public void Load_MissingFile_LeavesDragSnappingOff()
    {
        NewService().Current.DragSnappingEnabled.ShouldBeFalse();
    }

    /// <summary>
    /// An older settings.json has no key for this. Missing has to mean off, or an upgrade would
    /// change Restore from the one-shot jump the user already has.
    /// </summary>
    [Fact]
    public void Load_FileWithoutTheRestoreKey_LeavesOneMoveAtATimeOff()
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(SettingsPath, "{ \"dragSnappingEnabled\": true }");

        var service = NewService();
        service.Current.DragSnappingEnabled.ShouldBeTrue();
        service.Current.RestoreOneMoveAtATime.ShouldBeFalse();
    }

    [Fact]
    public void Update_PersistsRestoreOneMoveAtATime()
    {
        var service = NewService();
        service.Update(s => s.RestoreOneMoveAtATime = true);

        File.ReadAllText(SettingsPath).ShouldContain("\"restoreOneMoveAtATime\": true");
        NewService().Current.RestoreOneMoveAtATime.ShouldBeTrue();
    }

    [Fact]
    public void Update_PersistsAndReloads()
    {
        var service = NewService();
        service.Update(s =>
        {
            s.DragSnappingEnabled = false;
            s.Shortcuts["leftHalf"] = "Ctrl+Alt+Shift+Left";
            s.Shortcuts["center"] = "";
            // Migration only: the crash marker has its own store now, but a settings.json written
            // by an older build still has to round-trip so NativeSnap can pick that marker up once.
            s.NativeSnapPreviousValues = new Dictionary<string, bool> { ["DockMoving"] = true, ["SnapSizing"] = false };
        });

        var reloaded = NewService();
        reloaded.Current.DragSnappingEnabled.ShouldBeFalse();
        reloaded.Current.Shortcuts.ShouldBe(new Dictionary<string, string> { ["leftHalf"] = "Ctrl+Alt+Shift+Left", ["center"] = "" });
        reloaded.Current.NativeSnapPreviousValues.ShouldBe(new Dictionary<string, bool> { ["DockMoving"] = true, ["SnapSizing"] = false });
    }

    [Fact]
    public void Update_WritesCamelCaseJson_AndOmitsTheMarkerWhenNull()
    {
        var service = NewService();
        service.Update(s => s.DragSnappingEnabled = false);
        var json = File.ReadAllText(SettingsPath);
        json.ShouldContain("\"dragSnappingEnabled\": false");
        json.ShouldContain("\"shortcuts\"");
        json.ShouldNotContain("nativeSnapPreviousValues");
    }

    [Fact]
    public void Update_LeavesNoTempFileBehind()
    {
        var service = NewService();
        service.Update(s => s.DragSnappingEnabled = false);
        Directory.GetFiles(_directory).ShouldBe([SettingsPath]);
    }

    [Fact]
    public void Update_RaisesChanged()
    {
        var service = NewService();
        var raised = 0;
        service.Changed += (_, _) => raised++;
        service.Update(_ => { });
        raised.ShouldBe(1);
    }

    [Fact]
    public void Update_WhenTheSaveReachesDisk_ReturnsTrue()
    {
        NewService().Update(s => s.DragSnappingEnabled = false).ShouldBeTrue();
    }

    /// <summary>
    /// One subscriber that threw used to abort the notification part-way: the handlers registered
    /// after it never heard about a change that had already been saved, so the tray menu and the
    /// hotkeys stayed on the old settings until the next restart. It also surfaced as a throw out
    /// of whatever flipped the toggle, which in a Settings binding is a silent no-op.
    /// </summary>
    [Fact]
    public void Update_WhenASubscriberThrows_StillNotifiesTheRest_AndDoesNotReachTheCaller()
    {
        var service = NewService();
        var before = 0;
        var after = 0;
        service.Changed += (_, _) => before++;
        service.Changed += (_, _) => throw new InvalidOperationException("a Changed subscriber blew up");
        service.Changed += (_, _) => after++;

        // A propagating exception fails the test here, before any assertion runs.
        var saved = service.Update(s => s.DragSnappingEnabled = false);

        before.ShouldBe(1);
        after.ShouldBe(1);
        saved.ShouldBeTrue();
        NewService().Current.DragSnappingEnabled.ShouldBeFalse();
    }

    [Fact]
    public void Load_CorruptFile_BacksItUpAndUsesDefaults()
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(SettingsPath, "{ this is not json");

        var service = NewService();
        service.Current.DragSnappingEnabled.ShouldBeFalse();
        File.ReadAllText(SettingsPath + ".corrupt").ShouldBe("{ this is not json");

        service.Update(s => s.DragSnappingEnabled = false);
        NewService().Current.DragSnappingEnabled.ShouldBeFalse();
    }

    /// <summary>
    /// Load runs in the constructor, so anything it throws is a crash before the tray icon exists:
    /// no window, no message, a process that never appeared. The awkward shape is a corrupt file
    /// whose backup also fails, because that failure happens inside the handler for the first one.
    /// A SecurityException from a locked-down profile is caught in the same arm as this copy.
    /// </summary>
    [Fact]
    public void Load_CorruptFile_WhenTheBackupCannotBeWritten_StillUsesDefaults()
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(SettingsPath, "{ this is not json");
        Directory.CreateDirectory(SettingsPath + ".corrupt"); // the copy target is a directory

        var service = NewService();

        service.Current.DragSnappingEnabled.ShouldBeFalse();
        service.Current.Shortcuts.ShouldBeEmpty();
    }

    [Fact]
    public void Load_UnknownProperties_AreIgnored()
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(SettingsPath, "{ \"dragSnappingEnabled\": false, \"futureSetting\": 42 }");
        NewService().Current.DragSnappingEnabled.ShouldBeFalse();
    }

    /// <summary>A hand-edited null would otherwise reach every reader of the dictionary.</summary>
    [Fact]
    public void Load_NullShortcuts_BecomeAnEmptyDictionary()
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(SettingsPath, "{ \"shortcuts\": null }");

        var service = NewService();

        service.Current.Shortcuts.ShouldNotBeNull();
        service.Current.Shortcuts.ShouldBeEmpty();
    }

    /// <summary>
    /// At startup an escaping read failure would be a crash before the tray icon exists, and Load
    /// runs in the constructor, so every read failure has to come back as defaults: the lock here
    /// (IOException), a denied file (UnauthorizedAccessException), and a SecurityException from a
    /// locked-down profile, which shares their catch arm but cannot be provoked in-process.
    /// </summary>
    [Fact]
    public void Load_UnreadableFile_UsesDefaults()
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(SettingsPath, "{ \"dragSnappingEnabled\": false }");
        using var exclusive = new FileStream(SettingsPath, FileMode.Open, FileAccess.Read, FileShare.None);

        var service = NewService();

        service.Current.DragSnappingEnabled.ShouldBeFalse();
        service.Current.Shortcuts.ShouldBeEmpty();
        service.CanSave.ShouldBeFalse();
    }

    [Fact]
    public void Update_WhenTheFileCouldNotBeRead_DoesNotOverwriteIt()
    {
        Directory.CreateDirectory(_directory);
        var original = "{ \"shortcuts\": { \"leftHalf\": \"Ctrl+Alt+H\" } }";
        File.WriteAllText(SettingsPath, original);
        using (var exclusive = new FileStream(SettingsPath, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            var service = NewService();
            service.CanSave.ShouldBeFalse();

            service.Update(s => s.DragSnappingEnabled = true).ShouldBeFalse();
            service.Current.DragSnappingEnabled.ShouldBeTrue();
        }

        File.ReadAllText(SettingsPath).ShouldBe(original);
    }

    /// <summary>
    /// A Settings toggle whose save fails still applies to this run; the binding engine would
    /// swallow the throw. The false return is the only way a caller whose next step is unsafe
    /// unless the change survives a force-quit can find out, so it is pinned here too.
    /// </summary>
    [Fact]
    public void Update_WhenTheFileCannotBeWritten_ReturnsFalse_ButKeepsTheChangeInMemory()
    {
        Directory.CreateDirectory(SettingsPath + ".tmp");
        var service = NewService();

        var saved = true;
        Should.NotThrow(() => { saved = service.Update(s => s.DragSnappingEnabled = false); });

        saved.ShouldBeFalse();
        service.Current.DragSnappingEnabled.ShouldBeFalse();
        File.Exists(SettingsPath).ShouldBeFalse();
    }
}
