using Loadstone.Core.Settings;

namespace Loadstone.Core.Tests.Settings;

/// <summary>An ISettingsService that never touches disk. Shared by every test that needs settings.</summary>
public sealed class InMemorySettings : ISettingsService
{
    public AppSettings Current { get; } = new();
    public int UpdateCount { get; private set; }
    public event EventHandler? Changed;

    public void Update(Action<AppSettings> change)
    {
        change(Current);
        UpdateCount++;
        Changed?.Invoke(this, EventArgs.Empty);
    }
}
