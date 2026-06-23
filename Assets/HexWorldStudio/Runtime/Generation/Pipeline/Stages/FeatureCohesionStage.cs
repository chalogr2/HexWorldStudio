using System;
using System.Collections.Generic;
using HexWorldStudio.Content;
using HexWorldStudio.Coordinates;
using HexWorldStudio.Data;

namespace HexWorldStudio.Generation.Pipeline.Stages
{
    /// <summary>
    /// Post-placement cohesion smoothing for tile features. Same
    /// neighbor-consensus algorithm as
    /// <see cref="BiomeCohesionStage"/> but the candidate set is
    /// constrained to features the tile's biome explicitly allows, so
    /// a smoothing pass can never push (for example) a Forest into a
    /// biome whose <see cref="BiomeDefinition.AllowedFeatures"/> has
    /// no forest entries.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Strength comes from
    /// <see cref="WorldGenerationSettings.FeatureCohesion"/> and shares
    /// the same mapping function
    /// (<see cref="BiomeCohesionStage.FromStrength"/>). The default
    /// value is lower than the biome cohesion default because feature
    /// variety inside a biome is desirable (a Forest tile in a
    /// Grassland-dominant Temperate biome reads as natural diversity);
    /// the stage at the recommended setting only collapses single-tile
    /// outliers, not legitimate small clusters.
    /// </para>
    /// <para>
    /// Algorithm per iteration (double-buffered for order independence):
    /// snapshot every tile's current feature; for each non-water tile
    /// whose biome has any allowed features, count neighbors that
    /// share the snapshot feature; if fewer than the threshold do,
    /// switch to the most-common neighbor feature that is in the
    /// tile's biome's
    /// <see cref="BiomeDefinition.AllowedFeatures"/> list.
    /// Tie-break uses that list's order so the result is deterministic
    /// for the same seed. Water tiles and tiles with no current
    /// feature are preserved.
    /// </para>
    /// </remarks>
    internal sealed class FeatureCohesionStage : IGenerationStage
    {
        /// <inheritdoc/>
        public string Name => "FeatureCohesion";

        /// <inheritdoc/>
        public void Execute(GenerationContext ctx)
        {
            if (ctx == null) throw new ArgumentNullException(nameof(ctx));

            (int iterations, int threshold) = BiomeCohesionStage.FromStrength(ctx.Settings.FeatureCohesion);
            if (iterations <= 0) return;

            WorldMap map = ctx.Map;
            int count = map.Tiles.Count;

            TileFeatureDefinition[] snapshot = new TileFeatureDefinition[count];
            var tally = new Dictionary<TileFeatureDefinition, int>(6);

            for (int iter = 0; iter < iterations; iter++)
            {
                for (int i = 0; i < count; i++)
                {
                    snapshot[i] = map.Tiles[i].Feature;
                }

                for (int i = 0; i < count; i++)
                {
                    HexTile tile = map.Tiles[i];
                    if (tile.LandState == TileLandState.Water) continue;

                    BiomeDefinition biome = tile.Biome;
                    if (biome == null || biome.AllowedFeatures == null || biome.AllowedFeatures.Length == 0) continue;

                    TileFeatureDefinition current = snapshot[i];

                    int sameCount = 0;
                    tally.Clear();
                    foreach (HexCoord nc in tile.Coord.Neighbors())
                    {
                        if (!map.TryGetIndex(nc, out int ni)) continue;
                        TileFeatureDefinition nf = snapshot[ni];
                        if (nf == null) continue;
                        if (nf == current) sameCount++;
                        if (tally.TryGetValue(nf, out int c)) tally[nf] = c + 1;
                        else tally[nf] = 1;
                    }

                    if (sameCount >= threshold) continue;

                    // Tie-break by AllowedFeatures order, AND restrict
                    // candidates to features actually permitted by
                    // this tile's biome — keeps the post-pass from
                    // violating biome-feature compatibility.
                    TileFeatureDefinition best = current;
                    int bestCount = sameCount;
                    TileFeatureDefinition[] allowed = biome.AllowedFeatures;
                    for (int f = 0; f < allowed.Length; f++)
                    {
                        TileFeatureDefinition candidate = allowed[f];
                        if (candidate == null) continue;
                        if (!tally.TryGetValue(candidate, out int c)) continue;
                        if (c > bestCount)
                        {
                            best = candidate;
                            bestCount = c;
                        }
                    }

                    tile.Feature = best;
                }
            }
        }
    }
}
