namespace Loadstone.Core.Platform;

/// <summary>
/// Decodes the value Task Manager writes under Explorer's StartupApproved\Run key: a binary blob
/// whose first byte is even (0x02, 0x06) when the entry is enabled and odd (0x03, 0x07) when the
/// user disabled it. A missing value means enabled.
/// </summary>
public static class StartupApproval
{
    public static bool IsDisabled(byte[]? approvedValue) =>
        approvedValue is { Length: > 0 } && (approvedValue[0] & 1) == 1;
}
