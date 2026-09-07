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
        _settings.Read(NativeSnap.DockMoving).ShouldNotBeNull();
        _settings.Read(NativeSnap.SnapSizing).ShouldNotBeNull();
    }

    [Fact]
    public void Read_UnknownFlag_IsNull()
    {
        _settings.Read("NoSuchFlag").ShouldBeNull();
        _settings.Write("NoSuchFlag", true).ShouldBeFalse();
    }

    [Fact]
    public void Write_TheCurrentValueBack_SucceedsAndReadsTheSame()
    {
        // Writes only what is already there, so the user's setting is unchanged by this test.
        foreach (var flag in NativeSnap.ManagedFlags)
        {
            var current = _settings.Read(flag).ShouldNotBeNull();
            _settings.Write(flag, current).ShouldBeTrue();
            _settings.Read(flag).ShouldBe(current);
        }
    }
}
