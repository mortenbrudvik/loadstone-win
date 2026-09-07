namespace Loadstone.Core.Settings;

public interface ISettingsService
{
    AppSettings Current { get; }

    /// <summary>Applies <paramref name="change"/> to <see cref="Current"/>, saves, and raises <see cref="Changed"/>.</summary>
    void Update(Action<AppSettings> change);

    event EventHandler? Changed;
}
