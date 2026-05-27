using HexWorldStudio.Data;
using HexWorldStudio.Generation.Rng;

namespace HexWorldStudio.Generation.Pipeline
{
    /// <summary>
    /// Per-run state passed to every <see cref="IGenerationStage"/>.
    /// Carries the in-progress <see cref="WorldMap"/>, the originating
    /// <see cref="WorldGenerationSettings"/>, and the deterministic
    /// <see cref="RngService"/> that is the only legitimate source of
    /// randomness inside a pipeline run.
    /// </summary>
    /// <remarks>
    /// Stages mutate <see cref="Map"/> as they execute. The settings
    /// and RNG references are immutable for the run's duration; stages
    /// that need independent random streams should call
    /// <see cref="RngService.Fork(string)"/> rather than mutating or
    /// re-seeding the shared RNG.
    /// </remarks>
    internal sealed class GenerationContext
    {
        /// <summary>The world map being built up by the pipeline.</summary>
        public WorldMap Map { get; }

        /// <summary>The settings this run was launched with.</summary>
        public WorldGenerationSettings Settings { get; }

        /// <summary>
        /// The deterministic random source for the whole run. Stages
        /// either draw directly from this or fork labeled sub-streams.
        /// </summary>
        public RngService Rng { get; }

        /// <summary>
        /// Constructs a context. Called by <see cref="HexWorldGenerator"/>
        /// at the start of a generation run.
        /// </summary>
        internal GenerationContext(WorldMap map, WorldGenerationSettings settings, RngService rng)
        {
            Map = map;
            Settings = settings;
            Rng = rng;
        }
    }
}
