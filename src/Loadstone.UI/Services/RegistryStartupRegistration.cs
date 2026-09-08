using System.IO;
using Loadstone.Core.Platform;
using Microsoft.Win32;

namespace Loadstone.UI.Services;

/// <summary>
/// Launch at login through the user's Run key. Task Manager's Startup tab records its own
/// enable/disable under the StartupApproved key; enabling here clears that so the entry is live again.
/// </summary>
internal sealed class RegistryStartupRegistration : IStartupRegistration
{
    public const string ValueName = "Loadstone";

    private readonly string _runKeyPath;
    private readonly string _approvedKeyPath;
    private readonly string _exePath;

    public RegistryStartupRegistration()
        : this(
            @"Software\Microsoft\Windows\CurrentVersion\Run",
            @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run",
            Environment.ProcessPath ?? throw new InvalidOperationException("the running exe has no path"))
    {
    }

    internal RegistryStartupRegistration(string runKeyPath, string approvedKeyPath, string exePath)
    {
        _runKeyPath = runKeyPath;
        _approvedKeyPath = approvedKeyPath;
        _exePath = exePath;
    }

    private string QuotedExePath => $"\"{_exePath}\"";

    public StartupStatus Status
    {
        get
        {
            using var run = Registry.CurrentUser.OpenSubKey(_runKeyPath);
            if (run?.GetValue(ValueName) is not string)
            {
                return StartupStatus.NotRegistered;
            }

            using var approved = Registry.CurrentUser.OpenSubKey(_approvedKeyPath);
            return StartupApproval.IsDisabled(approved?.GetValue(ValueName) as byte[])
                ? StartupStatus.DisabledInTaskManager
                : StartupStatus.Enabled;
        }
    }

    public void Enable()
    {
        using (var run = Registry.CurrentUser.CreateSubKey(_runKeyPath))
        {
            run.SetValue(ValueName, QuotedExePath);
        }

        using var approved = Registry.CurrentUser.OpenSubKey(_approvedKeyPath, writable: true);
        approved?.DeleteValue(ValueName, throwOnMissingValue: false);
    }

    public void Disable()
    {
        using var run = Registry.CurrentUser.OpenSubKey(_runKeyPath, writable: true);
        run?.DeleteValue(ValueName, throwOnMissingValue: false);
    }

    /// <summary>
    /// After an installer moved the exe the Run value points at a path that is no longer there; rewrite
    /// it to the running exe. A registered path that still exists is left alone even when it is a
    /// different build: a second copy on disk was registered deliberately, and repointing the entry at
    /// whichever copy happens to launch would hand the user's login item to a build output directory.
    /// Does nothing when not registered.
    /// </summary>
    public void RefreshPathIfStale()
    {
        using var run = Registry.CurrentUser.OpenSubKey(_runKeyPath, writable: true);
        if (run?.GetValue(ValueName) is string current && !File.Exists(ExePathIn(current)))
        {
            run.SetValue(ValueName, QuotedExePath);
        }
    }

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
