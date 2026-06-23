using System;
using System.Collections.Generic;
using HexWorldStudio.Content;
using HexWorldStudio.Coordinates;
using HexWorldStudio.Data;

namespace HexWorldStudio.Generation.Pipeline.Stages
{
    /// <summary>
    /// Post-assignment cohesion smoothing for biomes. Repeatedly snaps
    /// outlier tiles toward their neighbors' majority biome so the raw
    /// climate-envelope classification produces coherent regions rather
    /// than single-tile speckling near biome boundaries.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Strength comes from <see cref="WorldGenerationSettings.BiomeCohesion"/>
    /// (one 0..1 slider per ADR-0019). The slider maps via
    /// <see cref="FromStrength"/> to an integer number of iterations
    /// and a per-tile "minimum same-biome neighbor" threshold. At
    /// strength 0 the stage is a no-op — existing seeded worlds remain
    /// byte-identical when buyers park the slider at zero.
    /// </para>
    /// <para>
    /// Algorithm per iteration (double-buffered for order independence):
    /// snapshot every tile's current biome, then for each non-water
    /// tile count neighbors that share the snapshot biome; if fewer
    /// than the threshold do, switch the tile to the most-common
    /// neighbor land biome. Ties are broken by
    /// <see cref="WorldGenerationSettings.Biomes"/> order so the result
    /// is deterministic given the seed.
    /// </para>
    /// <para>
    /// Water tiles are never modified. They are classified by topology
    /// in <see cref="BiomeAssignmentStage"/> (Coast if any land
    /// neighbor, Ocean otherwise), and that topology must be preserved
    /// for downstream rendering and gameplay. The tally excludes
    /// water-biome neighbors so smoothing never demotes a coastal land
    /// tile to a water biome.
    /// </para>
    /// <para>
    /// Cylindrical-wrap-aware via
    /// <see cref="WorldMap.TryGetIndex"/>: seam tiles see their wrapped
    /// neighbors and smooth correctly across the boundary. Total cost
    /// is <c>O(iterations × tiles × 6)</c> — trivial for v1's map
    /// sizes.
    /// </para>
    /// </remarks>
    internal sealed class BiomeCohesionStage : IGenerationStage
    {
        /// <inheritdoc/>
        public string Name => "BiomeCohesion";

        /// <summary>
        /// Maps the 0..1 strength slider to an integer
        /// <c>(iterations, threshold)</c> pair. Public so the same
        /// mapping is reachable from tests and from
        /// <see cref="FeatureCohesionStage"/>.
        /// </summary>
        /// <param name="strength">
        /// Cohesion strength in <c>[0, 1]</c>. Values below 0 are
        /// treated as 0 (stage no-ops); values above 1 are clamped
        /// to 1.
        /// </param>
        /// <returns>
        /// <c>(iterations, threshold)</c>. When <c>iterations == 0</c>
        /// the stage skips all work. <c>threshold</c> is the minimum
        /// number of same-type neighbors required to keep a tile from
        /// being smoothed.
        /// </returns>
        public static (int iterations, int threshold) FromStrength(float strength)
        {
            if (!(strength > 0f)) return (0, 0);          // NaN / negative / 0 all → no-op
            if (strength > 1f) strength = 1f;
            int iters     = Math.Max(1, (int)Math.Round(strength * 4));
            int threshold = Math.Max(1, (int)Math.Round(strength * 3));
            return (iters, threshold);
        }

        /// <inheritdoc/>
        public void Execute(GenerationContext ctx)
        {
            if (ctx == null) throw new ArgumentNullException(nameof(ctx));

            (int iterations, int threshold) = FromStrength(ctx.Settings.BiomeCohesion);
            if (iterations <= 0) return;

            WorldMap map = ctx.Map;
            IReadOnlyList<BiomeDefinition> biomes = ctx.Settings.Biomes;
            int count = map.Tiles.Count;

            BiomeDefinition[] snapshot = new BiomeDefinition[count];
            // Reused scratch tally so we don't reallocate per tile.
            var tally = new Dictionary<BiomeDefinition, int>(6);

            for (int iter = 0; iter < iterations; iter++)
            {
                // Build the snapshot at the start of every iteration
                // so smoothing decisions inside the iteration are
                // order-independent (every tile reads pre-iteration
                // state).
                for (int i = 0; i < count; i++)
                {
                    snapshot[i] = map.Tiles[i].Biome;
                }

                for (int i = 0; i < count; i++)
                {
                    HexTile tile = map.Tiles[i];

                    // Water tiles are classified by topology, not by
                    // climate — preserve them so coastal topology
                    // doesn't drift.
                    if (tile.LandState == TileLandState.Water) continue;

                    BiomeDefinition current = snapshot[i];

                    int sameCount = 0;
                    tally.Clear();
                    foreach (HexCoord nc in tile.Coord.Neighbors())
                    {
                        if (!map.TryGetIndex(nc, out int ni)) continue;
                        BiomeDefinition nb = snapshot[ni];
                        if (nb == null || nb.IsWater) continue;
                        if (nb == current) sameCount++;
                        if (tally.TryGetValue(nb, out int c)) tally[nb] = c + 1;
                        else tally[nb] = 1;
                    }

                    // Strong neighbor support → leave the tile alone.
                    if (sameCount >= threshold) continue;

                    // Pick the strictly-most-common neighbor land
                    // biome. Iterating in Biomes order means ties
                    // resolve to the first match in that order, which
                    // is the same deterministic rule
                    // BiomeAssignmentStage uses for envelope matches
                    // (ADR-0010 first-match-wins).
                    BiomeDefinition best = current;
                    int bestCount = sameCount;
                    if (biomes != null)
                    {
                        for (int b = 0; b < biomes.Count; b++)
                        {
                            BiomeDefinition candidate = biomes[b];
                            if (candidate == null || candidate.IsWater) continue;
                            if (!tally.TryGetValue(candidate, out int c)) continue;
                            if (c > bestCount)
                            {
                                best = candidate;
                                bestCount = c;
                            }
                        }
                    }

                    tile.Biome = best;
                }
            }
        }
    }
}
