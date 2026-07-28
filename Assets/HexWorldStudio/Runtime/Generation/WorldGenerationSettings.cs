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

        /// <summary>
        /// Default per-tile probability of a source-eligible land
        /// tile becoming a river source. Multiplied by tile moisture,
        /// so the effective source rate is
        /// <c>RiverDensity × avgMoisture</c>. Zero disables the
        /// river stage entirely. See <see cref="RiverDensity"/> and
        /// ADR-0024.
        /// </summary>
        public const float DefaultRiverDensity = 0.15f;

        /// <summary>
        /// Minimum <c>ElevationLevel</c> a land tile must have to be
        /// eligible as a river source. Default <c>4</c> (of 6 in the
        /// default quantization) reserves rivers for the upper
        /// highlands and mountains, matching the "rivers come from
        /// high country" hydrology intuition.
        /// </summary>
        /// <remarks>
        /// Lowered from <c>4</c> to <c>3</c> in ADR-0025 because the
        /// meaning changed: it used to gate a single source <i>tile</i>,
        /// and now gates the <i>lowest of the three tiles</i> meeting at
        /// a source corner. Measured on 60×40 worlds, all three tiles
        /// reaching level 4 leaves only ~6% of land corners eligible and
        /// yields under two rivers per map; level 3 yields roughly
        /// eight, which is the Civ-like density the old default was
        /// aiming at.
        /// </remarks>
        public const int DefaultRiverSourceMinElevation = 3;

        /// <summary>
        /// Safety cap on individual river length in steps. Rivers
        /// that hit water or merge before this cap terminate normally;
        /// anything reaching the cap is truncated. Prevents pathological
        /// runaway walks on unusual elevation topologies.
        /// </summary>
        public const int DefaultMaxRiverLength = 40;

        /// <summary>
        /// Default minimum river length, in edges. Walks that commit
        /// fewer steps than this are discarded rather than left as
        /// one- or two-segment stubs, which read as artifacts rather
        /// than rivers. See <see cref="MinRiverLength"/>.
        /// </summary>
        public const int DefaultMinRiverLength = 3;

        /// <summary>
        /// Default maximum waterfall drop, in elevation levels. A river
        /// channel must run between two tiles of equal
        /// <c>ElevationLevel</c>; a level boundary may only be crossed,
        /// as a single-edge waterfall, and never by more than this many
        /// levels at once. See <see cref="MaxWaterfallDrop"/>.
        /// </summary>
        public const int DefaultMaxWaterfallDrop = 1;

        /// <summary>
        /// Default lake depth threshold. A land tile that depression
        /// filling had to raise by more than this much (in normalized
        /// elevation units) is genuinely submerged in a basin and
        /// becomes a lake. See <see cref="LakeDepthThreshold"/>.
        /// </summary>
        /// <remarks>
        /// Measured on 60×40 worlds: <c>0.02</c> produces ~21 separate
        /// lake bodies per map averaging three tiles each, which reads
        /// as speckle rather than lakes. <c>0.08</c> produces ~3.5
        /// bodies of ~4 tiles, which reads as real water. Raise for
        /// fewer, deeper lakes.
        /// </remarks>
        public const float DefaultLakeDepthThreshold = 0.08f;

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
        /// Per-tile probability of a source-eligible land tile
        /// becoming a river source in <c>[0, 1]</c>. <c>0</c>
        /// disables the river stage entirely (opt-out). Multiplied
        /// by tile moisture, so the effective rate is
        /// <c>RiverDensity × moisture</c>; dry tiles are less
        /// likely to spawn rivers than wet ones. See ADR-0024.
        /// </summary>
        public float RiverDensity { get; }

        /// <summary>
        /// Minimum <c>ElevationLevel</c> a tile must have to be
        /// eligible as a river source. Filters low-elevation land
        /// out of the source pool so rivers only start from
        /// highlands and mountains.
        /// </summary>
        public int RiverSourceMinElevation { get; }

        /// <summary>
        /// Safety cap on the number of downhill steps a single
        /// river can take. Rivers that reach water or merge with an
        /// existing river before the cap terminate normally; any
        /// walk that exceeds the cap is truncated.
        /// </summary>
        public int MaxRiverLength { get; }

        /// <summary>
        /// Minimum river length in edges. Walks shorter than this are
        /// discarded (ADR-0025). The terrace constraint makes some
        /// walks strand after a step or two; dropping those keeps
        /// stubs off the map. Defaults to
        /// <see cref="DefaultMinRiverLength"/>.
        /// </summary>
        public int MinRiverLength { get; }

        /// <summary>
        /// Maximum number of elevation levels a single waterfall may
        /// drop (ADR-0025). River channels only ever run between two
        /// tiles of equal <c>ElevationLevel</c>; a terrace boundary is
        /// crossed by exactly one edge, and that crossing may not span
        /// more levels than this. Consecutive crossings are always
        /// forbidden, so a river never staircases down a cliff face.
        /// Defaults to <see cref="DefaultMaxWaterfallDrop"/>.
        /// </summary>
        public int MaxWaterfallDrop { get; }

        /// <summary>
        /// How deeply a tile must sit below its basin's spill level,
        /// in normalized elevation units, before it becomes a lake
        /// (ADR-0025). Lakes are derived from the depression-filled
        /// field rather than from stranded river walks, so they come
        /// out as contiguous bodies of water at a consistent surface
        /// level. Raise for fewer, deeper lakes; set to a large value
        /// to disable lake creation entirely. Defaults to
        /// <see cref="DefaultLakeDepthThreshold"/>.
        /// </summary>
        public float LakeDepthThreshold { get; }

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
        /// Constructor including the optional resource roster; river
        /// settings use their defaults. Resource placement is opt-in
        /// per ADR-0022: when <paramref name="resources"/> is null or
        /// empty, the <c>ResourcePlacementStage</c> is a no-op.
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
            : this(width, height, seed, landRatio, biomes,
                   biomeCohesion, featureCohesion, continentShapeAlgorithm,
                   resources, resourceDensity,
                   DefaultRiverDensity, DefaultRiverSourceMinElevation, DefaultMaxRiverLength)
        {
        }

        /// <summary>
        /// Full constructor including river settings. Rivers are
        /// opt-out per ADR-0024: when
        /// <paramref name="riverDensity"/> is <c>0</c>, the
        /// <c>RiverStage</c> is a no-op and
        /// <see cref="WorldMap.RiverEdges"/> stays empty.
        /// </summary>
        /// <param name="width">Number of columns. Must be positive.</param>
        /// <param name="height">Number of rows. Must be positive.</param>
        /// <param name="seed">The 64-bit RNG seed.</param>
        /// <param name="landRatio">Target land proportion in <c>(0, 1)</c>.</param>
        /// <param name="biomes">Biome content set; structural requirements per the shorter ctor.</param>
        /// <param name="biomeCohesion">Biome smoothing strength in <c>[0, 1]</c>.</param>
        /// <param name="featureCohesion">Feature smoothing strength in <c>[0, 1]</c>.</param>
        /// <param name="continentShapeAlgorithm">Continent-shape algorithm choice.</param>
        /// <param name="resources">Resource content set.</param>
        /// <param name="resourceDensity">Per-tile resource placement probability.</param>
        /// <param name="riverDensity">
        /// Per-source-eligible-tile probability of becoming a river
        /// source in <c>[0, 1]</c>. <c>0</c> disables rivers.
        /// </param>
        /// <param name="riverSourceMinElevation">
        /// Minimum <c>ElevationLevel</c> for a tile to be a river
        /// source candidate.
        /// </param>
        /// <param name="maxRiverLength">
        /// Safety cap on the number of downhill steps a single river
        /// can take.
        /// </param>
        public WorldGenerationSettings(int width, int height, ulong seed, float landRatio,
                                       IReadOnlyList<BiomeDefinition> biomes,
                                       float biomeCohesion, float featureCohesion,
                                       ContinentShapeAlgorithm continentShapeAlgorithm,
                                       IReadOnlyList<ResourceDefinition> resources,
                                       float resourceDensity,
                                       float riverDensity,
                                       int riverSourceMinElevation,
                                       int maxRiverLength)
            : this(width, height, seed, landRatio, biomes,
                   biomeCohesion, featureCohesion, continentShapeAlgorithm,
                   resources, resourceDensity,
                   riverDensity, riverSourceMinElevation, maxRiverLength,
                   DefaultMinRiverLength, DefaultMaxWaterfallDrop, DefaultLakeDepthThreshold)
        {
        }

        /// <summary>
        /// Full constructor including the ADR-0025 river shaping
        /// settings. Rivers remain opt-out: when
        /// <paramref name="riverDensity"/> is <c>0</c> the
        /// <c>RiverStage</c> is a no-op and both
        /// <see cref="WorldMap.RiverEdges"/> and
        /// <see cref="WorldMap.RiverPaths"/> stay empty.
        /// </summary>
        /// <param name="width">Number of columns. Must be positive.</param>
        /// <param name="height">Number of rows. Must be positive.</param>
        /// <param name="seed">The 64-bit RNG seed.</param>
        /// <param name="landRatio">Target land proportion in <c>(0, 1)</c>.</param>
        /// <param name="biomes">Biome content set; structural requirements per the shorter ctor.</param>
        /// <param name="biomeCohesion">Biome smoothing strength in <c>[0, 1]</c>.</param>
        /// <param name="featureCohesion">Feature smoothing strength in <c>[0, 1]</c>.</param>
        /// <param name="continentShapeAlgorithm">Continent-shape algorithm choice.</param>
        /// <param name="resources">Resource content set.</param>
        /// <param name="resourceDensity">Per-tile resource placement probability.</param>
        /// <param name="riverDensity">
        /// Per-source-eligible-corner probability of becoming a river
        /// source in <c>[0, 1]</c>. <c>0</c> disables rivers.
        /// </param>
        /// <param name="riverSourceMinElevation">
        /// Minimum <c>ElevationLevel</c> across a corner's three tiles
        /// for it to be a river source candidate.
        /// </param>
        /// <param name="maxRiverLength">
        /// Safety cap on the number of steps a single river can take.
        /// </param>
        /// <param name="minRiverLength">
        /// Minimum committed length in edges; shorter walks are discarded.
        /// </param>
        /// <param name="maxWaterfallDrop">
        /// Maximum elevation levels a single waterfall crossing may span.
        /// </param>
        /// <param name="lakeDepthThreshold">
        /// Fill depth above which a basin tile becomes a lake.
        /// </param>
        public WorldGenerationSettings(int width, int height, ulong seed, float landRatio,
                                       IReadOnlyList<BiomeDefinition> biomes,
                                       float biomeCohesion, float featureCohesion,
                                       ContinentShapeAlgorithm continentShapeAlgorithm,
                                       IReadOnlyList<ResourceDefinition> resources,
                                       float resourceDensity,
                                       float riverDensity,
                                       int riverSourceMinElevation,
                                       int maxRiverLength,
                                       int minRiverLength,
                                       int maxWaterfallDrop,
                                       float lakeDepthThreshold)
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
            RiverDensity = riverDensity;
            RiverSourceMinElevation = riverSourceMinElevation;
            MaxRiverLength = maxRiverLength;
            MinRiverLength = minRiverLength;
            MaxWaterfallDrop = maxWaterfallDrop;
            LakeDepthThreshold = lakeDepthThreshold;
        }
    }
}
