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
        service.Current.DragSnappingEnabled.ShouldBeTrue();
        service.Current.Shortcuts.ShouldBeEmpty();
        service.Current.NativeSnapPreviousValues.ShouldBeNull();
        File.Exists(SettingsPath).ShouldBeFalse();
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
    public void Load_CorruptFile_BacksItUpAndUsesDefaults()
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(SettingsPath, "{ this is not json");

        var service = NewService();
        service.Current.DragSnappingEnabled.ShouldBeTrue();
        File.ReadAllText(SettingsPath + ".corrupt").ShouldBe("{ this is not json");

        service.Update(s => s.DragSnappingEnabled = false);
        NewService().Current.DragSnappingEnabled.ShouldBeFalse();
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

    /// <summary>At startup an escaping read failure would be a crash before the tray icon exists.</summary>
    [Fact]
    public void Load_UnreadableFile_UsesDefaults()
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(SettingsPath, "{ \"dragSnappingEnabled\": false }");
        using var exclusive = new FileStream(SettingsPath, FileMode.Open, FileAccess.Read, FileShare.None);

        var service = NewService();

        service.Current.DragSnappingEnabled.ShouldBeTrue();
        service.Current.Shortcuts.ShouldBeEmpty();
    }

    /// <summary>A Settings toggle whose save fails still applies to this run; the binding engine would swallow the throw.</summary>
    [Fact]
    public void Save_WhenTheFileCannotBeWritten_KeepsTheChangeInMemory()
    {
        Directory.CreateDirectory(SettingsPath + ".tmp");
        var service = NewService();

        Should.NotThrow(() => service.Update(s => s.DragSnappingEnabled = false));

        service.Current.DragSnappingEnabled.ShouldBeFalse();
        File.Exists(SettingsPath).ShouldBeFalse();
    }
}
