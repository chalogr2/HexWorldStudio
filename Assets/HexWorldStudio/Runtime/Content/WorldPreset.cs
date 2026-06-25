using HexWorldStudio.Generation;
using UnityEngine;

namespace HexWorldStudio.Content
{
    /// <summary>
    /// A reusable bundle of generation settings — dimensions, land
    /// ratio, continent-shape algorithm choice, cohesion sliders, and
    /// biome content roster — saved as a ScriptableObject so buyers
    /// can author, share, and swap whole "world types" without
    /// editing fields one at a time on the bootstrap.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A preset deliberately does <b>not</b> carry the RNG
    /// <see cref="WorldGenerationSettings.Seed"/>. The seed is the
    /// per-generation randomness (one preset, many seeded worlds);
    /// keeping it out of the preset keeps the abstraction clean —
    /// the preset describes "what kind of world", the seed picks an
    /// instance. The bootstrap supplies the seed at
    /// <see cref="ToSettings"/> time.
    /// </para>
    /// <para>
    /// Default values mirror <see cref="WorldGenerationSettings"/>'s
    /// constants (<c>DefaultLandRatio</c>, <c>DefaultBiomeCohesion</c>,
    /// <c>DefaultFeatureCohesion</c>, <c>DefaultContinentShape</c>) so
    /// a freshly created preset asset already produces a runnable
    /// world once a biome roster is wired up. The biome roster has no
    /// inherent default — buyers either drag in the seven shipped
    /// biome assets (Ocean, Coast, Snow, Tundra, Arid, Temperate,
    /// Tropical) or their own.
    /// </para>
    /// <para>
    /// Several worked-example presets ship under
    /// <c>Assets/HexWorldStudio/Content/Presets/</c>: <c>Earthlike</c>
    /// (the default — Continents algorithm at 0.4 land ratio),
    /// <c>Pangaea</c>, <c>Archipelago</c>, <c>InlandSea</c>. Buyers
    /// duplicate-and-edit those to author their own world types or
    /// build new ones from scratch via Create → Hex World Studio →
    /// World Preset. See ADR-0021 for the design rationale.
    /// </para>
    /// </remarks>
    [CreateAssetMenu(fileName = "WorldPreset", menuName = "Hex World Studio/World Preset")]
    public sealed class WorldPreset : ScriptableObject
    {
        [Header("Size")]

        /// <summary>Number of columns in the generated map.</summary>
        [Tooltip("Number of columns in the generated map. Validated at WorldGenerationSettings.Generate (must be positive).")]
        public int Width = 32;

        /// <summary>Number of rows in the generated map.</summary>
        [Tooltip("Number of rows in the generated map. Validated at WorldGenerationSettings.Generate (must be positive).")]
        public int Height = 20;

        [Header("Continent Shape")]

        /// <summary>
        /// Target proportion of land tiles in <c>(0, 1)</c>. The
        /// continent-shape algorithm picks a percentile threshold so
        /// the realized land count matches within one tile.
        /// </summary>
        [Range(0.05f, 0.95f)]
        [Tooltip("Target proportion of land tiles. Each continent-shape algorithm has a recommended range — see ContinentShapeAlgorithm enum docs.")]
        public float LandRatio = WorldGenerationSettings.DefaultLandRatio;

        /// <summary>
        /// Which continent-shape algorithm fills the
        /// <c>ContinentShapeStage</c> pipeline slot. See
        /// <see cref="ContinentShapeAlgorithm"/>.
        /// </summary>
        [Tooltip("Continent-shape algorithm. Continents = default. Pangaea = one supercontinent. " +
                 "Archipelago = many small islands. Fractal = irregular coastlines. " +
                 "InlandSea = land ring. Lakes = mostly land. Shuffle = RNG-picks one.")]
        public ContinentShapeAlgorithm ContinentShapeAlgorithm = WorldGenerationSettings.DefaultContinentShape;

        [Header("Cohesion (ADR-0019)")]

        /// <summary>
        /// Biome cohesion strength in <c>[0, 1]</c>. 0 = raw
        /// classification; 0.5 = Civ-like mild blobs (recommended);
        /// 1 = strong smoothing. See ADR-0019.
        /// </summary>
        [Range(0f, 1f)]
        [Tooltip("How aggressively neighboring tiles flip toward consensus to form coherent biome regions. " +
                 "0 = raw classification (speckled). 0.5 = Civ-like mild blobs (recommended). " +
                 "1 = strong smoothing.")]
        public float BiomeCohesion = WorldGenerationSettings.DefaultBiomeCohesion;

        /// <summary>
        /// Feature cohesion strength in <c>[0, 1]</c>. Defaulted
        /// lower than biome cohesion so feature variety inside a
        /// biome reads as natural diversity.
        /// </summary>
        [Range(0f, 1f)]
        [Tooltip("How aggressively neighboring tiles flip toward feature consensus. " +
                 "Defaulted lower than biome cohesion so feature variety inside a biome reads as natural diversity.")]
        public float FeatureCohesion = WorldGenerationSettings.DefaultFeatureCohesion;

        [Header("Content")]

        /// <summary>
        /// Biome roster passed to <see cref="WorldGenerationSettings.Biomes"/>.
        /// Must contain at least one Ocean-style biome
        /// (<c>IsWater &amp;&amp; !IsCoastalWater</c>), one Coast-style
        /// biome (<c>IsWater &amp;&amp; IsCoastalWater</c>), and one
        /// land biome. Validated at <c>HexWorldGenerator.Generate</c>.
        /// </summary>
        [Tooltip("Biome content set. Must include at least one Ocean, one Coast, and one land biome. " +
                 "Drag in the seven shipped .asset files or your own.")]
        public BiomeDefinition[] Biomes;

        [Header("Resources (optional — ADR-0022)")]

        /// <summary>
        /// Optional resource roster. Empty or <c>null</c> = no
        /// resources placed (the <c>ResourcePlacementStage</c> no-ops).
        /// Populate to opt in.
        /// </summary>
        [Tooltip("Optional resource roster. Empty = no resources. Populate to enable resource placement.")]
        public ResourceDefinition[] Resources;

        /// <summary>
        /// Per-tile placement probability in <c>[0, 1]</c>. Default
        /// matches <see cref="WorldGenerationSettings.DefaultResourceDensity"/>.
        /// </summary>
        [Range(0f, 1f)]
        [Tooltip("Per-tile resource placement probability. 0 = no resources. 0.2 = Civ-style default. 1 = attempt every tile.")]
        public float ResourceDensity = WorldGenerationSettings.DefaultResourceDensity;

        /// <summary>
        /// Bundles this preset's fields together with a per-generation
        /// <paramref name="seed"/> into a fresh
        /// <see cref="WorldGenerationSettings"/> ready to hand to
        /// <c>HexWorldGenerator.Generate</c>.
        /// </summary>
        /// <param name="seed">
        /// The RNG seed for this generation. Presets describe the
        /// world type; the seed picks the instance. Same preset +
        /// same seed → byte-identical world.
        /// </param>
        public WorldGenerationSettings ToSettings(ulong seed)
        {
            return new WorldGenerationSettings(
                Width, Height, seed, LandRatio, Biomes,
                BiomeCohesion, FeatureCohesion,
                ContinentShapeAlgorithm,
                Resources, ResourceDensity);
        }
    }
}
