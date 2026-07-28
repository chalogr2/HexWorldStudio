using System;
using System.Collections.Generic;
using HexWorldStudio.Content;
using HexWorldStudio.Coordinates;
using HexWorldStudio.Data;
using HexWorldStudio.Generation.Hydrology;
using HexWorldStudio.Generation.Rng;

namespace HexWorldStudio.Generation.Pipeline.Stages
{
    /// <summary>
    /// Corner-network river generation per ADR-0025. Conditions the
    /// elevation field by filling depressions, derives lakes from the
    /// filled basins, then walks rivers from source corners down the
    /// hex-vertex graph to the sea, committing the result to
    /// <see cref="WorldMap.RiverEdges"/> and
    /// <see cref="WorldMap.RiverPaths"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Rivers stay opt-out: with
    /// <see cref="WorldGenerationSettings.RiverDensity"/> at <c>0</c>
    /// the stage is a complete no-op — no fill, no lakes, no edges.
    /// </para>
    /// <para>
    /// <b>Why corners.</b> Three hexes meet at every corner and every
    /// corner has exactly three neighbouring corners, so a step from
    /// one corner to an adjacent one traverses <b>exactly one hex
    /// edge</b>. A river expressed as a corner sequence is therefore
    /// edge-connected by construction: consecutive edges always share
    /// a corner, with no post-hoc bridging pass to leak cases. This
    /// replaces the ADR-0024 tile walk plus connector pass.
    /// </para>
    /// <para>
    /// <b>Two elevation fields, two jobs.</b> The continuous
    /// (depression-filled) field orders corners and drives the
    /// direction of flow — discrete levels are flat across a terrace
    /// and cannot. The discrete <c>ElevationLevel</c> decides step
    /// legality, because it is the terraced structure the player
    /// actually sees.
    /// </para>
    /// <para>
    /// <b>Step legality.</b> A step from corner <c>V</c> to adjacent
    /// corner <c>V'</c> across edge <c>E</c> is legal only when all of:
    /// </para>
    /// <list type="bullet">
    /// <item><description><b>Land</b> — both tiles flanking <c>E</c>
    /// are land, so no river edge ever lies on a shoreline.</description></item>
    /// <item><description><b>Descent</b> — <c>V'</c> is strictly lower
    /// than <c>V</c> on the filled continuous field.</description></item>
    /// <item><description><b>Channel or fall</b> — either the two tiles
    /// flanking <c>E</c> share an <c>ElevationLevel</c> (a channel
    /// running along a terrace), or they differ by at most
    /// <see cref="WorldGenerationSettings.MaxWaterfallDrop"/> levels
    /// <i>and</i> the previous step was a channel (a single-edge
    /// waterfall crossing a terrace boundary). Consecutive falls are
    /// forbidden, so a river never staircases down a cliff
    /// face.</description></item>
    /// <item><description><b>Continuity</b> — the step's
    /// <c>FromLevel</c> matches the previous step's <c>ToLevel</c>, so
    /// the water surface never teleports between terraces.</description></item>
    /// </list>
    /// <para>
    /// <b>Termination.</b> The walk stops the moment it arrives at a
    /// corner touching any water tile. This is what keeps rivers from
    /// running <i>along</i> a coastline: a second step can never be
    /// taken while adjacent to water, so the river meets the sea head
    /// on and ends there. Combined with the Land rule, the mouth
    /// corner touches water while no edge lies on the shore.
    /// </para>
    /// <para>
    /// <b>Search.</b> The terrace constraint is a hard filter, so a
    /// purely greedy walk strands often. The walk is a depth-first
    /// search that prefers the steepest descent and backtracks when a
    /// branch dies, bounded by an expansion budget. Walks that commit
    /// fewer than
    /// <see cref="WorldGenerationSettings.MinRiverLength"/> edges are
    /// discarded rather than left as stubs.
    /// </para>
    /// <para>
    /// <b>Determinism.</b> One labelled RNG fork
    /// (<c>"river-generation"</c>), consumed in a fixed order: source
    /// corners are enumerated as tiles in raster order × corner index
    /// <c>0..5</c>, taking only corners the tile canonically owns. No
    /// hash-ordered collection is ever iterated while drawing from the
    /// RNG, so the output is byte-identical across runs <i>and</i>
    /// across runtimes.
    /// </para>
    /// </remarks>
    internal sealed class RiverStage : IGenerationStage
    {
        /// <inheritdoc/>
        public string Name => "River";

