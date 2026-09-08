using Loadstone.Core.Commands;
using Loadstone.Core.Windows;
using Loadstone.UI.Interop;
using Microsoft.Extensions.Logging;

namespace Loadstone.UI.Windows;

internal sealed class CommandDispatcher : ICommandDispatcher
{
    private readonly WindowDirector _director;
    private readonly ILogger<CommandDispatcher> _logger;
    private readonly Func<nint?> _focusedWindow;
    private readonly Action _beep;

    public CommandDispatcher(WindowDirector director, ILogger<CommandDispatcher> logger)
        : this(director, logger, FocusedWindow.Resolve, Feedback.Beep)
    {
    }

    internal CommandDispatcher(WindowDirector director, ILogger<CommandDispatcher> logger, Func<nint?> focusedWindow, Action beep)
    {
        _director = director;
        _logger = logger;
        _focusedWindow = focusedWindow;
        _beep = beep;
    }

    /// <summary>Runs <paramref name="command"/> on the foreground window.</summary>
    public void Run(WindowCommand command)
    {
        if (_focusedWindow() is not { } hwnd)
        {
            _logger.LogInformation("{Command} skipped: no manageable foreground window", command.Id());
            _beep();
            return;
        }

        // The pid is read here, next to the handle, and travels with it from now on. A window whose
        // owner cannot be read has no identity worth recording, so the command stops rather than
        // entering the restore ledger under a pid that is really "unknown".
        if (Win32Window.For(hwnd, _logger) is not { } window)
        {
            _logger.LogInformation("{Command} skipped: the process owning window {Hwnd} could not be read", command.Id(), hwnd);
            _beep();
            return;
        }

        Report(command, window, _director.Perform(command, window));
    }

    /// <summary>Runs <paramref name="command"/> on <paramref name="target"/>, which the caller resolved and re-validated earlier (the tray menu path).</summary>
    public void Run(WindowCommand command, WindowIdentity target)
    {
        var window = new Win32Window(target, _logger);
        Report(command, window, _director.Perform(command, window));
    }

    private void Report(WindowCommand command, Win32Window window, CommandOutcome outcome)
    {
        var id = command.Id();
        var pid = window.Identity.Pid;

        // A switch expression, not a statement, so CS8509 fails the build the day an OutcomeKind is
        // added: this method exists precisely to stop an outcome reaching the user as a bare beep with
        // nothing in the log, and a statement's missing case would do exactly that in silence.
        Action log = outcome.Kind switch
        {
            OutcomeKind.Moved => () => _logger.LogDebug("{Command}: moved window {Hwnd} of pid {Pid} to {Frame}", id, window.Hwnd, pid, window.ReadFrame()),
            OutcomeKind.Rejected => () => _logger.LogError("{Command}: window {Hwnd} of pid {Pid} rejected the frame (Win32 error {Error})", id, window.Hwnd, pid, outcome.Win32Error),
            OutcomeKind.FrameUnreadable => () => _logger.LogError("{Command}: could not read the frame of window {Hwnd} of pid {Pid}", id, window.Hwnd, pid),
            OutcomeKind.NoDisplay => () => _logger.LogError("{Command}: no display attached", id),
            OutcomeKind.NoOtherDisplay => () => _logger.LogInformation("{Command}: only one display attached", id),
            OutcomeKind.NothingToRestore => () => _logger.LogInformation("{Command}: nothing remembered for window {Hwnd} of pid {Pid}", id, window.Hwnd, pid),
        };

        log();
        if (!outcome.IsMoved)
        {
            _beep();
        }
    }
}
