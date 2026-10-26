using System.Diagnostics;
using System.IO;
using System.Security;
using System.Windows;
using System.Windows.Threading;
using Loadstone.UI.Windows;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;

namespace Loadstone.UI.Services;

/// <summary>
/// The image shown while Loadstone starts. WPF's own <see cref="SplashScreen"/> rather than a window of
/// ours: it is not a <see cref="Window"/>, so it never takes <see cref="Application.MainWindow"/> - the
/// slot the Settings window holds, because WPF-UI's theme manager dresses whatever window is in it - and
/// it is up before the host and WPF-UI have loaded. The Mac app has no splash; this is Windows-only
/// behaviour.
/// </summary>
internal sealed class StartupSplash
{
    /// <summary>
    /// How long the splash stays up however quickly startup finished. It never holds startup back: the
    /// hotkeys and the drag monitor are already live while it is still showing.
    /// </summary>
    internal static readonly TimeSpan MinimumShown = TimeSpan.FromSeconds(1.5);

    private static readonly TimeSpan FadeOut = TimeSpan.FromSeconds(0.3);

    /// <summary>The scales tools/icons/generate.ps1 draws the splash at, in percent.</summary>
    private static readonly int[] Scales = [100, 150, 200];

    private readonly SplashScreen _screen;
    private readonly Dispatcher _dispatcher;
    private readonly Stopwatch _shown;

    private StartupSplash(SplashScreen screen, Dispatcher dispatcher, Stopwatch shown)
    {
        _screen = screen;
        _dispatcher = dispatcher;
        _shown = shown;
    }

    /// <summary>
    /// Shows the splash, topmost and centred on the primary display, in the app theme Windows is set to.
    /// Throws when the image cannot be loaded: the caller has no logger yet, so it is the one that holds
    /// on to the exception until there is somewhere to write it.
    /// </summary>
    public static StartupSplash Show()
    {
        // SplashScreen centres itself on the primary display (SM_CXSCREEN), so that display's DPI is the
        // one the image has to be drawn for. An empty snapshot leaves the 100 percent image.
        var dpi = Displays.Snapshot() is [var primary, ..] ? primary.Dpi : 96;
        var screen = new SplashScreen(typeof(StartupSplash).Assembly, ResourceName(dpi, IsAppThemeLight()));

        // Topmost is ours to ask for: the splash is this process's own window.
        screen.Show(autoClose: false, topMost: true);
        return new StartupSplash(screen, Dispatcher.CurrentDispatcher, Stopwatch.StartNew());
    }

    /// <summary>
    /// Fades the splash out once it has been up for <see cref="MinimumShown"/>, and completes when the
    /// fade has finished. Never faults: the caller discards the task, and a fault here would otherwise
    /// become an unobserved-task exception instead of a splash that simply did not fade.
    /// </summary>
    public async Task CloseAsync(ILogger logger)
    {
        try
        {
            await Task.Delay(RemainingDelay(_shown.Elapsed));

            // Marshalled explicitly rather than trusting the continuation to land on the UI thread: the
            // splash window belongs to the thread that showed it.
            await _dispatcher.InvokeAsync(() => _screen.Close(FadeOut));
            await Task.Delay(FadeOut);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "could not close the splash screen");
        }
    }

    /// <summary>
    /// The packed image for a display at <paramref name="dpi"/>: the nearest scale drawn, the larger one
    /// on a tie, since a splash slightly too large reads better than one slightly too small. Lowercase
    /// because that is how the build keys the assembly's .g resources, and SplashScreen looks it up there.
    /// </summary>
    internal static string ResourceName(int dpi, bool lightTheme)
    {
        var percent = dpi * 100.0 / 96;
        var scale = Scales.MinBy(s => (Math.Abs(s - percent), -s));
        return $"assets/splash/splash-{(lightTheme ? "light" : "dark")}.scale-{scale}.png";
    }

    /// <summary>What is left of <see cref="MinimumShown"/>; zero once startup took longer than that.</summary>
    internal static TimeSpan RemainingDelay(TimeSpan elapsed) => elapsed >= MinimumShown ? TimeSpan.Zero : MinimumShown - elapsed;

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
