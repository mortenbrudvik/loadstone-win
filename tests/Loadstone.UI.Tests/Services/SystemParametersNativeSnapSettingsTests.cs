using Loadstone.Core.Platform;
using Loadstone.UI.Services;
using Shouldly;
using Xunit;

namespace Loadstone.UI.Tests.Services;

public class SystemParametersNativeSnapSettingsTests
{
    private readonly SystemParametersNativeSnapSettings _settings = new();

    [Fact]
    public void Read_KnownFlags_HaveAValue()
    {
        _settings.Read(NativeSnapFlag.DockMoving).ShouldNotBeNull();
        _settings.Read(NativeSnapFlag.SnapSizing).ShouldNotBeNull();
    }

    /// <summary>
    /// Writes back exactly what it read, so the developer's own edge snapping is unchanged - unless a
    /// Loadstone is running. With drag snapping on it has already written false over the user's value
    /// and holds the real one in its crash marker, so the read below answers false; if that Loadstone
    /// then exits between the read and the write, its restore puts the user's true back and this test
    /// writes false straight over it, with nothing left anywhere that remembers what the value was.
    /// The user's edge snapping would be off for good, from a test run. The production mutex is the
    /// app's own "am I already running" answer, so it is the right thing to ask.
    /// </summary>
    [Fact]
    public void Write_TheCurrentValueBack_SucceedsAndReadsTheSame()
    {
        Assert.SkipWhen(LoadstoneIsRunning(), "Loadstone is running; it owns these flags for as long as it does.");

        foreach (var flag in NativeSnap.ManagedFlags)
        {
            var current = _settings.Read(flag).ShouldNotBeNull();
            _settings.Write(flag, current).ShouldBeTrue();
            _settings.Read(flag).ShouldBe(current);
        }
    }

    private static bool LoadstoneIsRunning()
    {
        try
        {
            if (!Mutex.TryOpenExisting(SingleInstance.DefaultMutexName, out var running))
            {
                return false;
            }

            running.Dispose();
            return true;
        }
        catch (UnauthorizedAccessException)
        {
            // The name exists but this session may not open it, which still means something holds it.
            return true;
        }
    }
}
