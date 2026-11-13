using System.IO;
using System.Security;
using System.Windows;
using System.Windows.Threading;
using Loadstone.UI.Views;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;

namespace Loadstone.UI.Services;

/// <summary>
/// The splash shown while Loadstone starts: <see cref="SplashWindow"/> on a thread and a dispatcher of
/// its own. The app's dispatcher does not pump until <c>OnStartup</c> returns, so a window on it could
/// neither draw nor move during the very part of startup the splash is for; on its own thread it is
/// drawn while the host is still being built. A window built off the app's thread is also never given
/// <see cref="Application.MainWindow"/>, the slot the Settings window holds because WPF-UI's theme
/// manager dresses whatever window is in it. The Mac app has no splash; this is Windows-only behaviour.
/// </summary>
/// <remarks>
/// Nothing on the splash thread may take the process down: an exception that escapes a thread that is
/// not the app's goes straight to the fatal handler, and the hotkeys and the user's edge snapping go
/// with it. So the thread's dispatcher handles its own exceptions, shuts itself down, and leaves the
/// failure here for <see cref="CloseAsync"/> to write, since startup has no logger when the splash begins.
/// </remarks>
internal sealed class StartupSplash
{
    /// <summary>
    /// How long the splash stays up however quickly startup finished: long enough for the windows to
    /// land and the name to be read. It never holds startup back: the hotkeys and the drag monitor are
    /// already live while it is still showing.
    /// </summary>
    internal static readonly TimeSpan MinimumShown = TimeSpan.FromSeconds(2);

    /// <summary>How long it waits after the pointer last moved over it or clicked the stone, so there is time to play.</summary>
    internal static readonly TimeSpan InteractionHold = TimeSpan.FromSeconds(1.5);

    internal static readonly TimeSpan FadeOut = TimeSpan.FromSeconds(0.3);

    private readonly TaskCompletionSource<SplashWindow> _shown = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource _ended = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private Exception? _failure;

    private StartupSplash()
    {
    }

    /// <summary>
    /// Starts the splash, topmost and centred on the primary display, in the app theme Windows is set
    /// to, and returns at once: the window is built and shown on its own thread while startup goes on.
    /// A splash that could not be shown is reported by <see cref="CloseAsync"/>, when there is a logger.
    /// Throws only when the thread itself cannot be started.
    /// </summary>
    public static StartupSplash Show() => Show(IsAppThemeLight(), SystemParameters.ClientAreaAnimation, MinimumShown);

    /// <summary>The same, with the theme, the motion and the minimum given, so a test need neither read the registry nor wait two seconds.</summary>
    internal static StartupSplash Show(bool light, bool animate, TimeSpan minimumShown)
    {
        var splash = new StartupSplash();
        var thread = new Thread(() => splash.Run(light, animate, minimumShown))
        {
            // Background, so a quit while the splash is still up does not wait for it.
            IsBackground = true,
            Name = "Loadstone splash",
        };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return splash;
    }

    /// <summary>
    /// Tells the splash that startup is done, and completes when the splash has gone: it fades once it
    /// has been up for its minimum and the pointer has left it alone. Never faults: the caller discards
    /// the task, and a fault here would otherwise become an unobserved-task exception instead of a
    /// splash that simply did not show.
    /// </summary>
    public async Task CloseAsync(ILogger logger)
    {
        try
        {
            SplashWindow window;
            try
            {
                window = await _shown.Task;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "could not show the splash screen; starting without it");
                return;
            }

            if (window.Problem is { } problem)
            {
                logger.LogWarning("{Problem}", problem);
            }

            try
            {
                // The window belongs to the splash thread, so the request is posted to it.
                await window.Dispatcher.InvokeAsync(window.CloseWhenDue);
            }
            catch (OperationCanceledException)
            {
                // The user clicked it away before startup finished, and its dispatcher has gone with it.
            }

            await _ended.Task;
            if (_failure is { } failure)
            {
                logger.LogWarning(failure, "the splash screen failed and was taken down");
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "could not close the splash screen");
        }
    }

    /// <summary>
    /// How long the splash has left: the rest of its minimum, or the rest of the wait after the pointer
    /// last played with it, whichever is longer, and never negative. A pointer that never touched it
    /// (<paramref name="sinceInteraction"/> null) leaves only the minimum.
    /// </summary>
    internal static TimeSpan RemainingDelay(TimeSpan shown, TimeSpan? sinceInteraction, TimeSpan minimumShown)
    {
        var minimum = minimumShown - shown;
        var hold = sinceInteraction is { } since ? InteractionHold - since : TimeSpan.Zero;
        var remaining = minimum > hold ? minimum : hold;
        return remaining > TimeSpan.Zero ? remaining : TimeSpan.Zero;
    }

    private void Run(bool light, bool animate, TimeSpan minimumShown)
    {
        try
        {
            var dispatcher = Dispatcher.CurrentDispatcher;
            dispatcher.UnhandledException += (_, e) =>
            {
                e.Handled = true;
                _failure ??= e.Exception;
                dispatcher.BeginInvokeShutdown(DispatcherPriority.Send);
            };

            var window = new SplashWindow(light, animate, minimumShown);
            window.Closed += (_, _) => dispatcher.BeginInvokeShutdown(DispatcherPriority.Background);
            window.Show();
            _shown.TrySetResult(window);
            Dispatcher.Run();
        }
        catch (Exception ex)
        {
            if (!_shown.TrySetException(ex))
            {
                _failure ??= ex;
            }
        }
        finally
        {
            _ended.TrySetResult();
        }
    }

    /// <summary>
    /// The app theme, not the taskbar's: the Settings window follows this one, and the splash is its
    /// forerunner. TrayController reads SystemUsesLightTheme from the same key for the opposite reason.
    /// </summary>
    private static bool IsAppThemeLight() =>
        IsAppThemeLight(() => Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize"));

    /// <summary>
    /// The same, over whatever opens the key. A policy or an ACL can refuse an HKCU read, as the tray's
    /// identical read of SystemUsesLightTheme guards against, and this runs before there is a logger:
    /// a refused read used to cost the whole splash where it should cost only the theme. Dark is the
    /// fallback, as it is for a value that is not there.
    /// </summary>
    internal static bool IsAppThemeLight(Func<RegistryKey?> openPersonalize)
    {
        try
        {
            using var key = openPersonalize();
            return key?.GetValue("AppsUseLightTheme") is int value && value == 1;
        }
        catch (Exception ex) when (ex is SecurityException or UnauthorizedAccessException or IOException)
        {
            return false;
        }
    }
}
