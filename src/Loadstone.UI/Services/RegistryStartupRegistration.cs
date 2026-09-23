using System.IO;
using System.Security;
using Loadstone.Core.Platform;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Win32;

namespace Loadstone.UI.Services;

/// <summary>
/// Launch at login through the user's Run key. Task Manager's Startup tab records its own
/// enable/disable under the StartupApproved key; enabling here clears that so the entry is live again.
/// </summary>
/// <remarks>
/// Nothing here throws on a registry failure, because every caller is on the UI thread with no
/// handler over it: <c>App.OnStartup</c> refreshes the path, and the Settings view model reads
/// <see cref="Status"/> from its constructor and again on every tray click. The Run key is not
/// always readable - a policy or an ACL can deny it, and <c>OpenSubKey(writable: true)</c> throws
/// rather than returning null when it does - so a raw exception would mean the app failing to launch
/// or a tray click ending the process, taking the hotkeys and the user's edge snapping with it.
/// Failures are logged here, where the reason is still in hand, and reported as
/// <see cref="StartupStatus.Unknown"/> or false.
/// </remarks>
internal sealed class RegistryStartupRegistration : IStartupRegistration
{
    public const string ValueName = "Loadstone";

    private readonly ILogger<RegistryStartupRegistration> _logger;
    private bool _reportedMissingExe;
    private readonly string _runKeyPath;
    private readonly string _approvedKeyPath;
    private readonly string _exePath;

    public RegistryStartupRegistration(ILogger<RegistryStartupRegistration> logger)
        : this(
            logger,
            @"Software\Microsoft\Windows\CurrentVersion\Run",
            @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run",
            Environment.ProcessPath ?? throw new InvalidOperationException("the running exe has no path"))
    {
    }

    /// <summary>Test seam: scratch keys and a made-up exe path, with logging off.</summary>
    internal RegistryStartupRegistration(string runKeyPath, string approvedKeyPath, string exePath)
        : this(NullLogger<RegistryStartupRegistration>.Instance, runKeyPath, approvedKeyPath, exePath)
    {
    }

    private RegistryStartupRegistration(
        ILogger<RegistryStartupRegistration> logger,
        string runKeyPath,
        string approvedKeyPath,
        string exePath)
    {
        _logger = logger;
        _runKeyPath = runKeyPath;
        _approvedKeyPath = approvedKeyPath;
        _exePath = exePath;
    }

    private string QuotedExePath => $"\"{_exePath}\"";

    public StartupStatus Status
    {
        get
        {
            try
            {
                using var run = Registry.CurrentUser.OpenSubKey(_runKeyPath);
                if (run?.GetValue(ValueName) is not string current)
                {
                    return StartupStatus.NotRegistered;
                }

                // A value whose exe is gone is not a login item. InitializeAsync rewrites it when it
                // can; when that write was refused the toggle must not stay on.
                var exe = ExePathIn(current);
                if (!File.Exists(exe))
                {
                    if (!_reportedMissingExe)
                    {
                        _reportedMissingExe = true;
                        _logger.LogError("launch at login points at {Path}, which is not on disk", exe);
                    }

                    return StartupStatus.Unknown;
                }

                _reportedMissingExe = false;

                using var approved = Registry.CurrentUser.OpenSubKey(_approvedKeyPath);
                return StartupApproval.IsDisabled(approved?.GetValue(ValueName) as byte[])
                    ? StartupStatus.DisabledInTaskManager
                    : StartupStatus.Enabled;
            }
            catch (Exception ex) when (IsRegistryRefusal(ex))
            {
                _logger.LogError(ex, "could not read the launch-at-login registration");
                return StartupStatus.Unknown;
            }
        }
    }

    /// <summary>
    /// Writes the Run value *and* deletes the StartupApproved entry. The delete is the one
    /// write outside the Run key and it is deliberate: without it a disable from Task Manager's
    /// Startup tab outlives re-enabling from here, so the checkbox would go on and nothing happen.
    /// </summary>
    public Task<bool> EnableAsync()
    {
        try
        {
            using (var run = Registry.CurrentUser.CreateSubKey(_runKeyPath))
            {
                run.SetValue(ValueName, QuotedExePath);
            }

            using var approved = Registry.CurrentUser.OpenSubKey(_approvedKeyPath, writable: true);
            approved?.DeleteValue(ValueName, throwOnMissingValue: false);
            return Task.FromResult(true);
        }
        catch (Exception ex) when (IsRegistryRefusal(ex))
        {
            _logger.LogError(ex, "could not register launch at login");
            return Task.FromResult(false);
        }
    }

    public Task<bool> DisableAsync()
    {
        try
        {
            using var run = Registry.CurrentUser.OpenSubKey(_runKeyPath, writable: true);
            run?.DeleteValue(ValueName, throwOnMissingValue: false);
            return Task.FromResult(true);
        }
        catch (Exception ex) when (IsRegistryRefusal(ex))
        {
            _logger.LogError(ex, "could not unregister launch at login");
            return Task.FromResult(false);
        }
    }

    /// <summary>
    /// After an installer moved the exe the Run value points at a path that is no longer there; rewrite
    /// it to the running exe. A registered path that still exists is left alone even when it is a
    /// different build: a second copy on disk was registered deliberately, and repointing the entry at
    /// whichever copy happens to launch would hand the user's login item to a build output directory.
    /// Does nothing when not registered, and nothing when the key will not open - this runs during
    /// startup, and a login item pointing at the wrong path is not worth failing to launch over.
    /// </summary>
    public Task InitializeAsync()
    {
        try
        {
            using var run = Registry.CurrentUser.OpenSubKey(_runKeyPath, writable: true);
            if (run?.GetValue(ValueName) is string current && !File.Exists(ExePathIn(current)))
            {
                run.SetValue(ValueName, QuotedExePath);
            }
        }
        catch (Exception ex) when (IsRegistryRefusal(ex))
        {
            _logger.LogError(ex, "could not refresh the launch-at-login path");
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// The registry saying no rather than the code being wrong: a policy or ACL denial
    /// (<see cref="UnauthorizedAccessException"/>, <see cref="SecurityException"/>), a key deleted or
    /// a hive that will not load (<see cref="IOException"/>), or a handle already closed
    /// (<see cref="ObjectDisposedException"/>). Everything else - a bad value type, a null path - is a
    /// bug here and is left to the unhandled handler, which logs and says so.
    /// </summary>
    private static bool IsRegistryRefusal(Exception ex) =>
        ex is UnauthorizedAccessException or SecurityException or IOException or ObjectDisposedException;

    /// <summary>The exe path out of a Run value, which is the quoted path; anything after the closing quote is arguments.</summary>
    private static string ExePathIn(string value)
    {
        var trimmed = value.Trim();
        if (!trimmed.StartsWith('"'))
        {
            return trimmed;
        }

        var end = trimmed.IndexOf('"', 1);
        return end < 0 ? trimmed[1..] : trimmed[1..end];
    }
}
