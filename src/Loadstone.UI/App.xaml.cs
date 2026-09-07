using System.Windows;

namespace Loadstone.UI;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        // Task 21 wires the host, tray, hotkeys, and drag monitor here. Until then the app exits.
        Shutdown();
    }
}