        // Candidate next step, before legality ordering.
        private readonly struct Candidate
        {
            public readonly HexCorner To;
            public readonly RiverEdge Edge;
            public readonly float Elevation;
            public readonly int FromLevel;
            public readonly int ToLevel;

            public Candidate(HexCorner to, RiverEdge edge, float elevation, int fromLevel, int toLevel)
            {
                To = to;
                Edge = edge;
                Elevation = elevation;
                FromLevel = fromLevel;
                ToLevel = toLevel;
            }
        }

        // One node of the depth-first walk.
        private sealed class Frame
        {
            public HexCorner Corner;
            public List<Candidate> Candidates;
            public int Next;
        }

        /// <inheritdoc/>
        public void Execute(GenerationContext ctx)
        {
            if (ctx == null) throw new ArgumentNullException(nameof(ctx));

            WorldGenerationSettings settings = ctx.Settings;
            if (settings.RiverDensity <= 0f) return;

            WorldMap map = ctx.Map;
            RngService rng = ctx.Rng.Fork("river-generation");

            // 1. Hydrological conditioning. Raw fBm is full of pits;
            //    filling guarantees a descent path to an outlet exists.
            float[] filled = DepressionFill.Fill(map);

            // 2. Lakes come from the filled basins, before any walking,
            //    so the walk sees them as water and rivers terminate at
            //    them naturally. Deriving lakes from the fill rather
            //    than from stranded walks is what keeps them contiguous
            //    instead of single-tile holes.
            BiomeDefinition lakeBiome = FindCoastBiome(settings.Biomes);
            CreateLakes(map, filled, settings.LakeDepthThreshold, lakeBiome);

            // 3. Walk.
            int maxLength = settings.MaxRiverLength > 0 ? settings.MaxRiverLength : 40;
            int minLength = settings.MinRiverLength > 0 ? settings.MinRiverLength : 1;
            int maxDrop = settings.MaxWaterfallDrop > 0 ? settings.MaxWaterfallDrop : 1;

            var edges = new HashSet<RiverEdge>();
            var paths = new List<RiverPath>();
            var committedCorners = new HashSet<HexCorner>();

            var visited = new HashSet<HexCorner>();
            var stack = new List<Frame>();
            var steps = new List<RiverStep>();

            int width = map.Width;

            for (int i = 0; i < map.Tiles.Count; i++)
            {
                HexTile tile = map.Tiles[i];
                if (tile.LandState != TileLandState.Land) continue;

                for (int c = 0; c < HexCorner.CornersPerTile; c++)
                {
                    var naming = new HexCorner(tile.Coord, c);
                    HexCorner corner = HexCorner.Canonicalize(tile.Coord, c, width);

                    // Visit each physical corner exactly once, from the
                    // tile that canonically owns it. Keeps enumeration
                    // order a pure function of the tile array.
                    if (!corner.Equals(naming)) continue;

                    if (!IsSourceEligible(corner, map, settings.RiverSourceMinElevation, out float moisture))
                    {
                        continue;
                    }

                    float probability = settings.RiverDensity * moisture;
                    if (probability <= 0f) continue;
                    if (rng.NextFloat() >= probability) continue;

                    RiverPath path = Walk(
                        corner, map, filled, rng, committedCorners,
                        maxLength, maxDrop, minLength,
                        visited, stack, steps);

                    if (path == null) continue;

                    paths.Add(path);
                    for (int s = 0; s < path.Steps.Count; s++)
                    {
                        edges.Add(path.Steps[s].Edge);
                        committedCorners.Add(path.Steps[s].From);
                        committedCorners.Add(path.Steps[s].To);
                    }
                }
            }

            AssignStrahlerOrders(paths);

            map.SetRivers(edges);
            map.SetRiverPaths(paths);
        }

        // ---- lakes ------------------------------------------------

        // A land tile that filling had to raise by more than the
        // threshold is genuinely submerged in a basin. Those tiles
        // become water. ElevationLevel is deliberately left alone: a
        // mountain lake sits at its terrace height, not at sea level,
        // and IsLake is what tells the renderer to respect that.
        private static void CreateLakes(WorldMap map, float[] filled, float threshold, BiomeDefinition lakeBiome)
        {
            if (lakeBiome == null || threshold <= 0f) return;

            for (int i = 0; i < map.Tiles.Count; i++)
            {
                HexTile tile = map.Tiles[i];
                if (tile.LandState != TileLandState.Land) continue;

                float raw = float.IsNaN(tile.Elevation) ? 0f : tile.Elevation;
                if (filled[i] - raw <= threshold) continue;

                tile.LandState = TileLandState.Water;
                tile.Biome = lakeBiome;
                tile.Feature = null;
                tile.Resource = null;
                tile.IsLake = true;
            }
        }

