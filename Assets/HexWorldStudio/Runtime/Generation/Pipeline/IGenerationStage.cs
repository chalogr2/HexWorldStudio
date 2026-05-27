namespace HexWorldStudio.Generation.Pipeline
{
    /// <summary>
    /// One ordered mutation step in the world-generation pipeline.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Each role in the pipeline (continent shape, elevation field,
    /// climate, biome assignment, hydrology, resource placement,
    /// starting positions, post-process) is filled by an
    /// <see cref="IGenerationStage"/> implementation. Multiple
    /// implementations can exist for the same role; the active one is
    /// selected at generation time via <c>WorldGenerationSettings</c>
    /// (and later <c>WorldPreset</c> SOs). See ADR-0010.
    /// </para>
    /// <para>
    /// Implementations must be deterministic: given the same
    /// <see cref="GenerationContext"/> they must mutate the world map
    /// identically across runs and platforms. The only source of
    /// randomness available is <c>GenerationContext.Rng</c>; stages
    /// must not call <c>UnityEngine.Random</c>, <c>System.DateTime</c>,
    /// <c>GetHashCode</c> on unstable inputs, or any parallel-for
    /// construct whose ordering is not pinned.
    /// </para>
    /// <para>
    /// Internal for v1. A formal public extension API for buyer-
    /// authored stages is a v1.1 candidate per ADR-0010.
    /// </para>
    /// </remarks>
    internal interface IGenerationStage
    {
        /// <summary>
        /// A short stable name for the stage, used for logging,
        /// diagnostics, and (eventually) progress reporting. Should be
        /// human-readable and unique within the pipeline.
        /// </summary>
        string Name { get; }

        /// <summary>
        /// Runs the stage. Implementations mutate
        /// <see cref="GenerationContext.Map"/> using
        /// <see cref="GenerationContext.Settings"/> as input and
        /// <see cref="GenerationContext.Rng"/> as the sole randomness
        /// source.
        /// </summary>
        /// <param name="ctx">The shared per-run pipeline state.</param>
        void Execute(GenerationContext ctx);
    }
}
