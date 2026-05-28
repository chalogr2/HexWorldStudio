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
        /// Constructs a fully-specified settings instance.
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
        {
            Width = width;
            Height = height;
            Seed = seed;
            LandRatio = landRatio;
            Biomes = biomes;
        }
    }
}
