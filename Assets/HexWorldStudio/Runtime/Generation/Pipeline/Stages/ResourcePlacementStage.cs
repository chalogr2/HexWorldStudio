using System;
using System.Collections.Generic;
using HexWorldStudio.Content;
using HexWorldStudio.Coordinates;
using HexWorldStudio.Data;
using HexWorldStudio.Generation.Rng;

namespace HexWorldStudio.Generation.Pipeline.Stages
{
    /// <summary>
    /// Optional resource placement stage per ADR-0022. Walks every
    /// tile, attempts a placement with probability
    /// <see cref="WorldGenerationSettings.ResourceDensity"/>, filters
    /// eligible resources via
    /// <see cref="ResourceDefinition.MatchesEnvironment"/>, enforces
    /// <see cref="ResourceDefinition.MinimumSpacing"/> against tiles
    /// already carrying the same resource, and picks weighted by
    /// <see cref="ResourceDefinition.Rarity"/>. Writes the chosen
    /// resource to <see cref="HexTile.Resource"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Resource placement is opt-in: when
    /// <see cref="WorldGenerationSettings.Resources"/> is null or
    /// empty, the stage is a complete no-op and every tile's
    /// <see cref="HexTile.Resource"/> stays <c>null</c>. Worlds
    /// generated with no resources behave exactly as Slice 12 — the
    /// stage adds no overhead when off.
    /// </para>
    /// <para>
    /// Determinism: uses a single labeled RNG fork
    /// (<c>"resource-placement"</c>) so adding / reordering other
    /// stages does not shift the seeded stream. Same seed + same
    /// settings produces byte-identical resource placements.
    /// </para>
    /// <para>
    /// Algorithm: iterate tiles in canonical index order; for each,
    /// roll a placement test against <c>ResourceDensity</c>; if it
    /// passes, gather eligible resources for this tile (passes both
    /// environment filter AND spacing constraint against previously-
    /// placed positions); weighted-random-pick by <c>Rarity</c>; assign
    /// to <c>tile.Resource</c>; record the position so subsequent
    /// placements can honor spacing. Cylindrical wrap is handled via
    /// <see cref="WorldMap.TryGetIndex"/> in the spacing BFS.
    /// </para>
    /// <para>
    /// Cost: <c>O(tiles × resources × spacing²)</c>. With v1 map
    /// sizes (sub-100×60), ~20 resources, and spacing radius ≤ 5,
    /// total cost is well under 100k operations per generation —
    /// negligible compared to the noise stages.
    /// </para>
    /// </remarks>
    internal sealed class ResourcePlacementStage : IGenerationStage
    {
        /// <inheritdoc/>
        public string Name => "ResourcePlacement";

        /// <inheritdoc/>
        public void Execute(GenerationContext ctx)
        {
            if (ctx == null) throw new ArgumentNullException(nameof(ctx));

            IReadOnlyList<ResourceDefinition> resources = ctx.Settings.Resources;
            float density = ctx.Settings.ResourceDensity;

            // Opt-in: empty roster or zero density → stage no-ops.
            if (resources == null || resources.Count == 0) return;
            if (density <= 0f) return;

            WorldMap map = ctx.Map;
            int tileCount = map.Tiles.Count;
            RngService rng = ctx.Rng.Fork("resource-placement");

            // Tracks every coord where each resource has already been
            // placed, so the spacing check is a small set-lookup
            // rather than a per-tile scan. Keyed by resource so two
            // different resources (e.g., Wheat and Cattle) at adjacent
            // tiles do not block each other.
            var placedByResource = new Dictionary<ResourceDefinition, List<HexCoord>>(resources.Count);

            // Scratch buffers reused across tiles to avoid per-tile allocations.
            var eligible = new List<ResourceDefinition>(resources.Count);
            var weights  = new List<float>(resources.Count);

            for (int i = 0; i < tileCount; i++)
            {
                // Per-tile placement coin flip. Cheap early-out for
                // the (typically vast) majority of tiles that get no
                // resource at all.
                if (rng.NextFloat() >= density) continue;

                HexTile tile = map.Tiles[i];

                eligible.Clear();
                weights.Clear();
                float totalWeight = 0f;

                for (int r = 0; r < resources.Count; r++)
                {
                    ResourceDefinition res = resources[r];
                    if (res == null) continue;
                    if (res.Rarity <= 0f) continue;

                    if (!res.MatchesEnvironment(tile.Biome, tile.Feature, tile.ElevationLevel)) continue;

                    if (!PassesSpacing(tile.Coord, res, placedByResource, map)) continue;

                    eligible.Add(res);
                    weights.Add(res.Rarity);
                    totalWeight += res.Rarity;
                }

                if (eligible.Count == 0 || totalWeight <= 0f) continue;

                // Weighted draw: uniform [0, totalWeight), walk the array.
                float pick = rng.NextFloat() * totalWeight;
                float cumulative = 0f;
                ResourceDefinition chosen = eligible[eligible.Count - 1]; // defensive fallback for fp drift
                for (int e = 0; e < eligible.Count; e++)
                {
                    cumulative += weights[e];
                    if (pick < cumulative)
                    {
                        chosen = eligible[e];
                        break;
                    }
                }

                tile.Resource = chosen;

                if (!placedByResource.TryGetValue(chosen, out List<HexCoord> placements))
                {
                    placements = new List<HexCoord>(8);
                    placedByResource[chosen] = placements;
                }
                placements.Add(tile.Coord);
            }
        }

        // True iff no previously-placed instance of the same resource
        // sits within MinimumSpacing hex steps of the candidate
        // coord. Linear scan over prior placements — cheap because
        // resource placements are sparse by design (Civ-style ~10-20%
        // of tiles, and each placement list contains only its own
        // type).
        private static bool PassesSpacing(
            HexCoord candidate,
            ResourceDefinition resource,
            Dictionary<ResourceDefinition, List<HexCoord>> placedByResource,
            WorldMap map)
        {
            if (resource.MinimumSpacing <= 0) return true;
            if (!placedByResource.TryGetValue(resource, out List<HexCoord> existing)) return true;

            int radius = resource.MinimumSpacing;
            for (int i = 0; i < existing.Count; i++)
            {
                if (WrappedDistance(candidate, existing[i], map) < radius) return false;
            }
            return true;
        }

        // Hex distance on a cylindrical map. The horizontal axis
        // wraps, so the shorter of the two distances around the
        // cylinder is used. Vertical axis is bounded so straightforward.
        private static int WrappedDistance(HexCoord a, HexCoord b, WorldMap map)
        {
            int direct = a.Distance(b);

            // Compare with the wrapped equivalent: shift `b` by a full
            // map width in each direction and take the closer one.
            // HexCoord.Distance is cheap (axial subtraction); checking
            // both shifts is two extra calls.
            int width = map.Width;
            HexCoord bLeft  = new HexCoord(b.Q - width, b.R);
            HexCoord bRight = new HexCoord(b.Q + width, b.R);

            int wrappedLeft  = a.Distance(bLeft);
            int wrappedRight = a.Distance(bRight);

            int best = direct;
            if (wrappedLeft  < best) best = wrappedLeft;
            if (wrappedRight < best) best = wrappedRight;
            return best;
        }
    }
}
