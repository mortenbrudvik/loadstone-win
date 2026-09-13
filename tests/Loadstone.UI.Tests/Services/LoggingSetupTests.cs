using Loadstone.UI.Services;
using NLog.Common;
using Shouldly;
using Xunit;

namespace Loadstone.UI.Tests.Services;

public class LoggingSetupTests
{
    /// <summary>
    /// The single-instance handshake runs before the host, and with it the logger, exists. A failure
    /// there used to leave nothing on disk at all, which reads as an app that refuses to start; NLog's
    /// internal log is the one channel that needs no host, so it has to be reachable on its own.
    /// </summary>
    [Fact]
    public void EnableInternalLog_PointsNLogAtTheTempFile_WithoutBuildingTheConfiguration()
    {
        LoggingSetup.EnableInternalLog();

        InternalLogger.LogFile.ShouldBe(LoggingSetup.InternalLogFile);
        InternalLogger.LogLevel.ShouldBe(NLog.LogLevel.Warn);
    }
}
