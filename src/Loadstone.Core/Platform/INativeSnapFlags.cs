namespace Loadstone.Core.Platform;

/// <summary>Windows' edge-snapping flags, one at a time. The UI implements it over SystemParametersInfo.</summary>
public interface INativeSnapFlags
{
    /// <summary>The flag's current value, or null when it could not be read.</summary>
    bool? Read(NativeSnapFlag flag);

    /// <summary>Writes the flag so that it persists and Explorer is told. False when the write failed.</summary>
    bool Write(NativeSnapFlag flag, bool value);
}
