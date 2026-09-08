using Shouldly;
using Xunit;

namespace Loadstone.Core.Tests;

public class SmokeTests
{
    [Fact]
    public void TestProject_Builds_AndRuns()
    {
        (1 + 1).ShouldBe(2);
    }
}
