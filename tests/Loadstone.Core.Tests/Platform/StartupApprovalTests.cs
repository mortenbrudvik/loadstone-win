using Loadstone.Core.Platform;
using Shouldly;
using Xunit;

namespace Loadstone.Core.Tests.Platform;

public class StartupApprovalTests
{
    [Fact]
    public void IsDisabled_MissingValue_MeansEnabled()
    {
        StartupApproval.IsDisabled(null).ShouldBeFalse();
        StartupApproval.IsDisabled([]).ShouldBeFalse();
    }

    [Theory]
    [InlineData(0x02, false)]
    [InlineData(0x06, false)]
    [InlineData(0x03, true)]
    [InlineData(0x07, true)]
    public void IsDisabled_LowBitOfTheFirstByte_MeansDisabled(byte first, bool disabled)
    {
        var value = new byte[12];
        value[0] = first;
        StartupApproval.IsDisabled(value).ShouldBe(disabled);
    }
}
