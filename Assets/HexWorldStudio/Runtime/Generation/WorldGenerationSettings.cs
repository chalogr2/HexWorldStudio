using System;
using System.Collections.Generic;
using HexWorldStudio.Content;

namespace HexWorldStudio.Generation
{
    /// <summary>
    /// Inputs to a single world generation run. Plain data class:
    /// constructors store values without validation so settings can be
    /// built incrementally (e.g., from inspector fields whose
    /// intermediate states may be invalid). All validation happens at
    /// the <see cref="HexWorldGenerator.Generate"/> boundary where it
    /// could actually corrupt something.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Marked <see cref="SerializableAttribute"/> so buyers can embed
    /// instances in inspector-edited classes or ScriptableObject
    /// content (e.g., <c>WorldPreset</c> SOs landing in a later slice).
    /// </para>
    /// <para>
    /// This class is reference-type; two instances constructed with the
    /// same values are still distinct objects and compare unequal under
    /// the default <c>Equals</c>. Use them as configuration containers,
    /// not as map identifiers.
    /// </para>
    /// <para>
    /// Algorithm selection (which <c>IGenerationStage</c> implementation
    /// fills each pipeline slot, per ADR-0010) and content selection
    /// (which <c>BiomeDefinition</c> set is active, per ADR-0007 and
    /// ADR-0011) are not yet exposed on <see cref="WorldGenerationSettings"/>;
    /// fields for those land alongside the slices that introduce the
    /// corresponding pluggable stages and SOs.
    /// </para>
    /// </remarks>
    [Serializable]
    public sealed class WorldGenerationSettings
    {
        /// <summary>
        /// Default land ratio when the 3-argument constructor is used.
        /// Roughly Civ-style: 40% land, 60% water.
        /// </summary>
        public const float DefaultLandRatio = 0.4f;

        /// <summary>
        /// Default biome cohesion strength. 0.5 produces Civ-like mild
        /// blobs out of the box without erasing legitimate biome
        /// boundaries. See <see cref="BiomeCohesion"/> and ADR-0019.
        /// </summary>
        public const float DefaultBiomeCohesion = 0.5f;

        /// <summary>
        /// Default feature cohesion strength. Lower than the biome
        /// default — feature variety inside a biome reads as natural
        /// diversity, so we only collapse single-tile outliers at the
        /// default setting. See <see cref="FeatureCohesion"/> and
        /// ADR-0019.
        /// </summary>
        public const float DefaultFeatureCohesion = 0.3f;

        /// <summary>
        /// Default continent-shape algorithm. Mirrors the historical
        /// Slice-5 behavior: cylindrical Perlin percentile threshold
        /// producing several medium-sized landmasses. See
        /// <see cref="ContinentShapeAlgorithm"/> and ADR-0020.
        /// </summary>
        public const ContinentShapeAlgorithm DefaultContinentShape = ContinentShapeAlgorithm.Continents;

        /// <summary>
        /// Default per-tile probability of attempting a resource
        /// placement when the resource roster is non-empty. Civ-style
        /// density — roughly one-in-five eligible tiles attempts a
        /// placement, with spacing constraints filtering further. See
        /// ADR-0022.
        /// </summary>
        public const float DefaultResourceDensity = 0.2f;

        /// <summary>The map's width in tiles (number of columns).</summary>
        public int Width { get; }

        /// <summary>The map's height in tiles (number of rows).</summary>
        public int Height { get; }

        /// <summary>
        /// The 64-bit seed feeding the deterministic RNG service.
        /// Identical settings and identical seeds produce a byte-
        /// identical <c>WorldMap</c>.
        /// </summary>
        public ulong Seed { get; }

        /// <summary>
        /// Target proportion of land tiles in <c>(0, 1)</c>. The
        /// default <c>ContinentShapeStage</c> uses a percentile-based
        /// threshold so the resulting land ratio matches this value
        /// to within one tile of the theoretical count.
        /// </summary>
        /// <remarks>
        /// Validated at <see cref="HexWorldGenerator.Generate"/> rather
        /// than here (intermediate invalid values are allowed during
        /// inspector-driven configuration). Alternate continent
        /// algorithms (per ADR-0010) may interpret this field
        /// differently or ignore it.
        /// </remarks>
        public float LandRatio { get; }

        /// <summary>
        /// The biome content set used by <c>BiomeAssignmentStage</c>.
        /// Buyers add, remove, or replace biomes by passing a different
        /// list — the system has no hardcoded biome assumptions. Only
        /// structural requirements (≥1 water, ≥1 coastal water, ≥1
        /// non-water) are enforced at <c>Generate</c>.
        /// </summary>
        /// <remarks>
        /// The list is stored by reference (no defensive copy) so that
        /// buyer-side reload / hot-edit flows work — mutations to the
        /// list mid-generation produce undefined behavior, as do
        /// generation flows in general.
        /// </remarks>
        public IReadOnlyList<BiomeDefinition> Biomes { get; }

