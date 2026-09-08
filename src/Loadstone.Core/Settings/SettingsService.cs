using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;

namespace Loadstone.Core.Settings;

public sealed class SettingsService : ISettingsService
{
    public static string DefaultPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Loadstone", "settings.json");

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    private readonly string _path;
    private readonly ILogger<SettingsService> _logger;
    private readonly Lock _lock = new();

    public SettingsService(string path, ILogger<SettingsService> logger)
    {
        _path = path;
        _logger = logger;
        Current = Load();
    }

    public AppSettings Current { get; }

    public event EventHandler? Changed;

    public void Update(Action<AppSettings> change)
    {
        lock (_lock)
        {
            change(Current);
            Save();
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Never throws: a settings file that cannot be read or parsed costs the user their customizations,
    /// not the app. At startup an escaping exception would be a crash before the tray exists.
    /// </summary>
    private AppSettings Load()
    {
        if (!File.Exists(_path))
        {
            return new AppSettings();
        }

        try
        {
            var loaded = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(_path), JsonOptions) ?? new AppSettings();

            // A hand-edited "shortcuts": null deserializes to a null dictionary, which every reader
            // would then dereference.
            if (loaded.Shortcuts is null)
            {
                loaded.Shortcuts = new();
            }

            return loaded;
        }
        catch (JsonException ex)
        {
            var backup = _path + ".corrupt";
            _logger.LogError(ex, "settings.json could not be parsed; backing it up to {Backup} and using defaults", backup);
            try
            {
                File.Copy(_path, backup, overwrite: true);
            }
            catch (Exception copyFailed) when (copyFailed is IOException or UnauthorizedAccessException)
            {
                _logger.LogError(copyFailed, "settings.json could not be backed up to {Backup}", backup);
            }

            return new AppSettings();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogError(ex, "settings.json could not be read; using defaults for this run");
            return new AppSettings();
        }
    }

    /// <summary>
    /// Atomic write: serialize to a temp file, then move it over the target. A failure is logged and
    /// the change stays in memory, so the toggle the user just flipped still applies to this run.
    /// </summary>
    private void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            var temp = _path + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(Current, JsonOptions));
            File.Move(temp, _path, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogError(ex, "settings.json could not be written; keeping the change in memory only");
        }
    }
}
