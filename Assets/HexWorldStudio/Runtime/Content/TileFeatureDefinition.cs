using UnityEngine;

namespace HexWorldStudio.Content
{
    /// <summary>
    /// One terrain detail that can be placed on a tile in addition to
    /// its <see cref="BiomeDefinition"/> — forest, hill, mountain,
    /// grassland, etc.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Authored as a <see cref="ScriptableObject"/> so buyers add,
    /// remove, or replace features purely from the inspector with no
    /// engine-code changes (per ADR-0011 and ADR-0014).
    /// </para>
    /// <para>
    /// Eligibility is controlled by each
    /// <see cref="BiomeDefinition.AllowedFeatures"/> array — a feature
    /// only appears on a tile whose biome lists it. Within the
    /// allow-list, the default <c>TileFeaturePlacementStage</c> uses
    /// <see cref="PreferredElevationLevel"/> and <see cref="Weight"/>
    /// to score features against the tile's <c>ElevationLevel</c>;
    /// the Gaussian falloff means features with a closer preferred
    /// level dominate without producing hard bands.
    /// </para>
    /// <para>
    /// <see cref="IsImpassable"/> and
    /// <see cref="MovementCostMultiplier"/> are exposed as buyer-facing
    /// metadata that gameplay code can read; the generator itself does
    /// not consume them.
    /// </para>
    /// </remarks>
    [CreateAssetMenu(fileName = "TileFeature", menuName = "Hex World Studio/Tile Feature Definition")]
    public sealed class TileFeatureDefinition : ScriptableObject
    {
        /// <summary>Human-readable name for diagnostics, overlays, and tooling.</summary>
        public string FeatureName;

        /// <summary>
        /// Display color used by the default renderer when this feature
        /// is assigned to a tile. Overrides the biome color through the
        /// per-tile <c>MaterialPropertyBlock</c>.
        /// </summary>
        public Color Color = Color.magenta;

        /// <summary>
        /// Discrete elevation level this feature naturally prefers. The
        /// placement stage scores the feature higher on tiles whose
        /// own <c>ElevationLevel</c> is close to this value.
        /// </summary>
        public int PreferredElevationLevel = 1;

        /// <summary>
        /// Base weight applied before the elevation-distance falloff.
        /// Higher weight → feature appears more often relative to its
        /// peers within the same biome. Must be non-negative.
        /// </summary>
        public float Weight = 1f;

        /// <summary>
        /// Buyer-facing gameplay metadata: <c>true</c> if units cannot
        /// enter tiles carrying this feature by default. The generator
        /// does not consume this field; gameplay code does.
        /// </summary>
        public bool IsImpassable;

        /// <summary>
        /// Buyer-facing gameplay metadata: multiplier applied to
        /// movement cost when entering a tile carrying this feature
        /// (1.0 = normal, 2.0 = takes twice as long). The generator
        /// does not consume this field; gameplay code does.
        /// </summary>
        public float MovementCostMultiplier = 1f;
    }
}