        // ---- source selection -------------------------------------

        // A corner may source a river when all three of its tiles are
        // land and the lowest of them is still high ground. Requiring
        // all three to be land means a source corner is never already
        // touching water, so the walk always gets at least one step.
        private static bool IsSourceEligible(HexCorner corner, WorldMap map, int minElevation, out float moisture)
        {
            moisture = 0f;
            corner.GetIncidentTiles(map.Width, out HexCoord a, out HexCoord b, out HexCoord c);

            if (!map.TryGetTile(a, out HexTile ta) || ta.LandState != TileLandState.Land) return false;
            if (!map.TryGetTile(b, out HexTile tb) || tb.LandState != TileLandState.Land) return false;
            if (!map.TryGetTile(c, out HexTile tc) || tc.LandState != TileLandState.Land) return false;

            int lowest = ta.ElevationLevel;
            if (tb.ElevationLevel < lowest) lowest = tb.ElevationLevel;
            if (tc.ElevationLevel < lowest) lowest = tc.ElevationLevel;
            if (lowest < minElevation) return false;

            moisture = (Sanitize(ta.Moisture) + Sanitize(tb.Moisture) + Sanitize(tc.Moisture)) / 3f;
            return true;
        }

        private static float Sanitize(float value) => float.IsNaN(value) ? 0.5f : value;

        // ---- the walk ---------------------------------------------

        // Depth-first descent with backtracking. Returns null when the
        // walk could not commit at least minLength edges.
        //
        // The scratch collections are passed in and cleared here so the
        // per-source allocation cost stays flat across a whole map.
        private static RiverPath Walk(
            HexCorner source,
            WorldMap map,
            float[] filled,
            RngService rng,
            HashSet<HexCorner> committedCorners,
            int maxLength,
            int maxDrop,
            int minLength,
            HashSet<HexCorner> visited,
            List<Frame> stack,
            List<RiverStep> steps)
        {
            visited.Clear();
            stack.Clear();
            steps.Clear();

            if (!TryCornerElevation(source, map, filled, out float sourceElevation)) return null;

            // Already at the sea: nothing to do. Source eligibility
            // rules this out, but the walk stays self-contained.
            if (IsWaterAdjacent(source, map)) return null;

            visited.Add(source);
            stack.Add(NewFrame(source, sourceElevation, prevLevel: -1, prevWasFall: false,
                               map, filled, rng, maxDrop));

            int budget = maxLength * 12;
            int expansions = 0;

            // Deepest descent seen so far. The terrace and continuity
            // rules mean many walks cannot thread all the way to the
            // sea; throwing those away entirely would leave almost no
            // rivers on the map. A river that runs a long way and then
            // peters out inland is a real feature of real terrain, so
            // the best branch is kept as a fallback and only the truly
            // short ones are dropped by the MinRiverLength filter.
            List<RiverStep> deepest = null;

            while (stack.Count > 0 && expansions < budget)
            {
                Frame frame = stack[stack.Count - 1];

                if (frame.Next >= frame.Candidates.Count)
                {
                    visited.Remove(frame.Corner);
                    stack.RemoveAt(stack.Count - 1);
                    // Every frame except the source has an incoming step.
                    if (stack.Count > 0 && steps.Count > 0) steps.RemoveAt(steps.Count - 1);
                    continue;
                }

                Candidate candidate = frame.Candidates[frame.Next++];
                expansions++;

                if (visited.Contains(candidate.To)) continue;

                steps.Add(new RiverStep(frame.Corner, candidate.To, candidate.Edge,
                                        candidate.FromLevel, candidate.ToLevel));

                if (deepest == null || steps.Count > deepest.Count)
                {
                    deepest = new List<RiverStep>(steps);
                }

                // Reached the sea, a lake, or an existing river: done.
                bool reachedWater = IsWaterAdjacent(candidate.To, map);
                if (reachedWater || committedCorners.Contains(candidate.To))
                {
                    return steps.Count >= minLength ? new RiverPath(steps, reachedWater) : null;
                }

                if (steps.Count >= maxLength)
                {
                    steps.RemoveAt(steps.Count - 1);
                    continue;
                }

                if (!TryCornerElevation(candidate.To, map, filled, out float nextElevation))
                {
                    steps.RemoveAt(steps.Count - 1);
                    continue;
                }

                visited.Add(candidate.To);
                stack.Add(NewFrame(candidate.To, nextElevation,
                                   prevLevel: candidate.ToLevel,
                                   prevWasFall: candidate.FromLevel != candidate.ToLevel,
                                   map, filled, rng, maxDrop));
            }

            // No branch reached water. Fall back to the longest descent
            // found, if it is substantial enough to read as a river.
            if (deepest != null && deepest.Count >= minLength)
            {
                return new RiverPath(deepest, reachesWater: false);
            }
            return null;
        }

