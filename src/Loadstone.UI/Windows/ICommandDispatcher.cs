using Loadstone.Core.Commands;
using Loadstone.Core.Windows;

namespace Loadstone.UI.Windows;

/// <summary>Runs a command on the focused window, or on a window captured earlier, and tells the user when it can't.</summary>
internal interface ICommandDispatcher
{
    void Run(WindowCommand command);

    /// <summary>
    /// Runs on a window the caller captured earlier, named by handle *and* owning process. The pid is
    /// not decoration: the tray menu captures the window that was in front before the tray, and Windows
    /// recycles handles quickly, so the pid is the only thing separating the window the user aimed at
    /// from a stranger that inherited its handle while the menu was open.
    /// </summary>
    void Run(WindowCommand command, WindowIdentity target);
}
