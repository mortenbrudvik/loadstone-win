using Loadstone.Core.Settings;

namespace Loadstone.UI.Tests;

public sealed class InMemorySettings : ISettingsService
{
    public AppSettings Current { get; } = new();
    public event EventHandler? Changed;

    public void Update(Action<AppSettings> change)
    {
        change(Current);
        Changed?.Invoke(this, EventArgs.Empty);
    }
}
