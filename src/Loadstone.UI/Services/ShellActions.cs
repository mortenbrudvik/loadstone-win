using Loadstone.UI.Tray;

namespace Loadstone.UI.Services;

/// <summary>The tray's requests to the application, filled in by App once the host is built.</summary>
internal sealed class ShellActions : IShellActions
{
    public Action OpenSettingsAction { get; set; } = () => { };

    public Action QuitAction { get; set; } = () => { };

    public void OpenSettings() => OpenSettingsAction();

    public void Quit() => QuitAction();
}
