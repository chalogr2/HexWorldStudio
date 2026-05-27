using System;

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
        /// Constructs a settings instance. Values are stored as-is;
        /// validation happens when these settings are passed to
        /// <see cref="HexWorldGenerator.Generate"/>.
        /// </summary>
        /// <param name="width">Number of columns. Must be positive at generation time.</param>
        /// <param name="height">Number of rows. Must be positive at generation time.</param>
        /// <param name="seed">The 64-bit RNG seed.</param>
        public WorldGenerationSettings(int width, int height, ulong seed)
        {
            Width = width;
            Height = height;
            Seed = seed;
        }
    }
}
