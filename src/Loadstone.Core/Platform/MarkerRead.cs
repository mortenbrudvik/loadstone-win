namespace Loadstone.Core.Platform;

/// <summary>
/// What <see cref="INativeSnapMarker.Read"/> found.
/// </summary>
/// <remarks>
/// <see cref="Unreadable"/> is not the same as nothing parked. The store exists and could not be
/// parsed or opened, and replacing it would record the flags a crashed run left off as the user's
/// values. Callers leave that file for the next launch.
/// </remarks>
public readonly record struct MarkerRead(IReadOnlyDictionary<string, bool>? Values, bool Unreadable)
{
    public static MarkerRead Missing => new(null, false);

    public static MarkerRead Failed => new(null, true);
}
