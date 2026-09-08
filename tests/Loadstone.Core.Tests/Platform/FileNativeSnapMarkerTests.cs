using Loadstone.Core.Platform;
using Loadstone.Core.Settings;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using Xunit;

namespace Loadstone.Core.Tests.Platform;

/// <summary>
/// The crash marker's real file store. Every test builds its own marker under a temp directory and
/// deletes it again: this class writes an actual file, and one test pointed at
/// <see cref="FileNativeSnapMarker.DefaultPath"/> would hand the developer's next launch a crash
/// marker from a test run, which <see cref="NativeSnap"/> would then "recover" onto their real
/// Windows edge-snapping flags.
/// </summary>
public sealed class FileNativeSnapMarkerTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "loadstone-tests", Guid.NewGuid().ToString("N"));

    private string MarkerPath => Path.Combine(_directory, "native-snap-marker.json");

    private FileNativeSnapMarker NewMarker() => new(MarkerPath, NullLogger<FileNativeSnapMarker>.Instance);

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    [Fact]
    public void WriteThenRead_RoundTripsTheParkedValues()
    {
        var marker = NewMarker();

        marker.Write(new Dictionary<string, bool> { ["DockMoving"] = true, ["SnapSizing"] = false }).ShouldBeTrue();

        // A fresh instance, not the one that wrote: the store is the file, not any in-memory copy.
        NewMarker().Read().ShouldBe(new Dictionary<string, bool> { ["DockMoving"] = true, ["SnapSizing"] = false });
    }

    /// <summary>
    /// Keys are flag names because the store is JSON; <c>NativeSnap.ReadMarker</c> parses them back
    /// to <see cref="NativeSnapFlag"/> and drops anything it cannot. A marker written in some other
    /// spelling would be dropped at the next launch, and dropping it is how the user's real values
    /// get lost for good.
    /// </summary>
    [Fact]
    public void Write_ParksTheKeysAsFlagNames_SoNativeSnapCanParseThemBack()
    {
        var marker = NewMarker();
        var parked = NativeSnap.ManagedFlags.ToDictionary(flag => flag.ToString(), _ => true);

        marker.Write(parked).ShouldBeTrue();

        File.ReadAllText(MarkerPath).ShouldContain("\"DockMoving\": true");
        foreach (var name in marker.Read().ShouldNotBeNull().Keys)
        {
            Enum.TryParse<NativeSnapFlag>(name, ignoreCase: true, out var flag).ShouldBeTrue($"{name} is not a NativeSnapFlag");
            Enum.IsDefined(flag).ShouldBeTrue($"{name} parsed to an undefined NativeSnapFlag");
        }
    }

    [Fact]
    public void Read_NoMarkerFile_ReturnsNull()
    {
        NewMarker().Read().ShouldBeNull();
        Directory.Exists(_directory).ShouldBeFalse("Read must not create anything");
    }

    [Fact]
    public void Write_Null_ClearsTheMarker()
    {
        var marker = NewMarker();
        marker.Write(new Dictionary<string, bool> { ["DockMoving"] = true }).ShouldBeTrue();

        marker.Write(null).ShouldBeTrue();

        File.Exists(MarkerPath).ShouldBeFalse();
        NewMarker().Read().ShouldBeNull();
    }

    /// <summary>Restore clears the marker whether or not one was parked; nothing to delete is success.</summary>
    [Fact]
    public void Write_Null_WhenNothingIsParked_ReturnsTrue()
    {
        NewMarker().Write(null).ShouldBeTrue();
    }

    /// <summary>
    /// A half-written or hand-mangled marker reads as "nothing parked" rather than throwing: this is
    /// called during startup, and a throw here would cost the user the launch instead of one run's
    /// recovery. Null is deliberately indistinguishable from an absent marker - both mean there is
    /// nothing to put back.
    /// </summary>
    [Theory]
    [InlineData("")]
    [InlineData("{ \"DockMoving\": tr")]
    [InlineData("{ this is not json")]
    [InlineData("{ \"DockMoving\": \"yes\" }")]
    [InlineData("null")]
    public void Read_CorruptOrTruncatedFile_ReturnsNull(string contents)
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(MarkerPath, contents);

        // A propagating exception fails the test here, before the assertion runs.
        NewMarker().Read().ShouldBeNull();
    }

    /// <summary>
    /// The lock here stands in for the IOException arm; a denied file
    /// (UnauthorizedAccessException) and a locked-down profile (SecurityException) share its catch
    /// and cannot be provoked in-process.
    /// </summary>
    [Fact]
    public void Read_UnreadableFile_ReturnsNull()
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(MarkerPath, "{ \"DockMoving\": true }");
        using var exclusive = new FileStream(MarkerPath, FileMode.Open, FileAccess.Read, FileShare.None);

        // A propagating exception fails the test here, before the assertion runs.
        NewMarker().Read().ShouldBeNull();
    }

    /// <summary>
    /// The false is load-bearing, not cosmetic: NativeSnap.Suppress leaves Windows' edge snapping
    /// alone when the marker did not persist, because turning the flags off with nothing on disk
    /// recording what the user had makes a force-quit's loss permanent. So a store it cannot reach
    /// has to report false and park nothing - never throw, and never leave a marker behind.
    /// </summary>
    [Fact]
    public void Write_WhenTheStoreCannotBeReached_ReturnsFalse_AndParksNothing()
    {
        Directory.CreateDirectory(MarkerPath + ".tmp"); // the temp file's own path is a directory
        var marker = NewMarker();

        // A propagating exception fails the test here, before the assertions run.
        marker.Write(new Dictionary<string, bool> { ["DockMoving"] = true }).ShouldBeFalse();

        File.Exists(MarkerPath).ShouldBeFalse();
        marker.Read().ShouldBeNull();
    }

    /// <summary>
    /// The write is atomic - temp file, then move - so a failure leaves the previous marker rather
    /// than a half-written one. The previous marker is the only record of the user's values, and a
    /// truncated one reads as nothing parked, which is the same as having lost it.
    /// </summary>
    [Fact]
    public void Write_WhenTheStoreCannotBeReached_LeavesThePreviousMarkerIntact()
    {
        var marker = NewMarker();
        marker.Write(new Dictionary<string, bool> { ["DockMoving"] = true }).ShouldBeTrue();
        Directory.CreateDirectory(MarkerPath + ".tmp");

        marker.Write(new Dictionary<string, bool> { ["DockMoving"] = false }).ShouldBeFalse();

        NewMarker().Read().ShouldBe(new Dictionary<string, bool> { ["DockMoving"] = true });
    }

    [Fact]
    public void Write_ReplacesAnEarlierMarker()
    {
        var marker = NewMarker();
        marker.Write(new Dictionary<string, bool> { ["DockMoving"] = true, ["SnapSizing"] = true }).ShouldBeTrue();

        marker.Write(new Dictionary<string, bool> { ["SnapSizing"] = false }).ShouldBeTrue();

        NewMarker().Read().ShouldBe(new Dictionary<string, bool> { ["SnapSizing"] = false });
    }

    [Fact]
    public void Write_CreatesTheDirectoryAndLeavesNoTempFileBehind()
    {
        NewMarker().Write(new Dictionary<string, bool> { ["DockMoving"] = true }).ShouldBeTrue();

        Directory.GetFiles(_directory).ShouldBe([MarkerPath]);
    }

    /// <summary>
    /// Beside settings.json, in its own file. Pinned because every other test here deliberately
    /// avoids this path, so nothing else would notice it moving onto a user-visible file.
    /// </summary>
    [Fact]
    public void DefaultPath_SitsBesideSettingsJson_UnderItsOwnName()
    {
        var path = FileNativeSnapMarker.DefaultPath;

        Path.GetFileName(path).ShouldBe("native-snap-marker.json");
        Path.GetDirectoryName(path).ShouldBe(Path.GetDirectoryName(SettingsService.DefaultPath));
        path.ShouldNotBe(MarkerPath, "the tests must never write the real marker");
    }
}
