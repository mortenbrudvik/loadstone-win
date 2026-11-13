using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Loadstone.Core.Commands;
using Loadstone.UI.Input;
using Loadstone.UI.Interop;
using Loadstone.UI.Services;
using Microsoft.Extensions.Logging;

namespace Loadstone.UI.ViewModels;

/// <summary>
/// The About tab: what Loadstone is, which build this is, and the few places a user is sent from
/// here. The miniature desktop on the tab asks it for the chord each command is bound to, so the keys
/// it shows are the user's own and not the defaults.
/// </summary>
internal sealed partial class AboutViewModel : ObservableObject
{
    public const string SourceCodeUrl = "https://github.com/mortenbrudvik/loadstone-win";
    public const string IssuesUrl = "https://github.com/mortenbrudvik/loadstone-win/issues";
    public const string MacAppUrl = "https://github.com/mortenbrudvik/loadstone";

    /// <summary>The author's homepage. The tray's Help item opens a page on the same site.</summary>
    public const string HomepageUrl = "https://brudvik.com";

    /// <summary>The Store's review page for Loadstone. The product id is the one in the README's install badge.</summary>
    public const string StoreReviewUri = "ms-windows-store://review/?ProductId=9NJZ0KVVD5N2";

    /// <summary>How long "Copied" stays beside the version.</summary>
    private static readonly TimeSpan CopiedShown = TimeSpan.FromSeconds(1.6);

    private readonly Func<WindowCommand, Shortcut?> _shortcutOf;
    private readonly ILogger<AboutViewModel> _logger;
    private readonly Func<string, bool> _tryOpen;
    private readonly Action _beep;
    private readonly string _logDirectory;
    private readonly Func<string, bool> _tryCopy;
    private readonly Func<TimeSpan, Task> _pause;
    private int _copies;

    public AboutViewModel(HotkeyCenter hotkeys, ILogger<AboutViewModel> logger)
        : this(hotkeys.EffectiveShortcut, logger, uri => UriLauncher.TryOpen(uri, logger), Feedback.Beep, LoggingSetup.LogDirectory,
            text => TryCopy(text, logger), Task.Delay)
    {
    }

    /// <summary>
    /// The shell, the clipboard, the log directory and the clock as seams, the way
    /// <c>CommandDispatcher</c> takes its focused window and its beep: through the public constructor
    /// a click starts a browser, Explorer on the real log folder, or overwrites the clipboard, none of
    /// which a test may do.
    /// </summary>
    internal AboutViewModel(
        Func<WindowCommand, Shortcut?> shortcutOf,
        ILogger<AboutViewModel> logger,
        Func<string, bool> tryOpen,
        Action beep,
        string logDirectory,
        Func<string, bool> tryCopy,
        Func<TimeSpan, Task> pause)
    {
        _shortcutOf = shortcutOf;
        _logger = logger;
        _tryOpen = tryOpen;
        _beep = beep;
        _logDirectory = logDirectory;
        _tryCopy = tryCopy;
        _pause = pause;
        Message = "";
        CopyMessage = "";
    }

    /// <summary>
    /// A version, not a commit: the informational version carries a "+sha" suffix on any build with
    /// source link, and the split is the only thing that keeps it off the tab.
    /// </summary>
    public string Version { get; } =
        Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+')[0]
        ?? Assembly.GetExecutingAssembly().GetName().Version?.ToString(3)
        ?? "?";

    /// <summary>The version as its button reads.</summary>
    public string VersionLabel => $"Version {Version}";

    /// <summary>The copyright line the build stamps into the assembly, so the tab and the file properties cannot disagree.</summary>
    public string Copyright { get; } =
        Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyCopyrightAttribute>()?.Copyright ?? "";

    /// <summary>Why the last button did nothing, or empty while the shell has done as asked.</summary>
    [ObservableProperty]
    public partial string Message { get; set; }

    /// <summary>"Copied" for a moment after the version went to the clipboard, or why it did not.</summary>
    [ObservableProperty]
    public partial string CopyMessage { get; set; }

    /// <summary>The chord <paramref name="command"/> is bound to right now, or null when it is unbound.</summary>
    public Shortcut? ShortcutOf(WindowCommand command) => _shortcutOf(command);

    [RelayCommand]
    private void OpenSourceCode() =>
        OpenOrBeep(SourceCodeUrl, $"Could not open the source code; Windows refused. It is at {SourceCodeUrl}.");

    [RelayCommand]
    private void ReportProblem() =>
        OpenOrBeep(IssuesUrl, $"Could not open the issue tracker; Windows refused. It is at {IssuesUrl}.");

    [RelayCommand]
    private void RateInStore() =>
        OpenOrBeep(StoreReviewUri, "Could not open the Microsoft Store; Windows refused.");

    [RelayCommand]
    private void OpenHomepage() =>
        OpenOrBeep(HomepageUrl, $"Could not open the homepage; Windows refused. It is at {HomepageUrl}.");

    [RelayCommand]
    private void OpenMacApp() =>
        OpenOrBeep(MacAppUrl, $"Could not open Loadstone for macOS; Windows refused. It is at {MacAppUrl}.");

    /// <summary>
    /// Creates the directory before opening it. NLog makes it on first write, so on a run where
    /// logging failed — exactly when a user is sent looking — it is not there and the shell would
    /// beep instead of showing anything.
    /// </summary>
    [RelayCommand]
    private void OpenLogFolder()
    {
        try
        {
            Directory.CreateDirectory(_logDirectory);
        }
        // Wider than this path is likely to need, on purpose: an escape from a button click on the UI
        // thread ends the process and takes the hotkeys and the user's edge snapping with it, which is
        // the rule IStartupRegistration states about itself twice. CreateDirectory can also answer
        // ArgumentException or NotSupportedException for a path it will not accept.
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SecurityException
            or ArgumentException or NotSupportedException)
        {
            _logger.LogError(ex, "could not create the log directory before opening it");
        }

        OpenOrBeep(_logDirectory, $"Could not open the log folder; Windows refused. It is at {_logDirectory}.");
    }

    /// <summary>
    /// Puts the version on the clipboard and says so for a moment. A second click while "Copied" is
    /// still showing restarts the moment rather than letting the first click's timer clear it early.
    /// </summary>
    [RelayCommand]
    private async Task CopyVersionAsync()
    {
        var copy = ++_copies;
        CopyMessage = _tryCopy(Version) ? "Copied" : "Could not copy; the clipboard is busy.";
        await _pause(CopiedShown);
        if (copy == _copies)
        {
            CopyMessage = "";
        }
    }

    /// <summary>
    /// Opens the URI. When nothing handled it, beeps and says so under the buttons: the beep is the
    /// cue and the caption the explanation, which is also what a screen reader announces. Cleared
    /// first, so a later attempt that works takes an earlier refusal down with it.
    /// </summary>
    private void OpenOrBeep(string uri, string refused)
    {
        Message = "";
        if (!_tryOpen(uri))
        {
            _beep();
            Message = refused;
        }
    }

    /// <summary>
    /// The clipboard belongs to whichever process has it open, and another one holding it makes
    /// SetText throw after its own retries. From a button click on the UI thread that would end the
    /// process, so it is a refusal to report instead.
    /// </summary>
    private static bool TryCopy(string text, ILogger logger)
    {
        try
        {
            Clipboard.SetText(text);
            return true;
        }
        catch (Exception ex) when (ex is ExternalException or ThreadStateException)
        {
            logger.LogWarning(ex, "could not copy the version to the clipboard");
            return false;
        }
    }
}
