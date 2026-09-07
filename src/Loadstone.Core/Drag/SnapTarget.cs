using Loadstone.Core.Geometry;

namespace Loadstone.Core.Drag;

/// <summary>What a drag will snap to: the tile, and the display whose work area it is laid into.</summary>
public readonly record struct SnapTarget(Tile Tile, Display Display);
