using System;
using HexWorldStudio.Coordinates;
using HexWorldStudio.Data;
using HexWorldStudio.Generation.Rng;

namespace HexWorldStudio.Generation.Pipeline.Stages
{
    /// <summary>
    /// Default elevation-field algorithm: fractal Brownian Perlin noise
    /// over the cylindrical map, normalized into <c>[0, 1]</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Independent of <see cref="ContinentShapeStage"/>: a different
    /// labeled fork of the master RNG produces a separate noise basin.
    /// Water tiles end up with an elevation value too — buyers and
    /// renderers may reinterpret that as sea-floor depth, or ignore it.
    /// Coupled "realistic" variants (where elevation drives the
    /// land/water threshold instead of being independent) land later
    /// as alternate <see cref="IGenerationStage"/> implementations
    /// per ADR-0010.
    /// </para>
    /// <para>
    /// Output range is enforced by linearly remapping the Perlin band
    /// (theoretically <c>[-1, 1]</c>, practically tighter) into
    /// <c>[0, 1]</c> via the empirical min/max over the actual sample
    /// set. This guarantees the output spans the full unit interval
    /// regardless of how narrow the realized Perlin range turns out
    /// to be for a given seed.
    /// </para>
    /// </remarks>
    internal sealed class ElevationFieldStage : IGenerationStage
    {
        // Defaults hardcoded for v1; will move to WorldPreset content.
        private const float CellsX = 6f;
        private const float CellsY = 4.5f;
        private const int   Octaves = 4;
        private const float Persistence = 0.5f;
        private const float Lacunarity = 2f;

        /// <inheritdoc/>
        public string Name => "ElevationField";

        /// <inheritdoc/>
        public void Execute(GenerationContext ctx)
        {
            if (ctx == null) throw new ArgumentNullException(nameof(ctx));

            WorldMap map = ctx.Map;
            int count = map.Tiles.Count;

            RngService noiseRng = ctx.Rng.Fork("noise-elevation-field");
            var perlin = new Perlin(noiseRng);

            float[] samples = new float[count];
            float min = float.PositiveInfinity;
            float max = float.NegativeInfinity;

            for (int i = 0; i < count; i++)
            {
                HexTile tile = map.Tiles[i];
                (int col, int row) = tile.Coord.ToOffsetOddR();
                float colNorm = (float)col / map.Width;
                float rowNorm = (float)row / Math.Max(1, map.Height - 1);
                float v = perlin.CylindricalFractalSample(
                    colNorm, rowNorm,
                    CellsX, CellsY,
                    Octaves, Persistence, Lacunarity);
                samples[i] = v;
                if (v < min) min = v;
                if (v > max) max = v;
            }

            // Defend against a degenerate constant field (max == min):
            // assign 0.5 to every tile and bail. Practically impossible
            // for non-degenerate Perlin output, but explicit guard
            // beats a NaN from division by zero.
            float range = max - min;
            if (range <= 0f)
            {
                for (int i = 0; i < count; i++)
                {
                    map.Tiles[i].Elevation = 0.5f;
                }
                return;
            }

            for (int i = 0; i < count; i++)
            {
                map.Tiles[i].Elevation = (samples[i] - min) / range;
            }
        }
    }
}
