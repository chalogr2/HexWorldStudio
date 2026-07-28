using System;
using HexWorldStudio.Coordinates;
using HexWorldStudio.Data;

namespace HexWorldStudio.Generation.Hydrology
{
    /// <summary>
    /// Priority-flood depression filling over a cylindrical hex map
    /// (ADR-0025). Produces a copy of the continuous elevation field in
    /// which every land tile has a monotonically non-increasing path to
    /// an outlet — no interior pits remain.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Why: the continuous <c>Elevation</c> field is raw fBm Perlin,
    /// which is full of local minima. A downhill walk over it strands
    /// constantly, and under ADR-0024 every stranded walk punched a
    /// spurious lake into the terrain. Filling first means "walk
    /// downhill until you reach the sea" actually terminates at the sea.
    /// </para>
    /// <para>
    /// Outlets are all water tiles plus the top and bottom rows. The
    /// poles are hard map edges (the vertical axis does not wrap), so
    /// seeding them guarantees the fill is well defined even for a
    /// world with no water at all.
    /// </para>
    /// <para>
    /// The source field is never mutated — <see cref="HexTile.Elevation"/>
    /// keeps its raw value so no other stage's behaviour shifts and no
    /// existing seed changes for non-river output. Callers get a fresh
    /// array indexed the same way as <see cref="WorldMap.Tiles"/>.
    /// </para>
    /// <para>
    /// Determinism: heap ties break on tile index, so the result is
    /// byte-identical across runs and runtimes. No RNG is involved.
    /// Cost is <c>O(n log n)</c>; on a 100×60 map that is well under a
    /// millisecond.
    /// </para>
    /// </remarks>
    internal static class DepressionFill
    {
        /// <summary>
        /// Slope imposed across filled regions so they are never
        /// perfectly flat. Small enough to be invisible against a
        /// normalized <c>[0, 1]</c> elevation field even after
        /// accumulating along a long fill chain, large enough to
        /// survive single-precision comparison.
        /// </summary>
        internal const float Epsilon = 1e-5f;

        /// <summary>
        /// Returns a depression-filled copy of <paramref name="map"/>'s
        /// continuous elevation field, indexed to match
        /// <see cref="WorldMap.Tiles"/>.
        /// </summary>
        /// <param name="map">The map to read elevations from. Must be non-null.</param>
        public static float[] Fill(WorldMap map)
        {
            if (map == null) throw new ArgumentNullException(nameof(map));

            int count = map.Tiles.Count;
            var filled = new float[count];
            var resolved = new bool[count];
            var heap = new MinHeap(count);

            int lastRow = map.Height - 1;
            for (int i = 0; i < count; i++)
            {
                HexTile tile = map.Tiles[i];
                float elevation = float.IsNaN(tile.Elevation) ? 0f : tile.Elevation;
                filled[i] = elevation;

                (int _, int row) = tile.Coord.ToOffsetOddR();
                bool isOutlet = tile.LandState == TileLandState.Water || row == 0 || row == lastRow;
                if (isOutlet)
                {
                    resolved[i] = true;
                    heap.Push(elevation, i);
                }
            }

            // Classic priority flood: repeatedly take the lowest tile on
            // the frontier and raise each unresolved neighbour to at
            // least that tile's water level. Because the frontier is
            // consumed in increasing order, the first time a tile is
            // reached is via its lowest spill path, so one visit per
            // tile suffices.
            while (heap.Count > 0)
            {
                heap.Pop(out float level, out int index);
                HexCoord coord = map.Tiles[index].Coord;

                for (int d = 0; d < 6; d++)
                {
                    HexCoord neighbor = coord.Neighbor((HexDirection)d);
                    if (!map.TryGetIndex(neighbor, out int ni)) continue;
                    if (resolved[ni]) continue;

                    resolved[ni] = true;

                    // Raise to just *above* the spill level, not level
                    // with it. A plain max() leaves filled basins
                    // perfectly flat, and a walk that requires strict
                    // descent cannot cross a flat region at all — so
                    // the rivers that most need the fill would strand
                    // in the middle of it. The epsilon gradient points
                    // back down the fill direction, which is toward the
                    // outlet, so filled basins drain the way water
                    // actually would. This is the standard
                    // priority-flood-with-epsilon variant.
                    float floor = level + Epsilon;
                    if (filled[ni] < floor) filled[ni] = floor;
                    heap.Push(filled[ni], ni);
                }
            }

            return filled;
        }

        // Binary min-heap over (elevation, tileIndex). Unity's scripting
        // runtime targets netstandard2.1, which predates
        // System.Collections.Generic.PriorityQueue, and the project
        // takes no third-party dependencies -- so this is hand-rolled.
        //
        // Ties break on tile index so the pop order is fully determined
        // by the input, which the determinism contract requires.
        private sealed class MinHeap
        {
            private float[] _priorities;
            private int[] _values;

            public int Count { get; private set; }

            public MinHeap(int capacity)
            {
                if (capacity < 4) capacity = 4;
                _priorities = new float[capacity];
                _values = new int[capacity];
            }

            public void Push(float priority, int value)
            {
                if (Count == _priorities.Length) Grow();

                int i = Count++;
                _priorities[i] = priority;
                _values[i] = value;

                while (i > 0)
                {
                    int parent = (i - 1) / 2;
                    if (!Less(i, parent)) break;
                    Swap(i, parent);
                    i = parent;
                }
            }

            public void Pop(out float priority, out int value)
            {
                priority = _priorities[0];
                value = _values[0];

                int last = --Count;
                _priorities[0] = _priorities[last];
                _values[0] = _values[last];

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
                _priorities[a] < _priorities[b]
                || (_priorities[a] == _priorities[b] && _values[a] < _values[b]);

            private void Swap(int a, int b)
            {
                (_priorities[a], _priorities[b]) = (_priorities[b], _priorities[a]);
                (_values[a], _values[b]) = (_values[b], _values[a]);
            }

            private void Grow()
            {
                int capacity = _priorities.Length * 2;
                Array.Resize(ref _priorities, capacity);
                Array.Resize(ref _values, capacity);
            }
        }
    }
}
