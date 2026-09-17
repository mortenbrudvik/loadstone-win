using System.Text.RegularExpressions;
using Shouldly;
using Xunit;

namespace Loadstone.UI.Tests;

/// <summary>
/// Startup behaviour that no test can exercise by constructing <c>App</c>: it is the WPF
/// <see cref="System.Windows.Application"/> and the real <c>OnStartup</c> takes the single-instance
/// mutex. These pin the source instead.
/// </summary>
public sealed class AppStartupTests
{
    /// <summary>
    /// Settings is opened by the tray, a left-click on the icon, or a second launch. Startup used to
    /// call <c>ShowSettings()</c> when settings.json was missing, which is every launch until the user
    /// first changes a setting — a window the user did not ask for, on a tray-only app.
    /// </summary>
    [Fact]
    public void App_DoesNotInvokeShowSettingsFromStartup()
    {
        var text = RepoFile.Text("src/Loadstone.UI/App.xaml.cs");

        Regex.Matches(text, @"ShowSettings\s*\(\s*\)\s*;").Count.ShouldBe(0);
    }
}
