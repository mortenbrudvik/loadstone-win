using System.IO;
using NLog.Config;
using NLog.Targets;
using NLog.Targets.Wrappers;

namespace Loadstone.UI.Services;

internal static class LoggingSetup
{
    public static string LogDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Loadstone", "Logs");

    /// <summary>Daily files, 14 kept, 10 MB archive threshold, written asynchronously.</summary>
    public static LoggingConfiguration Build()
    {
        var config = new LoggingConfiguration();
        var file = new FileTarget("file")
        {
            FileName = Path.Combine(LogDirectory, "Loadstone-${shortdate}.log"),
            Layout = "${longdate} ${level:uppercase=true} ${logger:shortName=true} ${message} ${exception:format=tostring}",
            ArchiveAboveSize = 10_000_000,
            MaxArchiveFiles = 14,
        };

        // The console lifetime announces the hosting environment and the content root path at every
        // launch, which puts an installation path in the user's log for no benefit. Rules are evaluated
        // in order and this one is final, so those events stop here; a warning from the same logger
        // still falls through to the file. It has to be an NLog rule: NLog's provider drops the
        // Microsoft.Extensions.Logging filters by default, so AddFilter would be ignored.
        config.AddRule(new LoggingRule("Microsoft.Hosting.Lifetime", NLog.LogLevel.Trace, NLog.LogLevel.Info, new NullTarget()) { Final = true });
        config.AddRule(NLog.LogLevel.Debug, NLog.LogLevel.Fatal, new AsyncTargetWrapper(file));
        return config;
    }
}
