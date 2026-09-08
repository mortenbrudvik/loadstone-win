namespace Loadstone.UI.Services;

/// <summary>
/// The tray's requests to the application, filled in by App once the host is built. This class is what
/// breaks the cycle — App builds the container that builds the tray, so the tray resolves this instead of
/// referencing App, and the delegates are set afterwards.
/// </summary>
internal sealed class ShellActions
{
    public Action OpenSettingsAction { get; set; } = () => { };

    public Action QuitAction { get; set; } = () => { };

    public void OpenSettings() => OpenSettingsAction();

    public void Quit() => QuitAction();
}