        /// <summary>
        /// Strength of post-assignment biome cohesion smoothing in
        /// <c>[0, 1]</c>. Drives <c>BiomeCohesionStage</c> (ADR-0019):
        /// at 0 the stage is a no-op and biomes are placed exactly as
        /// the climate envelope dictates; higher values progressively
        /// snap outlier tiles to their neighbors' majority biome,
        /// producing the coherent regions buyers expect from Civ-style
        /// worlds. The mapping from this slider to (iterations,
        /// threshold) lives in <c>BiomeCohesionStage.FromStrength</c>.
        /// </summary>
        public float BiomeCohesion { get; }

        /// <summary>
        /// Strength of post-placement feature cohesion smoothing in
        /// <c>[0, 1]</c>. Drives <c>FeatureCohesionStage</c>
        /// (ADR-0019) using the same mapping as
        /// <see cref="BiomeCohesion"/>. The default is set lower than
        /// <see cref="BiomeCohesion"/> because feature variety inside
        /// a biome is desirable; only single-tile outliers should be
        /// smoothed at the default setting.
        /// </summary>
        public float FeatureCohesion { get; }

        /// <summary>
        /// Which continent-shape algorithm fills the
        /// <c>ContinentShapeStage</c> pipeline slot. See
        /// <see cref="ContinentShapeAlgorithm"/> for the catalog of
        /// shipped algorithms (Continents, Pangaea, Archipelago,
        /// Fractal, InlandSea, Lakes, Shuffle). Default
        /// <see cref="DefaultContinentShape"/> preserves the
        /// historical Slice-5 behavior so existing seeds map to the
        /// same world unless the algorithm is explicitly changed.
        /// </summary>
        public ContinentShapeAlgorithm ContinentShapeAlgorithm { get; }

        /// <summary>
        /// Optional resource roster passed to
        /// <c>ResourcePlacementStage</c>. Empty or <c>null</c> = no
        /// resources are placed (the stage no-ops; see ADR-0022).
        /// Each entry is a <see cref="ResourceDefinition"/> describing
        /// what biome / feature / elevation it requires and how
        /// frequently it spawns.
        /// </summary>
        /// <remarks>
        /// Stored by reference (no defensive copy) so buyer-side
        /// hot-edit flows work. Mutating the list mid-generation is
        /// undefined behavior, same as the biome roster contract.
        /// </remarks>
        public IReadOnlyList<ResourceDefinition> Resources { get; }

        /// <summary>
        /// Per-tile probability of attempting a resource placement in
        /// <c>[0, 1]</c>. <c>0</c> disables placement even when the
        /// resource roster is populated; <c>1</c> attempts placement
        /// on every tile (the actual placement rate is still bounded
        /// by per-resource eligibility filters and spacing). Default
        /// <see cref="DefaultResourceDensity"/> approximates Civ-style
        /// resource density.
        /// </summary>
        public float ResourceDensity { get; }

        /// <summary>
        /// Constructs a settings instance with the default
        /// <see cref="LandRatio"/> and an empty biome set. Generation
        /// will fail at <c>HexWorldGenerator.Generate</c> because the
        /// biome set is empty; use a constructor overload that takes
        /// a populated <c>biomes</c> list to produce a runnable
        /// configuration.
        /// </summary>
        public WorldGenerationSettings(int width, int height, ulong seed)
            : this(width, height, seed, DefaultLandRatio, Array.Empty<BiomeDefinition>())
        {
        }

        /// <summary>
        /// Constructs a settings instance with an explicit
        /// <paramref name="landRatio"/> and an empty biome set.
        /// Generation will fail at <c>HexWorldGenerator.Generate</c>
        /// because the biome set is empty.
        /// </summary>
        public WorldGenerationSettings(int width, int height, ulong seed, float landRatio)
            : this(width, height, seed, landRatio, Array.Empty<BiomeDefinition>())
        {
        }

        /// <summary>
        /// Constructs a settings instance with explicit dimensions,
        /// seed, land ratio, and biome content; cohesion sliders use
        /// their defaults (<see cref="DefaultBiomeCohesion"/> /
        /// <see cref="DefaultFeatureCohesion"/>).
        /// </summary>
        /// <param name="width">Number of columns. Must be positive at generation time.</param>
        /// <param name="height">Number of rows. Must be positive at generation time.</param>
        /// <param name="seed">The 64-bit RNG seed.</param>
        /// <param name="landRatio">
        /// Target proportion of land tiles. Must be strictly between
        /// 0 and 1 at generation time.
        /// </param>
        /// <param name="biomes">
        /// Biome content set. Must be non-empty and contain at least
        /// one Ocean-style biome (IsWater &amp;&amp; !IsCoastalWater),
        /// one Coast-style biome (IsWater &amp;&amp; IsCoastalWater),
        /// and one land biome, all validated at
        /// <c>HexWorldGenerator.Generate</c>.
        /// </param>
        public WorldGenerationSettings(int width, int height, ulong seed, float landRatio,
                                       IReadOnlyList<BiomeDefinition> biomes)
            : this(width, height, seed, landRatio, biomes,
                   DefaultBiomeCohesion, DefaultFeatureCohesion)
        {
        }

