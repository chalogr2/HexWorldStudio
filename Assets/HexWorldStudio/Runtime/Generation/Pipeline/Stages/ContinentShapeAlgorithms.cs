using System;
using HexWorldStudio.Coordinates;
using HexWorldStudio.Data;
using HexWorldStudio.Generation.Rng;

namespace HexWorldStudio.Generation.Pipeline.Stages
{
    /// <summary>
    /// Concrete implementations of the continent-shape catalog
    /// declared in <see cref="ContinentShapeAlgorithm"/>. Each
    /// algorithm reads <see cref="GenerationContext"/> state, builds
    /// a per-tile sample field, and writes <see cref="HexTile.LandState"/>
    /// to every tile via a percentile threshold so
    /// <see cref="WorldGenerationSettings.LandRatio"/> is honored
    /// regardless of the algorithm's bias gradients.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Each algorithm forks <see cref="RngService"/> with a distinct
    /// label (<c>"noise-continent-shape"</c> for Continents,
    /// <c>"noise-continent-pangaea"</c> for Pangaea, etc.) so the
    /// seeded stream observed by any one algorithm is invariant
    /// to other algorithms being added or reordered. The
    /// <see cref="ContinentShapeAlgorithm.Shuffle"/> dispatcher
    /// forks under <c>"continent-shape-shuffle"</c> to pick its
    /// inner choice, and the chosen algorithm then forks its own
    /// noise label on top.
    /// </para>
    /// <para>
    /// All sampling goes through <see cref="Perlin.CylindricalSample"/>
    /// or <see cref="Perlin.CylindricalFractalSample"/>, both of which
    /// wrap on the column axis so seam tiles produce continuous
    /// shapes across the cylindrical boundary.
    /// </para>
    /// </remarks>
    internal static class ContinentShapeAlgorithms
    {
        // Explicit pool the Shuffle algorithm draws from. Hardcoded
        // rather than iterating the enum so adding a new
        // ContinentShapeAlgorithm value does not silently change a
        // seeded "Shuffle" result for existing buyers.
        private static readonly ContinentShapeAlgorithm[] s_ShufflePool =
        {
            ContinentShapeAlgorithm.Continents,
            ContinentShapeAlgorithm.Pangaea,
            ContinentShapeAlgorithm.Archipelago,
            ContinentShapeAlgorithm.Fractal,
            ContinentShapeAlgorithm.InlandSea,
            ContinentShapeAlgorithm.Lakes,
        };

        /// <summary>
        /// Dispatches to the concrete algorithm named by
        /// <paramref name="algorithm"/>. Unknown values fall back to
        /// <see cref="ContinentShapeAlgorithm.Continents"/> (the
        /// historical default) so a Settings instance constructed
        /// from an outdated serialized enum value still produces a
        /// runnable world.
        /// </summary>
        public static void Apply(ContinentShapeAlgorithm algorithm, GenerationContext ctx)
        {
            if (ctx == null) throw new ArgumentNullException(nameof(ctx));

            switch (algorithm)
            {
                case ContinentShapeAlgorithm.Continents:  ApplyContinents(ctx);  break;
                case ContinentShapeAlgorithm.Pangaea:     ApplyPangaea(ctx);     break;
                case ContinentShapeAlgorithm.Archipelago: ApplyArchipelago(ctx); break;
                case ContinentShapeAlgorithm.Fractal:     ApplyFractal(ctx);     break;
                case ContinentShapeAlgorithm.InlandSea:   ApplyInlandSea(ctx);   break;
                case ContinentShapeAlgorithm.Lakes:       ApplyLakes(ctx);       break;
                case ContinentShapeAlgorithm.Shuffle:     ApplyShuffle(ctx);     break;
                default:                                  ApplyContinents(ctx);  break;
            }
        }

        // ===== Concrete algorithms ==================================

