namespace Loadstone.Core.Platform;

/// <summary>One named Windows edge-snapping flag. The UI implements it over SystemParametersInfo.</summary>
public interface INativeSnapSettings
{
    /// <summary>The flag's current value, or null when it could not be read.</summary>
    bool? Read(string flag);

    /// <summary>Writes the flag so that it persists and Explorer is told. False when the write failed.</summary>
    bool Write(string flag, bool value);
}