        private static Frame NewFrame(
            HexCorner corner, float elevation, int prevLevel, bool prevWasFall,
            WorldMap map, float[] filled, RngService rng, int maxDrop)
        {
            var frame = new Frame
            {
                Corner = corner,
                Candidates = BuildCandidates(corner, elevation, prevLevel, prevWasFall, map, filled, rng, maxDrop),
                Next = 0,
            };
            return frame;
        }

        // The legal onward steps from `corner`, steepest descent first.
        private static List<Candidate> BuildCandidates(
            HexCorner corner, float elevation, int prevLevel, bool prevWasFall,
            WorldMap map, float[] filled, RngService rng, int maxDrop)
        {
            var list = new List<Candidate>(3);
            int width = map.Width;

            corner.GetAdjacentCorners(width, out HexCorner n0, out HexCorner n1, out HexCorner n2);
            TryAdd(list, corner, n0, elevation, prevLevel, prevWasFall, map, filled, maxDrop);
            TryAdd(list, corner, n1, elevation, prevLevel, prevWasFall, map, filled, maxDrop);
            TryAdd(list, corner, n2, elevation, prevLevel, prevWasFall, map, filled, maxDrop);

            // Steepest descent first, with a stable geometric tie-break
            // so the ordering never depends on collection internals.
            list.Sort(CandidateOrder);

            // Depression filling leaves genuinely flat regions, so exact
            // ties are common there. Breaking them geometrically would
            // drain every flat basin in the same compass direction and
            // read as unnaturally parallel rivers, so the leading tie is
            // broken by the seeded stream instead.
            int tied = 1;
            while (tied < list.Count && list[tied].Elevation == list[0].Elevation) tied++;
            if (tied > 1)
            {
                int pick = rng.NextInt(0, tied);
                if (pick != 0)
                {
                    Candidate swap = list[0];
                    list[0] = list[pick];
                    list[pick] = swap;
                }
            }

            return list;
        }

        private static int CandidateOrder(Candidate x, Candidate y)
        {
            int byElevation = x.Elevation.CompareTo(y.Elevation);
            if (byElevation != 0) return byElevation;
            int byQ = x.To.Coord.Q.CompareTo(y.To.Coord.Q);
            if (byQ != 0) return byQ;
            int byR = x.To.Coord.R.CompareTo(y.To.Coord.R);
            if (byR != 0) return byR;
            return x.To.Index.CompareTo(y.To.Index);
        }

        private static void TryAdd(
            List<Candidate> list, HexCorner from, HexCorner to, float fromElevation,
            int prevLevel, bool prevWasFall, WorldMap map, float[] filled, int maxDrop)
        {
            int width = map.Width;

            if (!from.TryGetSharedEdge(to, width, out RiverEdge edge)) return;

            // Land rule: no river edge may lie on a shoreline.
            if (!map.TryGetTile(edge.Coord, out HexTile t1)) return;
            if (!map.TryGetTile(edge.Coord.Neighbor(edge.Direction), out HexTile t2)) return;
            if (t1.LandState != TileLandState.Land || t2.LandState != TileLandState.Land) return;

            // Descent rule.
            if (!TryCornerElevation(to, map, filled, out float toElevation)) return;
            if (toElevation >= fromElevation) return;

            int high = t1.ElevationLevel >= t2.ElevationLevel ? t1.ElevationLevel : t2.ElevationLevel;
            int low = t1.ElevationLevel <= t2.ElevationLevel ? t1.ElevationLevel : t2.ElevationLevel;

            if (high != low)
            {
                // Waterfall: a terrace boundary may be crossed, never
                // run along. One crossing at a time, bounded height.
                if (prevWasFall) return;
                if (high - low > maxDrop) return;
            }

            // Continuity: the water surface must not teleport between
            // terraces. prevLevel < 0 marks the first step.
            if (prevLevel >= 0 && high != prevLevel) return;

            list.Add(new Candidate(to, edge, toElevation, high, low));
        }

