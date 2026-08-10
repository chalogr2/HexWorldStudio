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
    /// filled basins, grows a descent tree <b>inland from the sea</b>,
    /// then reads rivers off that tree into
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
    /// corner has exactly three neighbours, so a step from one corner
    /// to an adjacent one traverses <b>exactly one hex edge</b>. A
    /// river expressed as a corner sequence is edge-connected by
    /// construction.
    /// </para>
    /// <para>
    /// <b>Why the search runs backwards.</b> Walking downhill from a
    /// source and hoping to reach the sea does not work: the terrace
    /// rules are a hard filter, and measured over twelve worlds, 43%
    /// of forward walks stranded inland. Growing a tree outward from
    /// every water-touching corner inverts the problem — a corner is
    /// only ever added to the tree along a legal step from a corner
    /// that already drains to water, so <b>every corner in the tree
    /// has a valid route to the sea by construction</b> and a river
    /// traced back down its parent links cannot strand. Sources are
    /// then chosen among corners the tree actually reached.
    /// </para>
    /// <para>
    /// <b>Two elevation fields, two jobs.</b> The continuous
    /// (depression-filled) field orders corners and decides which way
    /// is downhill. The discrete <c>ElevationLevel</c> decides step
    /// legality, because it is the terraced structure the player sees.
    /// </para>
    /// <para>
    /// <b>Step legality.</b> Flow from corner <c>U</c> to adjacent
    /// corner <c>D</c> across edge <c>E</c> is legal only when all of:
    /// </para>
    /// <list type="bullet">
    /// <item><description><b>Land</b> — both tiles flanking <c>E</c>
    /// are land, so no river edge lies on a shoreline.</description></item>
    /// <item><description><b>Descent</b> — <c>D</c> is strictly lower
    /// than <c>U</c> on the filled field.</description></item>
    /// <item><description><b>Channel or fall</b> — either the tiles
    /// flanking <c>E</c> share an <c>ElevationLevel</c> (a channel
    /// running along a terrace), or they differ by at most
    /// <see cref="WorldGenerationSettings.MaxWaterfallDrop"/> levels
    /// and the step immediately downstream is a channel (a single-edge
    /// waterfall). Consecutive falls are forbidden.</description></item>
    /// <item><description><b>Continuity</b> — the step's terrace level
    /// on arrival matches the level the next step departs at, so the
    /// water surface never jumps between terraces.</description></item>
    /// </list>
    /// <para>
    /// A river must run along a height boundary for at least one edge
    /// per level it descends, and this is forced by the topology
    /// rather than chosen: consecutive river edges always share a
    /// tile, so requiring equal levels on every edge would pin the
    /// shared tile's level and, by induction, hold the whole river on
    /// a single terrace where it could never reach the sea. Measured
    /// over twelve worlds, such edges are 22% of river length, and
    /// pricing them into the route does not reduce that (see
    /// <c>StepCost</c>). What makes them read correctly is the
    /// renderer: the water drops at the upstream corner and runs at
    /// the foot of the cliff rather than along its top.
    /// </para>
    /// <para>
    /// <b>Roots and termination.</b> Tree roots are the corners
    /// touching water; expansion never enters another water-touching
    /// corner. So a river's mouth is its only corner against the sea,
    /// which is what stops rivers running <i>along</i> a coastline.
    /// </para>
    /// <para>
    /// <b>Determinism.</b> One labelled RNG fork
    /// (<c>"river-generation"</c>), drawn only while iterating tiles in
    /// raster order × corner index. The tree itself uses no randomness
    /// and breaks cost ties on an explicit state ordering, so it is a
    /// pure function of the map.
    /// </para>
    /// </remarks>
    internal sealed class RiverStage : IGenerationStage
    {
        // Every step costs the same, so the tree is a shortest-route
        // tree. A penalty on waterfall steps was tried and measured at
        // 1, 3, 6 and 20 against a channel's 1: the share of river
        // length lying on a terrace boundary stayed at 22-25% in every
        // case. The drops are structurally required rather than
        // chosen -- descending N levels costs N boundary edges however
        // the route is priced -- so there is no cheaper alternative
        // for a penalty to steer toward, and the knob was dropped
        // rather than left in looking meaningful.
        private const int StepCost = 1;

        /// <inheritdoc/>
        public string Name => "River";

        // A node of the descent search. The corner alone is not enough
        // state: continuity means the legal onward steps depend on the
        // terrace the water is on when it gets here, and the
        // no-two-falls rule means they also depend on whether the step
        // immediately downstream was a fall.
        private readonly struct Node : IEquatable<Node>
        {
            public readonly HexCorner Corner;
            public readonly int Level;      // terrace on arrival; -1 at a mouth
            public readonly bool WasFall;   // the step leaving here is a fall

            public Node(HexCorner corner, int level, bool wasFall)
            {
                Corner = corner;
                Level = level;
                WasFall = wasFall;
            }

            public bool Equals(Node other) =>
                Corner.Equals(other.Corner) && Level == other.Level && WasFall == other.WasFall;

            public override bool Equals(object obj) => obj is Node n && Equals(n);

            public override int GetHashCode()
            {
                unchecked
                {
                    int h = Corner.GetHashCode();
                    h = (h * 397) ^ Level;
                    return (h * 397) ^ (WasFall ? 1 : 0);
                }
            }

            // Total order used to break cost ties, so the tree is a
            // pure function of the map rather than of hash iteration.
            public static int Compare(Node a, Node b)
            {
                int c = a.Corner.Coord.Q.CompareTo(b.Corner.Coord.Q);
                if (c != 0) return c;
                c = a.Corner.Coord.R.CompareTo(b.Corner.Coord.R);
                if (c != 0) return c;
                c = a.Corner.Index.CompareTo(b.Corner.Index);
                if (c != 0) return c;
                c = a.Level.CompareTo(b.Level);
                if (c != 0) return c;
                return a.WasFall.CompareTo(b.WasFall);
            }
        }

        // The step taken from a node toward the sea.
        private readonly struct Link
        {
            public readonly Node Downstream;
            public readonly RiverEdge Edge;
            public readonly int FromLevel;
            public readonly int ToLevel;

            public Link(Node downstream, RiverEdge edge, int fromLevel, int toLevel)
            {
                Downstream = downstream;
                Edge = edge;
                FromLevel = fromLevel;
                ToLevel = toLevel;
            }
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

            // 2. Lakes come from the filled basins, before any routing,
            //    so they are contiguous and act as river outlets.
            BiomeDefinition lakeBiome = FindCoastBiome(settings.Biomes);
            CreateLakes(map, filled, settings.LakeDepthThreshold, lakeBiome);

            int maxLength = settings.MaxRiverLength > 0 ? settings.MaxRiverLength : 40;
            int minLength = settings.MinRiverLength > 0 ? settings.MinRiverLength : 1;
            int maxDrop = settings.MaxWaterfallDrop > 0 ? settings.MaxWaterfallDrop : 1;

            // 3. Grow the descent tree inland from every water-touching
            //    corner. Everything it reaches can reach the sea.
            Dictionary<HexCorner, Node> bestAtCorner = BuildDescentTree(
                map, filled, maxDrop, out Dictionary<Node, Link> links);

            // 4. Pick sources and trace them down the tree.
            var edges = new HashSet<RiverEdge>();
            var paths = new List<RiverPath>();
            var committed = new HashSet<HexCorner>();
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

                    // Visit each physical corner once, from the tile
                    // that canonically owns it, so the RNG is drawn in
                    // an order fixed by the tile array.
                    if (!corner.Equals(naming)) continue;

                    if (!IsSourceEligible(corner, map, settings.RiverSourceMinElevation, out float moisture))
                    {
                        continue;
                    }

                    float probability = settings.RiverDensity * moisture;
                    if (probability <= 0f) continue;
                    if (rng.NextFloat() >= probability) continue;

                    // Unreachable corners simply produce no river.
                    if (!bestAtCorner.TryGetValue(corner, out Node start)) continue;

                    RiverPath path = Trace(start, links, committed, steps, maxLength, minLength);
                    if (path == null) continue;

                    paths.Add(path);
                    for (int s = 0; s < path.Steps.Count; s++)
                    {
                        edges.Add(path.Steps[s].Edge);
                        committed.Add(path.Steps[s].From);
                        committed.Add(path.Steps[s].To);
                    }
                }
            }

            AssignStrahlerOrders(paths);

            map.SetRivers(edges);
            map.SetRiverPaths(paths);
        }

        // ---- the descent tree -------------------------------------

        // Dijkstra outward from every water-touching corner, following
        // legal steps in reverse (uphill). Returns, per corner, the
        // cheapest node reaching it, plus the link table used to walk
        // back down to the sea.
        private static Dictionary<HexCorner, Node> BuildDescentTree(
            WorldMap map, float[] filled, int maxDrop, out Dictionary<Node, Link> links)
        {
            var cost = new Dictionary<Node, int>();
            links = new Dictionary<Node, Link>();
            var heap = new NodeHeap(1024);
            int width = map.Width;

            // Roots: corners that touch water. Level -1 means "the
            // first step into here may arrive on any terrace".
            for (int i = 0; i < map.Tiles.Count; i++)
            {
                HexTile tile = map.Tiles[i];
                for (int c = 0; c < HexCorner.CornersPerTile; c++)
                {
                    var naming = new HexCorner(tile.Coord, c);
                    HexCorner corner = HexCorner.Canonicalize(tile.Coord, c, width);
                    if (!corner.Equals(naming)) continue;
                    if (!TouchesWater(corner, map)) continue;

                    var root = new Node(corner, -1, false);
                    if (cost.ContainsKey(root)) continue;
                    cost[root] = 0;
                    heap.Push(0, root);
                }
            }

            while (heap.Count > 0)
            {
                heap.Pop(out int currentCost, out Node node);
                if (!cost.TryGetValue(node, out int known) || currentCost > known) continue;

                node.Corner.GetAdjacentCorners(width, out HexCorner a, out HexCorner b, out HexCorner c2);
                Relax(node, a, currentCost, map, filled, maxDrop, cost, links, heap);
                Relax(node, b, currentCost, map, filled, maxDrop, cost, links, heap);
                Relax(node, c2, currentCost, map, filled, maxDrop, cost, links, heap);
            }

            // Cheapest node per corner. Ties broken on the node order,
            // so the result does not depend on hash iteration.
            var best = new Dictionary<HexCorner, Node>();
            var bestCost = new Dictionary<HexCorner, int>();
            foreach (KeyValuePair<Node, int> kv in cost)
            {
                HexCorner corner = kv.Key.Corner;
                if (!bestCost.TryGetValue(corner, out int existing)
                    || kv.Value < existing
                    || (kv.Value == existing && Node.Compare(kv.Key, best[corner]) < 0))
                {
                    bestCost[corner] = kv.Value;
                    best[corner] = kv.Key;
                }
            }
            return best;
        }

        // Considers the step upstream -> node (i.e. water flowing from
        // `upstream` into `node`) and records it if legal and cheaper.
        private static void Relax(
            Node node, HexCorner upstream, int currentCost,
            WorldMap map, float[] filled, int maxDrop,
            Dictionary<Node, int> cost, Dictionary<Node, Link> links, NodeHeap heap)
        {
            int width = map.Width;

            // A river's only water-touching corner is its mouth, so the
            // tree never expands into one. This is what prevents rivers
            // running along a shoreline.
            if (TouchesWater(upstream, map)) return;

            if (!upstream.TryGetSharedEdge(node.Corner, width, out RiverEdge edge)) return;

            // Land rule.
            if (!map.TryGetTile(edge.Coord, out HexTile t1)) return;
            if (!map.TryGetTile(edge.Coord.Neighbor(edge.Direction), out HexTile t2)) return;
            if (t1.LandState != TileLandState.Land || t2.LandState != TileLandState.Land) return;

            // Descent rule: water must fall from upstream to node.
            if (!TryCornerElevation(upstream, map, filled, out float upElevation)) return;
            if (!TryCornerElevation(node.Corner, map, filled, out float downElevation)) return;
            if (downElevation >= upElevation) return;

            int high = t1.ElevationLevel >= t2.ElevationLevel ? t1.ElevationLevel : t2.ElevationLevel;
            int low = t1.ElevationLevel <= t2.ElevationLevel ? t1.ElevationLevel : t2.ElevationLevel;
            bool isFall = high != low;

            if (isFall)
            {
                // One crossing at a time, bounded height, and never
                // straight into another crossing downstream.
                if (node.WasFall) return;
                if (high - low > maxDrop) return;
            }

            // Continuity: this step arrives on terrace `low`, which must
            // be the terrace the next step downstream departs from.
            // Level -1 marks a mouth, where anything is acceptable.
            if (node.Level >= 0 && low != node.Level) return;

            var next = new Node(upstream, high, isFall);
            int stepCost = currentCost + StepCost;

            bool known = cost.TryGetValue(next, out int existing);
            bool better = !known
                          || stepCost < existing
                          || (stepCost == existing && Node.Compare(node, links[next].Downstream) < 0);
            if (!better) return;

            cost[next] = stepCost;
            links[next] = new Link(node, edge, high, low);
            heap.Push(stepCost, next);
        }

        // ---- tracing ----------------------------------------------

        // Walks parent links from a source down to the sea, or until it
        // joins a river already committed. Returns null when the result
        // is too short to read as a river.
        private static RiverPath Trace(
            Node start, Dictionary<Node, Link> links, HashSet<HexCorner> committed,
            List<RiverStep> steps, int maxLength, int minLength)
        {
            steps.Clear();

            Node current = start;
            bool reachedWater = false;

            while (steps.Count < maxLength && links.TryGetValue(current, out Link link))
            {
                steps.Add(new RiverStep(current.Corner, link.Downstream.Corner,
                                        link.Edge, link.FromLevel, link.ToLevel));

                // Joining an existing river: its tail is already drawn,
                // and it already drains to water.
                if (committed.Contains(link.Downstream.Corner))
                {
                    reachedWater = true;
                    break;
                }

                current = link.Downstream;

                // A root has no link; reaching one means we are at the
                // shoreline.
                if (!links.ContainsKey(current))
                {
                    reachedWater = true;
                    break;
                }
            }

            if (steps.Count < minLength) return null;
            return new RiverPath(steps, reachedWater);
        }

        // ---- lakes ------------------------------------------------

        // A land tile that filling had to raise by more than the
        // threshold is genuinely submerged in a basin. ElevationLevel
        // is deliberately left alone: a mountain lake sits at its
        // terrace height, not at sea level, and IsLake is what tells
        // the renderer to respect that.
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
        // all three to be land keeps sources off the coast.
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

        // ---- helpers ----------------------------------------------

        // A corner's elevation is the mean of its three tiles on the
        // filled field. Averaging rather than taking the minimum keeps
        // the descent field smooth and produces far fewer exact ties.
        // Corners on the polar rows have a tile outside vertical bounds
        // and are excluded from the network entirely.
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

        private static bool TouchesWater(HexCorner corner, WorldMap map)
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
        // largest it attains.
        private static void AssignStrahlerOrders(List<RiverPath> paths)
        {
            if (paths.Count == 0) return;

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
                    // A node already on the stack is treated as a
                    // headwater rather than recursed into, so a cycle
                    // cannot hang the pass.
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

        // Binary min-heap over (cost, node). Unity's scripting runtime
        // targets netstandard2.1, which predates PriorityQueue, and the
        // project takes no third-party dependencies. Ties break on the
        // node order so pops are fully determined by the input.
        private sealed class NodeHeap
        {
            private int[] _costs;
            private Node[] _nodes;

            public int Count { get; private set; }

            public NodeHeap(int capacity)
            {
                if (capacity < 4) capacity = 4;
                _costs = new int[capacity];
                _nodes = new Node[capacity];
            }

            public void Push(int cost, Node node)
            {
                if (Count == _costs.Length) Grow();

                int i = Count++;
                _costs[i] = cost;
                _nodes[i] = node;

                while (i > 0)
                {
                    int parent = (i - 1) / 2;
                    if (!Less(i, parent)) break;
                    Swap(i, parent);
                    i = parent;
                }
            }

            public void Pop(out int cost, out Node node)
            {
                cost = _costs[0];
                node = _nodes[0];

                int last = --Count;
                _costs[0] = _costs[last];
                _nodes[0] = _nodes[last];

                int i = 0;
                while (true)
                {
                    int left = 2 * i + 1;
                    if (left >= Count) break;
                    int right = left + 1;
                    int smallest = (right < Count && Less(right, left)) ? right : left;
                    if (!Less(smallest, i)) break;
                    Swap(i, smallest);
                    i = smallest;
                }
            }

            private bool Less(int a, int b) =>
                _costs[a] < _costs[b]
                || (_costs[a] == _costs[b] && Node.Compare(_nodes[a], _nodes[b]) < 0);

            private void Swap(int a, int b)
            {
                (_costs[a], _costs[b]) = (_costs[b], _costs[a]);
                (_nodes[a], _nodes[b]) = (_nodes[b], _nodes[a]);
            }

            private void Grow()
            {
                int capacity = _costs.Length * 2;
                Array.Resize(ref _costs, capacity);
                Array.Resize(ref _nodes, capacity);
            }
        }
    }
}
