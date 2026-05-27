using System;
using HexWorldStudio.Coordinates;
using HexWorldStudio.Data;
using HexWorldStudio.Generation.Rng;

namespace HexWorldStudio.Generation.Pipeline.Stages
{
    /// <summary>
    /// Default continent-shape algorithm: a single cylindrical Perlin
    /// field thresholded so the resulting land ratio matches
    /// <see cref="WorldGenerationSettings.LandRatio"/> to within one
    /// tile of theoretical.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Algorithm: sample the cylindrical Perlin field at every tile
    /// using a labeled fork of the master RNG (label
    /// <c>"noise-continent-shape"</c>), sort the resulting values, and
    /// pick the threshold at the <c>(1 - LandRatio)</c> percentile.
    /// Any tile whose noise sample is strictly greater than the
    /// threshold becomes <see cref="TileLandState.Land"/>; every other
    /// tile becomes <see cref="TileLandState.Water"/>.
    /// </para>
    /// <para>
    /// This is the v1 baseline implementation. Civ-style variants
    /// (Pangaea, Archipelago, Fractal, Inland Sea, Island Plates,
    /// Lakes, Highlands, Small Continents, Mirror, etc., tracked in
    /// <c>FEATURES.md</c> "Map type algorithms") land as additional
    /// implementations of <see cref="IGenerationStage"/> in dedicated
    /// future slices per ADR-0010.
    /// </para>
    /// <para>
    /// The percentile threshold guarantees the configured land ratio
    /// exactly (modulo integer-tile granularity), making maps
    /// reproducibly proportioned across seeds — important for fair
    /// multiplayer setups and balanced demo content.
    /// </para>
    /// </remarks>
    internal sealed class ContinentShapeStage : IGenerationStage
    {
        // Noise scale: roughly the number of independent landmasses
        // around the cylindrical seam at default zoom. Hardcoded for v1;
        // moves to WorldPreset SO content later.
        private const float CellsX = 5f;
        private const float CellsY = 3.5f;

        /// <inheritdoc/>
        public string Name => "ContinentShape";

        /// <inheritdoc/>
        public void Execute(GenerationContext ctx)
        {
            if (ctx == null) throw new ArgumentNullException(nameof(ctx));

            WorldMap map = ctx.Map;
            int count = map.Tiles.Count;
            float landRatio = ctx.Settings.LandRatio;

            RngService noiseRng = ctx.Rng.Fork("noise-continent-shape");
            var perlin = new Perlin(noiseRng);

            // Sample the cylindrical field at every tile.
            float[] samples = new float[count];
            for (int i = 0; i < count; i++)
            {
                HexTile tile = map.Tiles[i];
                (int col, int row) = tile.Coord.ToOffsetOddR();
                float colNorm = (float)col / map.Width;
                float rowNorm = (float)row / Math.Max(1, map.Height - 1);
                samples[i] = perlin.CylindricalSample(colNorm, rowNorm, CellsX, CellsY);
            }

            // Threshold at the (1 - landRatio) percentile so exactly
            // `landRatio * count` (rounded down) tiles end up above it.
            float threshold = ComputePercentileThreshold(samples, 1f - landRatio);

            for (int i = 0; i < count; i++)
            {
                HexTile tile = map.Tiles[i];
                tile.LandState = samples[i] > threshold
                    ? TileLandState.Land
                    : TileLandState.Water;
            }
        }

        // Pick the value at the given percentile (0 = min, 1 = max) of
        // a sorted copy of `samples`. Uses Array.Sort for determinism;
        // for v1's map sizes (≤ low tens of thousands of tiles) the
        // O(N log N) cost is negligible. Quickselect can replace this
        // if a perf problem materializes.
        private static float ComputePercentileThreshold(float[] samples, float percentile)
        {
            float[] sorted = (float[])samples.Clone();
            Array.Sort(sorted);

            int index = (int)Math.Floor(percentile * sorted.Length);
            if (index < 0) index = 0;
            if (index >= sorted.Length) index = sorted.Length - 1;
            return sorted[index];
        }
    }
}