        // ---- helpers ----------------------------------------------

        // A corner's elevation is the mean of its three tiles on the
        // filled field. Averaging (rather than taking the minimum)
        // keeps the descent field smooth and produces far fewer exact
        // ties. Corners on the polar rows have a tile outside vertical
        // bounds and are excluded from the network entirely.
        private static bool TryCornerElevation(HexCorner corner, WorldMap map, float[] filled, out float elevation)
        {
            elevation = 0f;
            corner.GetIncidentTiles(map.Width, out HexCoord a, out HexCoord b, out HexCoord c);

            if (!map.TryGetIndex(a, out int ia)) return false;
            if (!map.TryGetIndex(b, out int ib)) return false;
            if (!map.TryGetIndex(c, out int ic)) return false;

            elevation = (filled[ia] + filled[ib] + filled[ic]) / 3f;
            return true;
        }

        private static bool IsWaterAdjacent(HexCorner corner, WorldMap map)
        {
            corner.GetIncidentTiles(map.Width, out HexCoord a, out HexCoord b, out HexCoord c);
            return IsWater(map, a) || IsWater(map, b) || IsWater(map, c);
        }

        private static bool IsWater(WorldMap map, HexCoord coord) =>
            map.TryGetTile(coord, out HexTile tile) && tile.LandState == TileLandState.Water;

        // ---- Strahler ---------------------------------------------

        // Strahler order over the confluence tree: a headwater is 1,
        // and where two streams of equal order meet the result is one
        // higher. Each path takes the order at its mouth, which is the
        // largest it attains; renderers taper width from 1 at the
        // source up to this at the mouth.
        private static void AssignStrahlerOrders(List<RiverPath> paths)
        {
            if (paths.Count == 0) return;

            // Upstream adjacency: corner -> the corners flowing into it.
            var upstream = new Dictionary<HexCorner, List<HexCorner>>();
            for (int p = 0; p < paths.Count; p++)
            {
                IReadOnlyList<RiverStep> steps = paths[p].Steps;
                for (int s = 0; s < steps.Count; s++)
                {
                    if (!upstream.TryGetValue(steps[s].To, out List<HexCorner> list))
                    {
                        list = new List<HexCorner>(2);
                        upstream[steps[s].To] = list;
                    }
                    list.Add(steps[s].From);
                }
            }

            var order = new Dictionary<HexCorner, int>();
            for (int p = 0; p < paths.Count; p++)
            {
                paths[p].StrahlerOrder = ResolveOrder(paths[p].Mouth, upstream, order);
            }
        }

        // Iterative post-order so deep networks cannot blow the stack.
        private static int ResolveOrder(
            HexCorner root,
            Dictionary<HexCorner, List<HexCorner>> upstream,
            Dictionary<HexCorner, int> order)
        {
            if (order.TryGetValue(root, out int cached)) return cached;

            var pending = new Stack<HexCorner>();
            var expanded = new HashSet<HexCorner>();
            pending.Push(root);

            while (pending.Count > 0)
            {
                HexCorner node = pending.Peek();
                if (order.ContainsKey(node)) { pending.Pop(); continue; }

                if (!upstream.TryGetValue(node, out List<HexCorner> parents) || parents.Count == 0)
                {
                    order[node] = 1;
                    pending.Pop();
                    continue;
                }

                if (expanded.Add(node))
                {
                    // Guard against the pathological case of a cycle
                    // sneaking in: a node already on the stack is
                    // treated as a headwater rather than recursed into.
                    for (int i = 0; i < parents.Count; i++)
                    {
                        if (!order.ContainsKey(parents[i])) pending.Push(parents[i]);
                    }
                    continue;
                }

                int best = 0;
                int bestCount = 0;
                for (int i = 0; i < parents.Count; i++)
                {
                    int value = order.TryGetValue(parents[i], out int v) ? v : 1;
                    if (value > best) { best = value; bestCount = 1; }
                    else if (value == best) bestCount++;
                }

                order[node] = bestCount >= 2 ? best + 1 : best;
                pending.Pop();
            }

            return order.TryGetValue(root, out int result) ? result : 1;
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
