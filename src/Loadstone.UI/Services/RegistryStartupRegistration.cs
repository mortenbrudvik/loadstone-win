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

    /// <summary>After an installer moved the exe the Run value still points at the old path; rewrite it. Does nothing when not registered.</summary>
    public void RefreshPathIfStale()
    {
        using var run = Registry.CurrentUser.OpenSubKey(_runKeyPath, writable: true);
        if (run?.GetValue(ValueName) is string current && current != QuotedExePath)
        {
            run.SetValue(ValueName, QuotedExePath);
        }
    }
}
