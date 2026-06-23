namespace HexWorldStudio.Generation
{
    /// <summary>
    /// Selects which concrete continent-shape algorithm fills the
    /// <c>ContinentShapeStage</c> pipeline slot. The shipped algorithms
    /// mirror the classic Civilization map-type catalog; buyers pick
    /// the value they want on
    /// <see cref="WorldGenerationSettings.ContinentShapeAlgorithm"/>
    /// (or via the inspector slider on
    /// <c>WorldGeneratorBootstrap</c>). See ADR-0020 for the
    /// per-algorithm rationale and ADR-0010 for the pluggable-slot
    /// pattern.
    /// </summary>
    /// <remarks>
    /// Each algorithm is deterministic per seed and honors
    /// <see cref="WorldGenerationSettings.LandRatio"/> exactly (to
    /// within one tile of the theoretical count) — algorithms that
    /// bias the noise field with vertical or radial gradients still
    /// apply a percentile threshold afterward to preserve that
    /// invariant. Algorithms fork the master RNG with distinct labels
    /// so adding a new algorithm does not shift the seeded stream
    /// experienced by existing-seed users of other algorithms.
    /// </remarks>
    public enum ContinentShapeAlgorithm
    {
        /// <summary>
        /// Default. Single-octave cylindrical Perlin thresholded at
        /// the <c>(1 − LandRatio)</c> percentile, producing several
        /// medium-sized landmasses scattered around the cylinder. The
        /// historical Slice-5 behavior — Civ "Continents" equivalent.
        /// </summary>
        Continents = 0,

        /// <summary>
        /// One large supercontinent clustered in the temperate
        /// latitudes with the poles left as water. Lower-frequency
        /// Perlin plus a vertical Gaussian boost centered on the
        /// mid-row. Civ "Pangaea" equivalent. Best with
        /// <c>LandRatio</c> in <c>[0.3, 0.55]</c>.
        /// </summary>
        Pangaea = 1,

        /// <summary>
        /// Many small islands scattered across the map. Higher-
        /// frequency 2-octave fBm thresholded at <c>(1 − LandRatio)</c>.
        /// Civ "Archipelago" equivalent. Best with <c>LandRatio</c>
        /// around <c>0.3-0.4</c>.
        /// </summary>
        Archipelago = 2,

        /// <summary>
        /// Irregular coastlines with fjords, peninsulas, and inland
        /// chains. Six-octave fBm at medium base frequency
        /// preserves detail at every scale. Civ "Fractal" equivalent.
        /// </summary>
        Fractal = 3,

        /// <summary>
        /// One central inland sea surrounded by a ring of land.
        /// Medium-frequency Perlin minus a Gaussian centered on the
        /// mid-row (inverse of <see cref="Pangaea"/>). Civ "Inland
        /// Sea" equivalent. Best with <c>LandRatio</c> in
        /// <c>[0.6, 0.75]</c> so the surrounding ring has enough land.
        /// </summary>
        InlandSea = 4,

        /// <summary>
        /// Mostly land with many small scattered water bodies. Higher
        /// frequency than <see cref="Archipelago"/>. Civ "Lakes"
        /// equivalent. Best with <c>LandRatio</c> in <c>[0.8, 0.9]</c>.
        /// </summary>
        Lakes = 5,

        /// <summary>
        /// Picks one of the non-shuffle algorithms uniformly at
        /// random using the seeded RNG, then dispatches to it. The
        /// choice is part of the deterministic output: same seed
        /// always picks the same algorithm and produces the same
        /// world.
        /// </summary>
        Shuffle = 99,
    }
}
