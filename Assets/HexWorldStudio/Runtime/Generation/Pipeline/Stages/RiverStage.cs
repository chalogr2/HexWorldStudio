using System;
using System.Collections.Generic;
using HexWorldStudio.Content;
using HexWorldStudio.Coordinates;
using HexWorldStudio.Data;
using HexWorldStudio.Generation.Rng;

namespace HexWorldStudio.Generation.Pipeline.Stages
{
    /// <summary>
    /// Edge-based river generation stage per ADR-0024. Picks a
    /// moisture-weighted subset of high-elevation land tiles as
    /// sources; from each source walks strictly downhill along tile
    /// edges to the nearest water body; commits the accumulated
    /// edges to <see cref="WorldMap.RiverEdges"/> in canonical form.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Rivers are opt-in: with
    /// <see cref="WorldGenerationSettings.RiverDensity"/> at <c>0</c>
    /// the stage is a complete no-op. Positive densities produce
    /// approximately <c>candidateCount × density × avgMoisture</c>
    /// river-source picks per generation.
    /// </para>
    /// <para>
    /// Determinism: uses a single labeled RNG fork
    /// (<c>"river-generation"</c>) so appending / reordering other
    /// stages does not shift the seeded stream. Same seed + same
    /// settings produce byte-identical river networks.
    /// </para>
    /// <para>
    /// Algorithm — source selection: iterate canonical tile order;
    /// candidates are land tiles whose <c>ElevationLevel</c> is at
    /// least <see cref="WorldGenerationSettings.RiverSourceMinElevation"/>;
    /// a candidate becomes a source with probability
    /// <c>RiverDensity × tile.Moisture</c>. Higher-moisture tiles are
    /// therefore preferentially picked, matching real-world
    /// hydrology.
    /// </para>
    /// <para>
    /// Algorithm — downhill walk: from each source, find the
    /// neighbor with the strictly lower elevation. Ties broken by RNG
    /// (deterministic via the forked stream). Record the edge as a
    /// canonical <see cref="RiverEdge"/>. If the next tile is water
    /// (Ocean or Coast), terminate — the river has reached the sea.
    /// If the recorded edge is already in the set, terminate — the
    /// walk has merged with an existing river. If no downhill
    /// neighbor exists (rare on smoothed terrain) the tile is a local
    /// minimum and is flipped to a Coast lake tile, then terminated.
    /// Safety cap: no walk exceeds
    /// <see cref="WorldGenerationSettings.MaxRiverLength"/> steps.
    /// </para>
    /// <para>
    /// Cost: <c>O(candidates + rivers × maxLength × 6)</c>. At the
    /// v1 default map sizes (sub-100×60) and default density (0.15),
    /// this is a few thousand operations per generation — negligible
    /// against the noise stages.
    /// </para>
    /// </remarks>
    internal sealed class RiverStage : IGenerationStage
    {
        /// <inheritdoc/>
        public string Name => "River";

        /// <inheritdoc/>
        public void Execute(GenerationContext ctx)
        {
            if (ctx == null) throw new ArgumentNullException(nameof(ctx));

            WorldGenerationSettings settings = ctx.Settings;
            if (settings.RiverDensity <= 0f) return;

            WorldMap map = ctx.Map;
            RngService rng = ctx.Rng.Fork("river-generation");

            int minElevation = settings.RiverSourceMinElevation;
            int maxLength = settings.MaxRiverLength;
            if (maxLength <= 0) maxLength = 40;

            BiomeDefinition lakeBiome = FindCoastBiome(settings.Biomes);

            var edges = new HashSet<RiverEdge>();
            var neighborBuffer = new List<(HexDirection dir, HexTile tile)>(6);

            // Deterministic source iteration: raster order over the
            // tile array with a per-tile probability draw. Same seed
            // + same settings ⇒ identical source set.
            for (int i = 0; i < map.Tiles.Count; i++)
            {
                HexTile source = map.Tiles[i];
                if (source.LandState != TileLandState.Land) continue;
                if (source.ElevationLevel < minElevation) continue;

                float moisture = float.IsNaN(source.Moisture) ? 0.5f : source.Moisture;
                float probability = settings.RiverDensity * moisture;
                if (probability <= 0f) continue;
                if (rng.NextFloat() >= probability) continue;

                WalkDownhill(source, map, rng, edges, neighborBuffer, maxLength, lakeBiome);
            }

            map.SetRivers(edges);
        }

