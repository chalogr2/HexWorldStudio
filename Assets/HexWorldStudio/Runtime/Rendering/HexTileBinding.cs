using HexWorldStudio.Coordinates;
using UnityEngine;

namespace HexWorldStudio.Rendering
{
    /// <summary>
    /// Per-tile-GameObject component carrying the canonical
    /// <see cref="HexCoord"/> the tile represents (ADR-0016).
    /// Attached by <c>WorldRenderer</c> on every tile during build;
    /// survives <see cref="UnityEngine.Object.Instantiate"/> on
    /// wrap-copy tiles because <see cref="Coord"/> is plain
    /// serialized state.
    /// </summary>
    /// <remarks>
    /// Picking code reads <see cref="Coord"/> off the raycast-hit
    /// GameObject to resolve a tile in constant time without parsing
    /// the GameObject name or doing offset math. Buyers extending the
    /// renderer can attach the same component on their custom tile
    /// objects to plug into the demo's interaction layer.
    /// </remarks>
    public sealed class HexTileBinding : MonoBehaviour
    {
        /// <summary>
        /// The canonical axial coordinate this tile represents. On
        /// wrap copies this is the same value as the canonical
        /// source tile — wrap is handled by <c>WorldWrapRenderer</c>
        /// at the transform level, not by varying the coord stored
        /// here.
        /// </summary>
        public HexCoord Coord;
    }
}
