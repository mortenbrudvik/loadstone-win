using Loadstone.Core.Settings;

namespace Loadstone.UI.Tests;

public sealed class InMemorySettings : ISettingsService
{
    /// <summary>The mutable document, so a test can seed state without going through Update.</summary>
    public AppSettings Current { get; } = new();

    IReadOnlyAppSettings ISettingsService.Current => Current;

    /// <summary>What IsFirstRun reports. Default false: most tests are not exercising first run.</summary>
    public bool IsFirstRun { get; set; }

    /// <summary>What Update reports. Set false to stand in for a save that never reached disk.</summary>
    public bool SaveSucceeds { get; set; } = true;

    public event EventHandler? Changed;

    public bool Update(Action<AppSettings> change)
    {
        change(Current);
        Changed?.Invoke(this, EventArgs.Empty);
        return SaveSucceeds;
    }
}