        // Walks strictly downhill from `source` recording edges into
        // `edges`. Terminates on: reaching water, merging with an
        // existing edge, hitting a local minimum (which is flipped to
        // a lake), or exceeding maxLength.
        private static void WalkDownhill(
            HexTile source,
            WorldMap map,
            RngService rng,
            HashSet<RiverEdge> edges,
            List<(HexDirection dir, HexTile tile)> neighborBuffer,
            int maxLength,
            BiomeDefinition lakeBiome)
        {
            HexTile current = source;
            for (int step = 0; step < maxLength; step++)
            {
                // Collect strictly lower neighbors. Ties (multiple
                // neighbors at the same minimum) are resolved by RNG
                // pick from the tie set — the picked tile becomes the
                // next step.
                neighborBuffer.Clear();
                int lowest = int.MaxValue;
                for (int d = 0; d < 6; d++)
                {
                    HexDirection dir = (HexDirection)d;
                    HexCoord neighborRaw = current.Coord.Neighbor(dir);
                    if (!map.TryGetTile(neighborRaw, out HexTile neighbor)) continue;

                    // Water neighbor is always a valid terminator,
                    // even if its elevation-level is equal to ours
                    // (Coast tiles pin to ElevationLevel 0). Include
                    // in candidates unconditionally.
                    bool isWater = neighbor.LandState == TileLandState.Water;
                    int level = neighbor.ElevationLevel;

                    if (isWater)
                    {
                        // Water is the strongest downhill signal:
                        // pin lowest to -1 so any water candidate
                        // beats any land candidate on this step.
                        if (lowest > -1) { neighborBuffer.Clear(); lowest = -1; }
                        if (lowest == -1) neighborBuffer.Add((dir, neighbor));
                        continue;
                    }

                    if (level >= current.ElevationLevel) continue;
                    if (level < lowest) { neighborBuffer.Clear(); lowest = level; }
                    if (level == lowest) neighborBuffer.Add((dir, neighbor));
                }

                if (neighborBuffer.Count == 0)
                {
                    // Local minimum on land: flip to a lake tile
                    // (Coast biome) so the river terminates in water.
                    // Skip when the buyer roster has no coast biome
                    // (unlikely — Generate validates roles).
                    if (lakeBiome != null)
                    {
                        current.LandState = TileLandState.Water;
                        current.Biome = lakeBiome;
                        current.Feature = null;
                        current.ElevationLevel = 0;
                    }
                    return;
                }

                int pick = neighborBuffer.Count == 1 ? 0 : rng.NextInt(0, neighborBuffer.Count);
                (HexDirection chosenDir, HexTile chosenTile) = neighborBuffer[pick];

                RiverEdge edge = RiverEdge.Canonicalize(current.Coord, chosenDir, map.Width);

                // Merging with an existing river = terminate. The
                // downstream flow is already in the set; adding
                // further edges would just duplicate an existing tail.
                if (!edges.Add(edge)) return;

                // Reached water = terminate. The edge is committed;
                // the river visibly meets the sea at this seam.
                if (chosenTile.LandState == TileLandState.Water) return;

                current = chosenTile;
            }
        }

        private static BiomeDefinition FindCoastBiome(IReadOnlyList<BiomeDefinition> biomes)
        {
            if (biomes == null) return null;
            for (int i = 0; i < biomes.Count; i++)
            {
                BiomeDefinition b = biomes[i];
                if (b != null && b.IsWater && b.IsCoastalWater) return b;
            }
            return null;
        }
    }
}
