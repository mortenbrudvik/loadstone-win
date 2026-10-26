using System.Globalization;
using System.IO;
using System.Security;
using NLog.Common;
using NLog.Config;
using NLog.Targets;
using NLog.Targets.Wrappers;

namespace Loadstone.UI.Services;

internal static class LoggingSetup
{
    public static string LogDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Loadstone", "Logs");

    /// <summary>Today's file: what ${shortdate} in <see cref="Build"/> resolves to, spelled out so it can be looked for.</summary>
    public static string CurrentLogFile => Path.Combine(LogDirectory, $"Loadstone-{DateTime.Now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}.log");

    /// <summary>
    /// Where NLog reports its own failures. In the temp directory on purpose: what it usually has to
    /// report is that <see cref="LogDirectory"/> could not be created or written, so the log
    /// directory is the one place the report must not depend on.
    /// </summary>
    public static string InternalLogFile => Path.Combine(Path.GetTempPath(), "Loadstone-nlog.log");

    /// <summary>
    /// The size of today's log file as it is now, to be taken before writing the line
    /// <see cref="VerifyWritable"/> will look for.
    /// </summary>
    /// <remarks>
    /// A file that cannot be measured answers 0, the same as one that is not there yet: that softens
    /// the check that follows rather than inventing a failure out of a read that was merely locked
    /// for a moment.
    /// </remarks>
    public static long CurrentLogLength()
    {
        try
        {
            var file = new FileInfo(CurrentLogFile);
            return file.Exists ? file.Length : 0;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SecurityException or ArgumentException)
        {
            return 0;
        }
    }

    /// <summary>
    /// Points NLog's own log at <see cref="InternalLogFile"/>. Static and host-free on purpose: the
    /// single-instance handshake runs before the host, and with it the logger, exists, and a failure
    /// there used to leave nothing on disk at all - an app that looks as if it refuses to start.
    /// <see cref="Build"/> calls this too, so calling it first costs nothing.
    /// </summary>
    public static void EnableInternalLog()
    {
        InternalLogger.LogFile = InternalLogFile;
        InternalLogger.LogLevel = NLog.LogLevel.Warn;
    }

    /// <summary>Daily files, 14 kept, 10 MB archive threshold, written asynchronously.</summary>
    public static LoggingConfiguration Build()
    {
        // NLog is this app's only error channel and it is built to swallow its own failures:
        // ThrowExceptions stays off - a logging failure must not take a window tiler down - which
        // leaves an unwritable log directory looking exactly like a quiet run. The internal logger
        // is the one place those complaints can still land.
        EnableInternalLog();

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

        // Block rather than the default Discard. Loadstone writes a line or two per user action, so
        // a queue that fills means the file target is stuck - and the lines written while something
        // is going wrong are the ones worth waiting for. The wrapper drains whether or not the
        // writes succeed, so a broken file target empties the queue instead of wedging the caller.
        config.AddRule(NLog.LogLevel.Debug, NLog.LogLevel.Fatal, new AsyncTargetWrapper(file)
        {
            OverflowAction = AsyncTargetWrapperOverflowAction.Block,
        });
        return config;
    }

    /// <summary>
    /// Looks for the line the caller just wrote and flushed, by comparing today's log file against
    /// the size it had before that write. A log directory that could not be created, or a file that
    /// could not be opened, leaves the app running with every diagnostic going nowhere and NLog
    /// saying nothing about it, so the only honest check is whether this run's own line reached the
    /// disk.
    /// </summary>
    /// <remarks>
    /// A change in size and not mere existence, because on the second launch of a day the file the
    /// first launch wrote is already there and not empty: existence is satisfied by a run that has
    /// written nothing. A change rather than growth, because the archive rollover replaces the file
    /// with a shorter one and that is still proof of a write. What this cannot tell is *which* line
    /// landed - any write from anywhere in the process counts - and it cannot tell anything at all
    /// when <see cref="CurrentLogLength"/> could not read the file before the write, where its 0
    /// softens this back to "the file exists and is not empty".
    /// </remarks>
    /// <param name="lengthBefore">What <see cref="CurrentLogLength"/> answered before the caller wrote its line.</param>
    /// <returns>
    /// What to tell the user, or null while logging works. Also written to
    /// <see cref="InternalLogFile"/>, which is where a later diagnosis can still find it: NLog is
    /// the only channel this app has and NLog is what just failed.
    /// </returns>
    public static string? VerifyWritable(long lengthBefore)
    {
        string? failure;
        try
        {
            var file = new FileInfo(CurrentLogFile);
            failure = file.Exists && file.Length > 0 && file.Length != lengthBefore
                ? null
                : $"Loadstone could not write its log at {CurrentLogFile}. NLog's own report is in {InternalLogFile}.";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SecurityException or ArgumentException)
        {
            failure = $"Loadstone could not read back its log at {CurrentLogFile}: {ex.Message}";
        }

        if (failure is not null)
        {
            InternalLogger.Error(failure);
        }

        return failure;
    }
}
