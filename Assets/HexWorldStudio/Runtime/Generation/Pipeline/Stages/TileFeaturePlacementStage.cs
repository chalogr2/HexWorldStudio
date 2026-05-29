using System;
using HexWorldStudio.Content;
using HexWorldStudio.Data;
using HexWorldStudio.Generation.Rng;

namespace HexWorldStudio.Generation.Pipeline.Stages
{
    /// <summary>
    /// Default tile-feature placement algorithm per ADR-0014. For each
    /// land tile, scores every feature in the tile's biome's
    /// <see cref="BiomeDefinition.AllowedFeatures"/> list against the
    /// tile's <see cref="HexTile.ElevationLevel"/> using a Gaussian
    /// falloff around the feature's
    /// <see cref="TileFeatureDefinition.PreferredElevationLevel"/>,
    /// weighted by <see cref="TileFeatureDefinition.Weight"/>, and
    /// draws one feature from the resulting distribution.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Score formula: <c>weight * exp(-(dist² / sigma²))</c> where
    /// <c>dist = |tile.ElevationLevel - feature.PreferredElevationLevel|</c>
    /// and <c>sigma</c> is a smoothing constant (1.5 by default).
    /// </para>
    /// <para>
    /// Uses <c>RngService.Fork("feature-placement")</c> so the labeled
    /// substream is reorder-safe with the rest of the pipeline.
    /// </para>
    /// <para>
    /// Water tiles keep <c>HexTile.Feature</c> as <c>null</c>: water
    /// biomes have an empty <c>AllowedFeatures</c> array, so no
    /// feature is ever eligible. Land tiles whose biome happens to
    /// have an empty allow-list also receive <c>null</c> — buyers
    /// authoring biomes with no features get the expected behavior
    /// of "this biome has nothing to place."
    /// </para>
    /// </remarks>
    internal sealed class TileFeaturePlacementStage : IGenerationStage
    {
        private const float SigmaSquared = 1.5f * 1.5f;

        /// <inheritdoc/>
        public string Name => "TileFeaturePlacement";

        /// <inheritdoc/>
        public void Execute(GenerationContext ctx)
        {
            if (ctx == null) throw new ArgumentNullException(nameof(ctx));

            RngService rng = ctx.Rng.Fork("feature-placement");
            WorldMap map = ctx.Map;
            int count = map.Tiles.Count;

            // Scratch buffer reused across tiles to avoid per-tile
            // allocations.
            float[] scores = new float[16];

            for (int i = 0; i < count; i++)
            {
                HexTile tile = map.Tiles[i];

                BiomeDefinition biome = tile.Biome;
                if (biome == null || biome.AllowedFeatures == null || biome.AllowedFeatures.Length == 0)
                {
                    tile.Feature = null;
                    continue;
                }

                int candidateCount = biome.AllowedFeatures.Length;
                if (candidateCount > scores.Length)
                {
                    scores = new float[Math.Max(candidateCount, scores.Length * 2)];
                }

                float totalScore = 0f;
                for (int f = 0; f < candidateCount; f++)
                {
                    TileFeatureDefinition feat = biome.AllowedFeatures[f];
                    if (feat == null)
                    {
                        scores[f] = 0f;
                        continue;
                    }
                    int dist = tile.ElevationLevel - feat.PreferredElevationLevel;
                    float distSq = dist * dist;
                    float falloff = (float)Math.Exp(-distSq / SigmaSquared);
                    float weight = feat.Weight < 0f ? 0f : feat.Weight;
                    scores[f] = weight * falloff;
                    totalScore += scores[f];
                }

                if (totalScore <= 0f)
                {
                    // All zero scores (e.g., all candidates null or
                    // all weights zero). Fall back to the first
                    // non-null entry, or null if none.
                    tile.Feature = FirstNonNull(biome.AllowedFeatures);
                    continue;
                }

                // Draw uniform [0, totalScore) and walk the array.
                float pick = rng.NextFloat() * totalScore;
                float cumulative = 0f;
                TileFeatureDefinition chosen = null;
                for (int f = 0; f < candidateCount; f++)
                {
                    cumulative += scores[f];
                    if (pick < cumulative)
                    {
                        chosen = biome.AllowedFeatures[f];
                        break;
                    }
                }
                tile.Feature = chosen ?? FirstNonNull(biome.AllowedFeatures);
            }
        }

        private static TileFeatureDefinition FirstNonNull(TileFeatureDefinition[] list)
        {
            for (int i = 0; i < list.Length; i++)
            {
                if (list[i] != null) return list[i];
            }
            return null;
        }
    }
}
