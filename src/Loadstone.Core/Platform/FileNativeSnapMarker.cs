using System.Security;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace Loadstone.Core.Platform;

/// <summary>
/// The crash marker in its own small file beside settings.json.
/// </summary>
/// <remarks>
/// Its own file, not a settings key: the marker is written on every suppress and restore, and while
/// it lived in the settings document each of those writes raised
/// <see cref="Loadstone.Core.Settings.ISettingsService.Changed"/> and re-entered every subscriber.
/// The file is not documented for users. A stale one costs the user nothing only because of the
/// guard on the write that reads it: a parked value goes back to a flag that still holds the value
/// Loadstone left there, and a flag the user has turned on since is left at their setting.
/// </remarks>
public sealed class FileNativeSnapMarker : INativeSnapMarker
{
    public static string DefaultPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Loadstone", "native-snap-marker.json");

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        AllowTrailingCommas = true,
    };

    private readonly string _path;
    private readonly ILogger<FileNativeSnapMarker> _logger;

    public FileNativeSnapMarker(string path, ILogger<FileNativeSnapMarker> logger)
    {
        _path = path;
        _logger = logger;
    }

    /// <summary>
    /// Never throws: a marker that cannot be read costs the user this launch's recovery, and throwing
    /// would cost them the launch. An unreadable file is not reported as "nothing parked" — that
    /// answer is what lets the next capture overwrite it with the flags the crashed run left off.
    /// </summary>
    public MarkerRead Read()
    {
        if (!File.Exists(_path))
        {
            return MarkerRead.Missing;
        }

        try
        {
            var parsed = JsonSerializer.Deserialize<Dictionary<string, bool>>(File.ReadAllText(_path), JsonOptions);
            if (parsed is null)
            {
                _logger.LogError("the native snap marker at {Path} could not be read; leaving it for the next launch", _path);
                return MarkerRead.Failed;
            }

            return new MarkerRead(parsed, false);
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException or SecurityException)
        {
            _logger.LogError(ex, "the native snap marker at {Path} could not be read; leaving it for the next launch", _path);
            return MarkerRead.Failed;
        }
    }

    /// <summary>
    /// Atomic write: serialize to a temp file, then move it over the target, so a crash mid-write
    /// leaves the previous marker rather than a half-written one.
    /// </summary>
    public bool Write(IReadOnlyDictionary<string, bool>? values)
    {
        try
        {
            if (values is null)
            {
                if (File.Exists(_path))
                {
                    File.Delete(_path);
                }

                return true;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            var temp = _path + ".tmp";
            // Copied, not serialized in place: the caller keeps its dictionary and goes on using it.
            File.WriteAllText(temp, JsonSerializer.Serialize(new Dictionary<string, bool>(values), JsonOptions));
            File.Move(temp, _path, overwrite: true);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SecurityException)
        {
            _logger.LogError(ex, "the native snap marker at {Path} could not be written", _path);
            return false;
        }
    }
}
