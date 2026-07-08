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

            // Post-process: the walk records edges that share TILES
            // (chain L → M → R shares tile M between edges (L, M)
            // and (M, R)), not hex vertices. For rivers to render as
            // a continuous chain of edges connected corner-to-corner,
            // interior tiles with 2 incident walk edges get bridging
            // hex edges added between them — following the shortest
            // arc along the tile's boundary. This gives the Civ V / VI
            // "rivers wind around tiles" look.
            AddCornerConnectors(edges, map, rng);

            map.SetRivers(edges);
        }

        // Post-processing pass. For each land tile T with exactly 2
        // incident walk edges whose directions are non-adjacent (they
        // don't already share a corner), insert intermediate hex
        // edges along T's boundary to bridge them into a corner-
        // connected chain. Skips intermediates that would produce a
        // land ↔ water edge (invariant preserved from WalkDownhill).
        private static void AddCornerConnectors(HashSet<RiverEdge> edges, WorldMap map, RngService rng)
        {
            var tileIncidents = new Dictionary<HexCoord, List<HexDirection>>();
            foreach (RiverEdge e in edges)
            {
                HexCoord a = e.Coord;
                HexCoord b = map.Normalize(e.Coord.Neighbor(e.Direction));
                AddIncident(tileIncidents, a, e.Direction);
                AddIncident(tileIncidents, b, RiverEdge.Opposite(e.Direction));
            }

            // Snapshot iterable keys so we can mutate `edges` inside
            // the loop without invalidating enumeration.
            var tiles = new HexCoord[tileIncidents.Count];
            int idx = 0;
            foreach (HexCoord key in tileIncidents.Keys) tiles[idx++] = key;

            foreach (HexCoord tileCoord in tiles)
            {
                List<HexDirection> incidents = tileIncidents[tileCoord];
                if (incidents.Count != 2) continue;

                int d1 = (int)incidents[0];
                int d2 = (int)incidents[1];
                int diff = ((d2 - d1) % 6 + 6) % 6;

                // diff == 1 or 5: incident edges already share a hex
                // corner on this tile — nothing to bridge.
                if (diff == 1 || diff == 5) continue;

                // Pick the shortest arc along the tile's boundary.
                // Ties (diff == 3, opposite sides) resolved by RNG so
                // rivers occasionally bend around either the "top" or
                // "bottom" of a tile deterministically per seed.
                int arcStep;
                int arcLen;
                if (diff == 2) { arcStep = +1; arcLen = 1; }
                else if (diff == 4) { arcStep = -1; arcLen = 1; }
                else /* diff == 3 */ { arcStep = rng.NextBool() ? +1 : -1; arcLen = 2; }

                int cursor = d1;
                for (int step = 0; step < arcLen; step++)
                {
                    cursor = ((cursor + arcStep) % 6 + 6) % 6;
                    HexDirection connectorDir = (HexDirection)cursor;

                    HexCoord otherRaw = tileCoord.Neighbor(connectorDir);
                    if (!map.TryGetTile(otherRaw, out HexTile otherTile)) break;
                    // Preserve the "no land ↔ water edge" invariant
                    // from WalkDownhill: if the connector would cross
                    // into water, stop bridging here rather than
                    // laying an intermediate edge on the coastline.
                    if (otherTile.LandState == TileLandState.Water) break;

                    edges.Add(RiverEdge.Canonicalize(tileCoord, connectorDir, map.Width));
                }
            }
        }

        private static void AddIncident(Dictionary<HexCoord, List<HexDirection>> map, HexCoord tile, HexDirection dir)
        {
            if (!map.TryGetValue(tile, out List<HexDirection> list))
            {
                list = new List<HexDirection>(3);
                map[tile] = list;
            }
            list.Add(dir);
        }

        // Walks strictly downhill from `source` recording edges into
        // `edges`. Terminates on: reaching water, merging with an
        // existing edge, hitting a local minimum (which is flipped to
        // a lake), or exceeding maxLength.
        //
        // The walk uses the CONTINUOUS Elevation field (float noise),
        // not the discretized ElevationLevel — otherwise elevation
        // smoothing produces same-level plateaus of neighbors and the
        // strict-lower rule terminates the walk after one step. Water
        // tiles' Elevation is naturally lower than land (they were
        // classified as water by the continent-shape stage precisely
        // because they fell below the sea-level threshold), so the
        // "downhill" walk naturally reaches the sea without special-
        // casing. Fallback to ElevationLevel / 5 when Elevation is
        // NaN (test scaffolding that doesn't seed the noise field).
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
                float currentElev = ResolveElevation(current);

                neighborBuffer.Clear();
                bool hasWaterNeighbor = false;
                float lowest = float.PositiveInfinity;
                for (int d = 0; d < 6; d++)
                {
                    HexDirection dir = (HexDirection)d;
                    HexCoord neighborRaw = current.Coord.Neighbor(dir);
                    if (!map.TryGetTile(neighborRaw, out HexTile neighbor)) continue;

                    // Water neighbors are candidates for TERMINATION,
                    // not walk destinations. Rivers never occupy a
                    // land ↔ water edge (Civ V / VI convention) —
                    // they run along land ↔ land edges and meet the
                    // sea at a corner. Track the presence of water so
                    // we know to stop the walk once no downhill land
                    // remains.
                    if (neighbor.LandState == TileLandState.Water)
                    {
                        hasWaterNeighbor = true;
                        continue;
                    }

                    float neighborElev = ResolveElevation(neighbor);
                    if (neighborElev >= currentElev) continue;

                    if (neighborElev < lowest) { neighborBuffer.Clear(); lowest = neighborElev; }
                    if (neighborElev == lowest) neighborBuffer.Add((dir, neighbor));
                }

                if (neighborBuffer.Count == 0)
                {
                    // No downhill LAND neighbor. Two cases:
                    if (hasWaterNeighbor)
                    {
                        // We've reached the coast. Terminate the walk
                        // WITHOUT recording an edge into water — the
                        // last recorded land ↔ land edge is the
                        // river's mouth, and its vertex touching a
                        // water tile forms the visible corner where
                        // the river meets the sea.
                        return;
                    }
                    // True inland local minimum: flip to a lake tile
                    // so the river terminates in water. Skipped when
                    // the buyer roster has no coast biome (unlikely
                    // — Generate validates roles).
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

                current = chosenTile;
            }
        }

        // Continuous elevation for the downhill walk. Falls back to
        // discretized ElevationLevel scaled into [0, 1] when the
        // continuous field is NaN (test scaffolding).
        private static float ResolveElevation(HexTile tile)
        {
            if (!float.IsNaN(tile.Elevation)) return tile.Elevation;
            if (tile.ElevationLevel < 0) return 0f;
            return tile.ElevationLevel / 5f;
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
