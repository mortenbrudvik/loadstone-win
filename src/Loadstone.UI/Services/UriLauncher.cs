using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using Microsoft.Extensions.Logging;

namespace Loadstone.UI.Services;

/// <summary>
/// Hands a URI to the shell's handler. There may not be one (no default browser, a locked-down
/// <c>ms-settings:</c> handler, a shell policy), and the throw would otherwise reach
/// <c>DispatcherUnhandledException</c> and end the process over a menu click, taking the hotkeys and
/// the user's edge snapping with it. Returns false when nothing was launched; the caller beeps.
/// </summary>
internal static class UriLauncher
{
    public static bool TryOpen(string uri, ILogger logger)
    {
        try
        {
            Process.Start(new ProcessStartInfo(uri) { UseShellExecute = true });
            return true;
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or PlatformNotSupportedException or FileNotFoundException)
        {
            logger.LogError(ex, "could not open {Uri}", uri);
            return false;
        }
    }
}
