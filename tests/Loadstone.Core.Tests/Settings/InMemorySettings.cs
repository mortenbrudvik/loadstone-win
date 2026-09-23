using Loadstone.Core.Settings;

namespace Loadstone.Core.Tests.Settings;

/// <summary>An ISettingsService that never touches disk. Shared by every test that needs settings.</summary>
public sealed class InMemorySettings : ISettingsService
{
    /// <summary>The mutable document, so a test can seed state without going through Update.</summary>
    public AppSettings Current { get; } = new();

    IReadOnlyAppSettings ISettingsService.Current => Current;

    /// <summary>What IsFirstRun reports. Default false: most tests are not exercising first run.</summary>
    public bool IsFirstRun { get; set; }

    /// <summary>What CanSave reports. Default true: the document was read, or there was no file.</summary>
    public bool CanSave { get; set; } = true;

    public int UpdateCount { get; private set; }

    /// <summary>What Update reports. Set false to stand in for a save that never reached disk.</summary>
    public bool SaveSucceeds { get; set; } = true;

    public event EventHandler? Changed;

    public bool Update(Action<AppSettings> change)
    {
        change(Current);
        UpdateCount++;
        Changed?.Invoke(this, EventArgs.Empty);
        return SaveSucceeds;
    }
}
