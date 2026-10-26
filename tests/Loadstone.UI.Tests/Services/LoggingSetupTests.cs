using System.Globalization;
using System.IO;
using Loadstone.UI.Services;
using NLog.Common;
using Shouldly;
using Xunit;

namespace Loadstone.UI.Tests.Services;

public class LoggingSetupTests
{
    /// <summary>
    /// NLog names the file with ${shortdate}, which it renders in the invariant culture, and the probe
    /// that checks the log is writable looks for the same name. Formatted in the current culture the
    /// year is not Gregorian on th-TH (2569 for 2026) or ar-SA, so the probe looked for a file NLog
    /// never writes and reported a log that could not be written, on every launch, to the internal
    /// log. Thai is the culture whose default calendar makes the difference visible.
    /// </summary>
    [Fact]
    public void CurrentLogFile_NamesTheDayAsNLogDoes_WhateverTheCultureIs()
    {
        var was = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("th-TH");
            var expected = $"Loadstone-{DateTime.Now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}.log";

            Path.GetFileName(LoggingSetup.CurrentLogFile).ShouldBe(expected);
        }
        finally
        {
            CultureInfo.CurrentCulture = was;
        }
    }

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
