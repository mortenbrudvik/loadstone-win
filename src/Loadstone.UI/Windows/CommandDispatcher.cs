using Loadstone.Core.Commands;
using Loadstone.Core.Windows;
using Loadstone.UI.Interop;
using Microsoft.Extensions.Logging;
using static Loadstone.UI.Interop.NativeMethods;

namespace Loadstone.UI.Windows;

internal sealed class CommandDispatcher : ICommandDispatcher
{
    private readonly WindowDirector _director;
    private readonly ILogger<CommandDispatcher> _logger;
    private readonly Func<nint?> _focusedWindow;
    private readonly Action _beep;

    public CommandDispatcher(WindowDirector director, ILogger<CommandDispatcher> logger)
        : this(director, logger, FocusedWindow.Resolve, () => User32.MessageBeep(NativeConstants.MB_OK))
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

        Run(command, hwnd);
    }

    /// <summary>Runs <paramref name="command"/> on <paramref name="hwnd"/>, which the caller resolved earlier (the tray menu path).</summary>
    public void Run(WindowCommand command, nint hwnd)
    {
        var window = new Win32Window(hwnd);
        var outcome = _director.Perform(command, window);
        Report(command, window, outcome);
    }

    private void Report(WindowCommand command, Win32Window window, CommandOutcome outcome)
    {
        var id = command.Id();
        var pid = window.Identity.Pid;
        switch (outcome.Kind)
        {
            case OutcomeKind.Moved:
                _logger.LogDebug("{Command}: moved window {Hwnd} of pid {Pid} to {Frame}", id, window.Hwnd, pid, window.Frame);
                return;
            case OutcomeKind.Rejected:
                _logger.LogError("{Command}: window {Hwnd} of pid {Pid} rejected the frame (Win32 error {Error})", id, window.Hwnd, pid, outcome.Win32Error);
                break;
            case OutcomeKind.FrameUnreadable:
                _logger.LogError("{Command}: could not read the frame of window {Hwnd} of pid {Pid}", id, window.Hwnd, pid);
                break;
            case OutcomeKind.NoDisplay:
                _logger.LogError("{Command}: no display attached", id);
                break;
            case OutcomeKind.NoOtherDisplay:
                _logger.LogInformation("{Command}: only one display attached", id);
                break;
            case OutcomeKind.NothingToRestore:
                _logger.LogInformation("{Command}: nothing remembered for window {Hwnd} of pid {Pid}", id, window.Hwnd, pid);
                break;
        }

        _beep();
    }
}
