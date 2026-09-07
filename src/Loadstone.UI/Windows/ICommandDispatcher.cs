using Loadstone.Core.Commands;

namespace Loadstone.UI.Windows;

/// <summary>Runs a command on the focused window, or on a window captured earlier, and tells the user when it can't.</summary>
internal interface ICommandDispatcher
{
    void Run(WindowCommand command);

    void Run(WindowCommand command, nint hwnd);
}
