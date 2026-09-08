using System.IO;
using Loadstone.UI.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using Xunit;

namespace Loadstone.UI.Tests.Services;

public class UriLauncherTests
{
    /// <summary>
    /// The one path this class exists for. <c>Process.Start</c> with UseShellExecute throws when the
    /// shell has nothing to run, and the tray menu has no handler over it: that throw reaches
    /// DispatcherUnhandledException and ends the process over a menu click, taking the hotkeys and the
    /// user's edge snapping with it. Symptom: Help on a machine with no default browser, or Open
    /// Startup Apps with a locked-down ms-settings handler, quits Loadstone.
    /// </summary>
    /// <remarks>
    /// A made-up <c>scheme:</c> is the wrong probe and was tried first: Windows answers an unregistered
    /// scheme by opening its own "how do you want to open this" picker and reporting success, so the
    /// test asserted the opposite of what the OS does and put a dialog on the desktop of whoever ran
    /// the suite. A path with no file behind it fails inside ShellExecute instead, which is the branch
    /// the catch is written for, and shows nobody anything.
    /// </remarks>
    [Fact]
    public void TryOpen_WhenTheShellCannotOpenIt_ReportsFalseInsteadOfThrowing()
    {
        var missing = Path.Combine(Path.GetTempPath(), "loadstone-tests", Guid.NewGuid().ToString("N"), "nothing.exe");
        File.Exists(missing).ShouldBeFalse();

        var opened = Should.NotThrow(() => UriLauncher.TryOpen(missing, NullLogger.Instance));

        opened.ShouldBeFalse();
    }
}
