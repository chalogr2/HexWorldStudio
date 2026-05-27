using System;
using HexWorldStudio.Data;
using HexWorldStudio.Generation.Pipeline;
using HexWorldStudio.Generation.Rng;

namespace HexWorldStudio.Generation
{
    /// <summary>
    /// Public entry point for world generation. Constructs a fresh
    /// <see cref="WorldMap"/> from a <see cref="WorldGenerationSettings"/>
    /// by running the configured pipeline of
    /// <see cref="IGenerationStage"/> instances against a deterministic
    /// <see cref="RngService"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Instance-based: <c>new HexWorldGenerator().Generate(settings)</c>.
    /// The no-arg constructor exists today so the default pipeline can
    /// be replaced by an alternative constructor (for example a
    /// <c>WorldPreset</c>-driven one) in a later slice without an API-
    /// breaking refactor. Multiple instances may be used in parallel
    /// on separate threads; a single instance is not thread-safe.
    /// </para>
    /// <para>
    /// The default pipeline is currently empty: <see cref="Generate"/>
    /// allocates the <see cref="WorldMap"/>, seeds the RNG from the
    /// settings, runs zero stages, and returns the map. Tiles therefore
    /// retain their default NaN / <c>TileLandState.Unset</c> values
    /// until later slices add the actual stages.
    /// </para>
    /// <para>
    /// Settings validation lives here, not in
    /// <see cref="WorldGenerationSettings"/>'s constructor, so settings
    /// can be built incrementally (e.g., from inspector fields) without
    /// throwing on intermediate values.
    /// </para>
    /// </remarks>
    public sealed class HexWorldGenerator
    {
        private readonly IGenerationStage[] _stages;

        /// <summary>
        /// Constructs a generator with the default pipeline (empty in v1
        /// Slice 4; subsequent slices append stages as they're authored).
        /// </summary>
        public HexWorldGenerator()
        {
            _stages = Array.Empty<IGenerationStage>();
        }

        /// <summary>
        /// Runs the pipeline against the given settings and returns the
        /// resulting <see cref="WorldMap"/>.
        /// </summary>
        /// <param name="settings">The generation inputs.</param>
        /// <returns>
        /// A freshly allocated <see cref="WorldMap"/> whose
        /// <c>Width</c>, <c>Height</c>, and <c>Seed</c> echo the
        /// supplied settings. Per-tile state reflects whatever stages
        /// (if any) have run.
        /// </returns>
        /// <exception cref="ArgumentNullException">
        /// Thrown when <paramref name="settings"/> is null.
        /// </exception>
        /// <exception cref="ArgumentOutOfRangeException">
        /// Thrown when <paramref name="settings"/>'s width or height is
        /// not strictly positive.
        /// </exception>
        public WorldMap Generate(WorldGenerationSettings settings)
        {
            if (settings == null)
            {
                throw new ArgumentNullException(nameof(settings));
            }
            if (settings.Width <= 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(settings),
                    settings.Width,
                    $"settings.Width must be positive (got {settings.Width}).");
            }
            if (settings.Height <= 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(settings),
                    settings.Height,
                    $"settings.Height must be positive (got {settings.Height}).");
            }
            if (!(settings.LandRatio > 0f) || !(settings.LandRatio < 1f) || float.IsNaN(settings.LandRatio))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(settings),
                    settings.LandRatio,
                    $"settings.LandRatio must be strictly between 0 and 1 (got {settings.LandRatio}).");
            }

            var map = new WorldMap(settings.Width, settings.Height, settings.Seed);
            var rng = new RngService(settings.Seed);
            var context = new GenerationContext(map, settings, rng);

            for (int i = 0; i < _stages.Length; i++)
            {
                _stages[i].Execute(context);
            }

            return map;
        }
    }
}
