namespace Loadstone.UI.Tray;

/// <summary>What the tray asks the application to do. App implements it; the tray never references App.</summary>
internal interface IShellActions
{
    void OpenSettings();

    void Quit();
}