        /// <summary>
        /// Constructs a settings instance with explicit cohesion
        /// values; the continent-shape algorithm uses its default
        /// (<see cref="DefaultContinentShape"/>).
        /// </summary>
        /// <param name="width">Number of columns. Must be positive at generation time.</param>
        /// <param name="height">Number of rows. Must be positive at generation time.</param>
        /// <param name="seed">The 64-bit RNG seed.</param>
        /// <param name="landRatio">
        /// Target proportion of land tiles. Must be strictly between
        /// 0 and 1 at generation time.
        /// </param>
        /// <param name="biomes">
        /// Biome content set; same structural requirements as the
        /// shorter constructor overload.
        /// </param>
        /// <param name="biomeCohesion">
        /// Strength of post-assignment biome smoothing in
        /// <c>[0, 1]</c>. See <see cref="BiomeCohesion"/>.
        /// </param>
        /// <param name="featureCohesion">
        /// Strength of post-placement feature smoothing in
        /// <c>[0, 1]</c>. See <see cref="FeatureCohesion"/>.
        /// </param>
        public WorldGenerationSettings(int width, int height, ulong seed, float landRatio,
                                       IReadOnlyList<BiomeDefinition> biomes,
                                       float biomeCohesion, float featureCohesion)
            : this(width, height, seed, landRatio, biomes,
                   biomeCohesion, featureCohesion, DefaultContinentShape)
        {
        }

        /// <summary>
        /// Constructs a fully-specified settings instance including
        /// the cohesion sliders and the continent-shape algorithm
        /// selection.
        /// </summary>
        /// <param name="width">Number of columns. Must be positive at generation time.</param>
        /// <param name="height">Number of rows. Must be positive at generation time.</param>
        /// <param name="seed">The 64-bit RNG seed.</param>
        /// <param name="landRatio">
        /// Target proportion of land tiles. Must be strictly between
        /// 0 and 1 at generation time.
        /// </param>
        /// <param name="biomes">
        /// Biome content set; same structural requirements as the
        /// shorter constructor overload.
        /// </param>
        /// <param name="biomeCohesion">
        /// Strength of post-assignment biome smoothing in
        /// <c>[0, 1]</c>. See <see cref="BiomeCohesion"/>.
        /// </param>
        /// <param name="featureCohesion">
        /// Strength of post-placement feature smoothing in
        /// <c>[0, 1]</c>. See <see cref="FeatureCohesion"/>.
        /// </param>
        /// <param name="continentShapeAlgorithm">
        /// Which continent-shape algorithm fills the
        /// <c>ContinentShapeStage</c> pipeline slot. See
        /// <see cref="ContinentShapeAlgorithm"/>.
        /// </param>
        public WorldGenerationSettings(int width, int height, ulong seed, float landRatio,
                                       IReadOnlyList<BiomeDefinition> biomes,
                                       float biomeCohesion, float featureCohesion,
                                       ContinentShapeAlgorithm continentShapeAlgorithm)
            : this(width, height, seed, landRatio, biomes,
                   biomeCohesion, featureCohesion, continentShapeAlgorithm,
                   resources: null, resourceDensity: DefaultResourceDensity)
        {
        }

        /// <summary>
        /// Full constructor including the optional resource roster.
        /// Resource placement is opt-in per ADR-0022: when
        /// <paramref name="resources"/> is null or empty, the
        /// <c>ResourcePlacementStage</c> is a no-op.
        /// </summary>
        /// <param name="width">Number of columns. Must be positive.</param>
        /// <param name="height">Number of rows. Must be positive.</param>
        /// <param name="seed">The 64-bit RNG seed.</param>
        /// <param name="landRatio">Target land proportion in <c>(0, 1)</c>.</param>
        /// <param name="biomes">Biome content set; structural requirements per the shorter ctor.</param>
        /// <param name="biomeCohesion">Biome smoothing strength in <c>[0, 1]</c>.</param>
        /// <param name="featureCohesion">Feature smoothing strength in <c>[0, 1]</c>.</param>
        /// <param name="continentShapeAlgorithm">Continent-shape algorithm choice.</param>
        /// <param name="resources">
        /// Resource content set. <c>null</c> or empty = no resources
        /// placed.
        /// </param>
        /// <param name="resourceDensity">
        /// Per-tile placement probability in <c>[0, 1]</c>. <c>0</c>
        /// disables placement even when resources is non-empty.
        /// </param>
        public WorldGenerationSettings(int width, int height, ulong seed, float landRatio,
                                       IReadOnlyList<BiomeDefinition> biomes,
                                       float biomeCohesion, float featureCohesion,
                                       ContinentShapeAlgorithm continentShapeAlgorithm,
                                       IReadOnlyList<ResourceDefinition> resources,
                                       float resourceDensity)
        {
            Width = width;
            Height = height;
            Seed = seed;
            LandRatio = landRatio;
            Biomes = biomes;
            BiomeCohesion = biomeCohesion;
            FeatureCohesion = featureCohesion;
            ContinentShapeAlgorithm = continentShapeAlgorithm;
            Resources = resources;
            ResourceDensity = resourceDensity;
        }
    }
}
