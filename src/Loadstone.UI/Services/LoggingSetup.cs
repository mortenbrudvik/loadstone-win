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
        config.AddRule(NLog.LogLevel.Debug, NLog.LogLevel.Fatal, new AsyncTargetWrapper(file));
        return config;
    }
}
