using System.Security;
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
    private readonly bool _canSave;

    /// <summary>The mutable document. Only <see cref="Update"/> hands it out, and only inside the callback.</summary>
    private readonly AppSettings _current;

    public SettingsService(string path, ILogger<SettingsService> logger)
    {
        _path = path;
        _logger = logger;
        IsFirstRun = !File.Exists(path);
        (_current, _canSave) = Load();
    }

    public IReadOnlyAppSettings Current => _current;

    public bool IsFirstRun { get; }

    public bool CanSave => _canSave;

    /// <summary>
    /// Raised on the thread that called <see cref="Update"/>: nothing here marshals, and in production
    /// every write comes from the UI thread. Subscribers are isolated from each other, see
    /// <see cref="RaiseChanged"/>.
    /// </summary>
    public event EventHandler? Changed;

    public bool Update(Action<AppSettings> change)
    {
        bool saved;
        lock (_lock)
        {
            change(_current);
            _current.Shortcuts ??= [];
            saved = Save();
        }

        RaiseChanged();
        return saved;
    }

    /// <summary>
    /// Never throws: a settings file that cannot be read or parsed costs the user their customizations,
    /// not the app. At startup an escaping exception would be a crash before the tray exists, and this
    /// runs in the constructor, so <see cref="SecurityException"/> from a locked-down profile counts too.
    /// </summary>
    /// <returns>The document, and whether a later save may replace the file. False only when the file existed and its bytes were never read.</returns>
    private (AppSettings Settings, bool CanSave) Load()
    {
        if (!File.Exists(_path))
        {
            return (new AppSettings(), true);
        }

        try
        {
            var loaded = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(_path), JsonOptions) ?? new AppSettings();

            // A hand-edited "shortcuts": null deserializes to a null dictionary, which every reader
            // would then dereference.
            loaded.Shortcuts ??= [];

            return (loaded, true);
        }
        catch (JsonException ex)
        {
            var backup = _path + ".corrupt";
            _logger.LogError(ex, "settings.json could not be parsed; backing it up to {Backup} and using defaults", backup);
            try
            {
                File.Copy(_path, backup, overwrite: true);
            }
            catch (Exception copyFailed) when (copyFailed is IOException or UnauthorizedAccessException or SecurityException)
            {
                _logger.LogError(copyFailed, "settings.json could not be backed up to {Backup}", backup);
            }

            // The bytes were read and kept aside. Replacing the original with defaults is the recovery.
            return (new AppSettings(), true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SecurityException)
        {
            _logger.LogError(ex, "settings.json could not be read; using defaults for this run and leaving the file untouched");
            return (new AppSettings(), false);
        }
    }

    /// <summary>
    /// Atomic write: serialize to a temp file, then move it over the target. A failure is logged and
    /// the change stays in memory, so the toggle the user just flipped still applies to this run.
    /// </summary>
    /// <returns>False when the change did not reach disk.</returns>
    private bool Save()
    {
        if (!_canSave)
        {
            _logger.LogError("settings.json was not read this run; keeping the change in memory and leaving the file untouched");
            return false;
        }

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            var temp = _path + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(_current, JsonOptions));
            File.Move(temp, _path, overwrite: true);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SecurityException)
        {
            _logger.LogError(ex, "settings.json could not be written; keeping the change in memory only");
            return false;
        }
    }

    /// <summary>
    /// Isolates subscribers: one that throws is logged and the rest still run. A single bad handler
    /// used to abort the caller part-way through a sequence it had to finish, leaving the caller's own
    /// state disagreeing with what had already been written.
    /// </summary>
    private void RaiseChanged()
    {
        var subscribers = Changed;
        if (subscribers is null)
        {
            return;
        }

        foreach (var subscriber in subscribers.GetInvocationList())
        {
            try
            {
                ((EventHandler)subscriber)(this, EventArgs.Empty);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "a settings Changed subscriber threw; the remaining subscribers still ran");
            }
        }
    }
}
