using UnityEngine;

namespace HexWorldStudio.Content
{
    /// <summary>
    /// A single climate-band biome definition: the data needed to
    /// classify a tile (climate envelope, water status) and to render
    /// it (display color).
    /// </summary>
    /// <remarks>
    /// <para>
    /// Authored as a <see cref="ScriptableObject"/> so buyers add,
    /// remove, or replace biomes purely from the inspector with no
    /// engine-code changes (per ADR-0007 and ADR-0011).
    /// </para>
    /// <para>
    /// The default content set shipped under
    /// <c>Assets/HexWorldStudio/Content/Biomes/</c> covers the seven
    /// Slice-6 starter biomes: Ocean, Coast, Snow, Tundra, Arid,
    /// Temperate, Tropical. Buyers may delete that folder entirely
    /// and ship their own biome set; the only structural requirements
    /// validated at <c>HexWorldGenerator.Generate</c> are: at least
    /// one water biome with <see cref="IsCoastalWater"/> = <c>true</c>
    /// (the Coast role), at least one water biome with
    /// <see cref="IsCoastalWater"/> = <c>false</c> (the Ocean role),
    /// and at least one land biome.
    /// </para>
    /// <para>
    /// Climate envelope semantics: a land tile with temperature in
    /// <c>[MinTemperature, MaxTemperature]</c> and moisture in
    /// <c>[MinMoisture, MaxMoisture]</c> matches this biome. The
    /// <c>BiomeAssignmentStage</c> resolves overlaps by first match
    /// in <c>WorldGenerationSettings.Biomes</c> order, so authoring
    /// order is meaningful — order more specific envelopes before
    /// more general ones. The envelope fields are ignored for water
    /// biomes (<see cref="IsWater"/> = <c>true</c>); water tiles are
    /// classified by neighbor topology, not by climate.
    /// </para>
    /// </remarks>
    [CreateAssetMenu(fileName = "Biome", menuName = "Hex World Studio/Biome Definition")]
    public sealed class BiomeDefinition : ScriptableObject
    {
        /// <summary>Human-readable name for diagnostics, overlays, and tooling.</summary>
        public string BiomeName;

        /// <summary>
        /// Display color used by the default renderer. Buyers may
        /// override per-tile via material swaps or shader logic.
        /// </summary>
        public Color Color = Color.magenta;

        /// <summary>
        /// <c>true</c> if this biome represents a water tile (Ocean
        /// or Coast). Water biomes ignore the climate envelope; they
        /// are assigned by <c>BiomeAssignmentStage</c> based on
        /// neighbor land/water topology.
        /// </summary>
        public bool IsWater;

        /// <summary>
        /// When <see cref="IsWater"/> is true, distinguishes Coast
        /// (<c>true</c>: water tile with at least one land neighbor)
        /// from Ocean (<c>false</c>: water tile with no land neighbors).
        /// Ignored for land biomes.
        /// </summary>
        public bool IsCoastalWater;

        /// <summary>Lower bound of the temperature envelope (inclusive).</summary>
        public float MinTemperature;

        /// <summary>Upper bound of the temperature envelope (inclusive).</summary>
        public float MaxTemperature = 1f;

        /// <summary>Lower bound of the moisture envelope (inclusive).</summary>
        public float MinMoisture;

        /// <summary>Upper bound of the moisture envelope (inclusive).</summary>
        public float MaxMoisture = 1f;

        /// <summary>
        /// Tile features this biome allows. Empty or null for water
        /// biomes (Ocean, Coast); non-empty for land biomes. The
        /// default <c>TileFeaturePlacementStage</c> scores only the
        /// features that appear in this array and assigns one to
        /// every land tile per ADR-0014.
        /// </summary>
        public TileFeatureDefinition[] AllowedFeatures;

        /// <summary>
        /// Optional visual prefab. When non-null, the default
        /// <c>WorldRenderer</c> instantiates this prefab as a child
        /// of every tile that carries this biome (unless the tile's
        /// <see cref="TileFeatureDefinition.Prefab"/> overrides it).
        /// When null, the renderer falls back to its procedural
        /// <c>HexMesh</c> + tinted <c>MaterialPropertyBlock</c> path
        /// for tiles of this biome — no buyer action is required to
        /// get a working visual.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Authoring convention: the prefab's origin sits at the
        /// tile center; the top hex vertex points to <c>+Z</c>
        /// (pointy-top per ADR-0013), matching <c>HexMesh</c>. Bake
        /// desired scale and rotation into the prefab itself — there
        /// is no <c>PrefabScale</c> / <c>PrefabRotation</c> knob on
        /// this SO (per ADR-0017).
        /// </para>
        /// <para>
        /// The renderer always attaches a flat-<c>HexMesh</c>
        /// <see cref="UnityEngine.MeshCollider"/> to the tile root
        /// regardless of which prefab (if any) is used, so picking
        /// (hover / click) works on any prefab without requiring the
        /// prefab to author its own collider.
        /// </para>
        /// </remarks>
        public GameObject Prefab;

        /// <summary>
        /// True when <paramref name="temperature"/> and
        /// <paramref name="moisture"/> both fall inside this biome's
        /// envelope. Always false for water biomes (water tiles match
        /// by topology, not climate).
        /// </summary>
        /// <param name="temperature">Tile temperature in <c>[0, 1]</c>.</param>
        /// <param name="moisture">Tile moisture in <c>[0, 1]</c>.</param>
        public bool Matches(float temperature, float moisture)
        {
            if (IsWater) return false;
            return temperature >= MinTemperature && temperature <= MaxTemperature
                && moisture   >= MinMoisture    && moisture   <= MaxMoisture;
        }
    }
}