        // Continents (default): single-octave cylindrical Perlin
        // percentile threshold. The historical Slice-5 behavior —
        // same noise label so existing seeds remain byte-stable.
        private static void ApplyContinents(GenerationContext ctx)
        {
            const float CellsX = 5f;
            const float CellsY = 3.5f;

            WorldMap map = ctx.Map;
            int count = map.Tiles.Count;
            Perlin perlin = new Perlin(ctx.Rng.Fork("noise-continent-shape"));

            float[] samples = new float[count];
            for (int i = 0; i < count; i++)
            {
                (float colNorm, float rowNorm) = NormalizedCoord(map.Tiles[i], map);
                samples[i] = perlin.CylindricalSample(colNorm, rowNorm, CellsX, CellsY);
            }

            WriteLandWaterByPercentile(map, samples, ctx.Settings.LandRatio);
        }

        // Pangaea: lower-frequency Perlin biased toward mid-row via a
        // vertical Gaussian, then percentile-thresholded. The bias
        // pulls the land tiles into one tight mass in temperate
        // latitudes while leaving the poles as water.
        private static void ApplyPangaea(GenerationContext ctx)
        {
            const float CellsX = 2f;
            const float CellsY = 1.5f;
            const float GaussianSigma = 0.3f;
            const float GaussianBoost = 1.5f;

            WorldMap map = ctx.Map;
            int count = map.Tiles.Count;
            Perlin perlin = new Perlin(ctx.Rng.Fork("noise-continent-pangaea"));

            float[] samples = new float[count];
            for (int i = 0; i < count; i++)
            {
                (float colNorm, float rowNorm) = NormalizedCoord(map.Tiles[i], map);
                float p = perlin.CylindricalSample(colNorm, rowNorm, CellsX, CellsY);
                float lat = rowNorm - 0.5f;
                float bias = (float)Math.Exp(-(lat * lat) / (GaussianSigma * GaussianSigma));
                samples[i] = p + GaussianBoost * bias;
            }

            WriteLandWaterByPercentile(map, samples, ctx.Settings.LandRatio);
        }

        // Archipelago: higher-frequency 2-octave fBm — more peaks per
        // unit area → many small islands once thresholded.
        private static void ApplyArchipelago(GenerationContext ctx)
        {
            const float CellsX = 10f;
            const float CellsY = 7f;
            const int   Octaves = 2;
            const float Persistence = 0.5f;
            const float Lacunarity = 2f;

            WorldMap map = ctx.Map;
            int count = map.Tiles.Count;
            Perlin perlin = new Perlin(ctx.Rng.Fork("noise-continent-archipelago"));

            float[] samples = new float[count];
            for (int i = 0; i < count; i++)
            {
                (float colNorm, float rowNorm) = NormalizedCoord(map.Tiles[i], map);
                samples[i] = perlin.CylindricalFractalSample(
                    colNorm, rowNorm, CellsX, CellsY,
                    Octaves, Persistence, Lacunarity);
            }

            WriteLandWaterByPercentile(map, samples, ctx.Settings.LandRatio);
        }

        // Fractal: deep fBm (6 octaves) keeps detail at every scale,
        // producing craggy coastlines with fjords and peninsulas.
        private static void ApplyFractal(GenerationContext ctx)
        {
            const float CellsX = 4f;
            const float CellsY = 3f;
            const int   Octaves = 6;
            const float Persistence = 0.5f;
            const float Lacunarity = 2f;

            WorldMap map = ctx.Map;
            int count = map.Tiles.Count;
            Perlin perlin = new Perlin(ctx.Rng.Fork("noise-continent-fractal"));

            float[] samples = new float[count];
            for (int i = 0; i < count; i++)
            {
                (float colNorm, float rowNorm) = NormalizedCoord(map.Tiles[i], map);
                samples[i] = perlin.CylindricalFractalSample(
                    colNorm, rowNorm, CellsX, CellsY,
                    Octaves, Persistence, Lacunarity);
            }

            WriteLandWaterByPercentile(map, samples, ctx.Settings.LandRatio);
        }

