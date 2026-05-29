using HexWorldStudio.Content;
using HexWorldStudio.Coordinates;

namespace HexWorldStudio.Data
{
    /// <summary>
    /// Land / water state of a tile, set by the continent-shape stage.
    /// </summary>
    /// <remarks>
    /// Default is <see cref="Unset"/> so any read of <see cref="HexTile.LandState"/>
    /// before generation has classified the tile surfaces loudly as
    /// <c>Unset</c> rather than being silently misinterpreted as water.
    /// This matches the project's preference for NaN-style "did the stage
    /// forget to set this?" diagnostics on <see cref="HexTile.Elevation"/>,
    /// <see cref="HexTile.Temperature"/>, and <see cref="HexTile.Moisture"/>.
    /// </remarks>
    public enum TileLandState
    {
        /// <summary>The continent-shape stage has not classified this tile yet.</summary>
        Unset = 0,

        /// <summary>The tile is water (ocean, sea, or lake).</summary>
        Water = 1,

        /// <summary>The tile is land.</summary>
        Land = 2,
    }

    /// <summary>
    /// A single hex cell in a <c>WorldMap</c>. Class (reference type, per
    /// ADR-0004), so buyers can hold references, attach gameplay data via
    /// composition, and pass tiles into their own code without surprising
    /// copy semantics.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Coordinate (<see cref="Coord"/>) and tile index (<see cref="TileIndex"/>)
    /// are immutable after construction. Per-tile state fields
    /// (<see cref="Elevation"/>, <see cref="Temperature"/>,
    /// <see cref="Moisture"/>, <see cref="LandState"/>) are publicly
    /// readable and have <c>internal</c> setters so generation stages
    /// (which live in the same assembly) can mutate them. Buyers may
    /// mutate them after generation; see ADR-0005 and the
    /// <c>Documentation/Concepts/DataModelContract.md</c> reference for
    /// what is safe to change.
    /// </para>
    /// <para>
    /// Float fields default to <see cref="float.NaN"/> rather than zero
    /// so that a missing stage produces loud, immediately-visible NaN
    /// values in diagnostic overlays rather than silently plausible
    /// zeros. <see cref="LandState"/> defaults to
    /// <see cref="TileLandState.Unset"/> for the same reason.
    /// </para>
    /// </remarks>
    public sealed class HexTile
    {
        /// <summary>
        /// The tile's axial coordinate inside its owning map. Canonical
        /// (Q in <c>[0, Width)</c> via the map's odd-r layout) and stable
        /// for the tile's lifetime.
        /// </summary>
        public HexCoord Coord { get; }

        /// <summary>
        /// The tile's index into <c>WorldMap.Tiles</c>. Stable and
        /// contiguous: tiles are laid out in raster order
        /// (<c>row * Width + col</c>).
        /// </summary>
        public int TileIndex { get; }

        /// <summary>
        /// Normalized elevation in <c>[0, 1]</c>: 0 = sea-floor /
        /// abyssal, 1 = highest mountain peak. Defaults to
        /// <see cref="float.NaN"/> until the elevation stage runs.
        /// </summary>
        public float Elevation { get; internal set; } = float.NaN;

        /// <summary>
        /// Normalized temperature in <c>[0, 1]</c>: 0 = polar / coldest,
        /// 1 = equatorial / hottest. Defaults to <see cref="float.NaN"/>
        /// until the climate stage runs.
        /// </summary>
        public float Temperature { get; internal set; } = float.NaN;

        /// <summary>
        /// Normalized moisture in <c>[0, 1]</c>: 0 = driest, 1 = wettest.
        /// Defaults to <see cref="float.NaN"/> until the climate stage runs.
        /// </summary>
        public float Moisture { get; internal set; } = float.NaN;

        /// <summary>
        /// Land / water classification. Defaults to
        /// <see cref="TileLandState.Unset"/> until the continent-shape
        /// stage runs.
        /// </summary>
        public TileLandState LandState { get; internal set; } = TileLandState.Unset;

        /// <summary>
        /// Discrete elevation step used by gameplay traversal rules
        /// (default: a unit can move only to a tile within ±1 of its
        /// current <see cref="ElevationLevel"/>; buyer code may
        /// override). Set by the elevation-level quantization stage
        /// after the continuous <see cref="Elevation"/> field is
        /// populated.
        /// </summary>
        /// <remarks>
        /// Defaults to <c>-1</c> (unset sentinel) — matches the NaN
        /// convention on float fields so any read before the
        /// quantization stage has run is immediately recognizable. The
        /// valid range after generation is <c>[0, N-1]</c> where N is
        /// the per-map elevation-level count (default 6).
        /// </remarks>
        public int ElevationLevel { get; internal set; } = -1;

        /// <summary>
        /// The biome assigned to this tile by
        /// <c>BiomeAssignmentStage</c>. References a
        /// <see cref="BiomeDefinition"/> ScriptableObject from
        /// <c>WorldGenerationSettings.Biomes</c>.
        /// </summary>
        /// <remarks>
        /// Defaults to <c>null</c> until biome assignment runs. After
        /// the default pipeline completes, every tile carries a
        /// non-null biome reference.
        /// </remarks>
        public BiomeDefinition Biome { get; internal set; }

        /// <summary>
        /// The tile-feature assigned to this tile by
        /// <c>TileFeaturePlacementStage</c>. References a
        /// <see cref="TileFeatureDefinition"/> drawn from the tile's
        /// biome's <see cref="BiomeDefinition.AllowedFeatures"/> list
        /// per ADR-0014.
        /// </summary>
        /// <remarks>
        /// Defaults to <c>null</c>. After the default pipeline
        /// completes, every land tile carries a non-null feature
        /// (water tiles remain <c>null</c> since water biomes have
        /// no <c>AllowedFeatures</c>).
        /// </remarks>
        public TileFeatureDefinition Feature { get; internal set; }

        /// <summary>
        /// Constructs a tile bound to a specific coordinate and index.
        /// Called by <c>WorldMap</c> during construction; not used by
        /// buyers directly.
        /// </summary>
        /// <param name="coord">
        /// The canonical axial coordinate this tile occupies.
        /// </param>
        /// <param name="tileIndex">
        /// The tile's slot in <c>WorldMap.Tiles</c>.
        /// </param>
        internal HexTile(HexCoord coord, int tileIndex)
        {
            Coord = coord;
            TileIndex = tileIndex;
        }
    }
}