        // InlandSea: inverse Pangaea — subtract a mid-row Gaussian
        // from the noise so the middle latitudes are pushed below
        // threshold (water) while everything outside forms the land
        // ring. Best at LandRatio ~0.6-0.75 so the ring is substantial.
        private static void ApplyInlandSea(GenerationContext ctx)
        {
            const float CellsX = 4f;
            const float CellsY = 3f;
            const float GaussianSigma = 0.25f;
            const float GaussianDepth = 1.6f;

            WorldMap map = ctx.Map;
            int count = map.Tiles.Count;
            Perlin perlin = new Perlin(ctx.Rng.Fork("noise-continent-inlandsea"));

            float[] samples = new float[count];
            for (int i = 0; i < count; i++)
            {
                (float colNorm, float rowNorm) = NormalizedCoord(map.Tiles[i], map);
                float p = perlin.CylindricalSample(colNorm, rowNorm, CellsX, CellsY);
                float lat = rowNorm - 0.5f;
                float bias = (float)Math.Exp(-(lat * lat) / (GaussianSigma * GaussianSigma));
                samples[i] = p - GaussianDepth * bias;
            }

            WriteLandWaterByPercentile(map, samples, ctx.Settings.LandRatio);
        }

        // Lakes: highest noise frequency. With LandRatio ~0.85 the
        // bottom ~15% of the noise field becomes scattered water bodies.
        private static void ApplyLakes(GenerationContext ctx)
        {
            const float CellsX = 14f;
            const float CellsY = 10f;
            const int   Octaves = 2;
            const float Persistence = 0.5f;
            const float Lacunarity = 2f;

            WorldMap map = ctx.Map;
            int count = map.Tiles.Count;
            Perlin perlin = new Perlin(ctx.Rng.Fork("noise-continent-lakes"));

            float[] samples = new float[count];
            for (int i = 0; i < count; i++)
            {
                (float colNorm, float rowNorm) = NormalizedCoord(map.Tiles[i], map);
                samples[i] = perlin.CylindricalFractalSample(
                    colNorm, rowNorm, CellsX, CellsY,
                    Octaves, Persistence, Lacunarity);
            }

            WriteLandWaterByPercentile(map, samples, ctx.Settings.LandRatio);
        }

        // Shuffle: pick a concrete algorithm via the seeded RNG, then
        // dispatch. The "continent-shape-shuffle" label keeps this
        // pick reproducible; the picked algorithm then forks its own
        // noise label as normal.
        private static void ApplyShuffle(GenerationContext ctx)
        {
            RngService rng = ctx.Rng.Fork("continent-shape-shuffle");
            int pick = rng.NextInt(0, s_ShufflePool.Length);
            Apply(s_ShufflePool[pick], ctx);
        }

        // ===== Shared helpers ======================================

        private static (float colNorm, float rowNorm) NormalizedCoord(HexTile tile, WorldMap map)
        {
            (int col, int row) = tile.Coord.ToOffsetOddR();
            float colNorm = (float)col / map.Width;
            float rowNorm = (float)row / Math.Max(1, map.Height - 1);
            return (colNorm, rowNorm);
        }

        // Sorts a copy of the per-tile sample field, picks the value
        // at the (1 − landRatio) percentile, and writes
        // <c>tile.LandState</c> for every tile. Strictly-greater
        // matches the ContinentShapeStage behavior shipped in Slice 5
        // so byte-identical results for the Continents algorithm
        // with existing seeds are preserved.
        private static void WriteLandWaterByPercentile(WorldMap map, float[] samples, float landRatio)
        {
            float threshold = ComputePercentileThreshold(samples, 1f - landRatio);
            for (int i = 0; i < samples.Length; i++)
            {
                HexTile tile = map.Tiles[i];
                tile.LandState = samples[i] > threshold
                    ? TileLandState.Land
                    : TileLandState.Water;
            }
        }

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
