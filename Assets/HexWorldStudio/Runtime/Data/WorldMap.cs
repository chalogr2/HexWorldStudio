using System;
using System.Collections.Generic;
using HexWorldStudio.Coordinates;

namespace HexWorldStudio.Data
{
    /// <summary>
    /// Pure-data root container produced by world generation. Holds the
    /// rectangular hex grid of <see cref="HexTile"/> instances plus the
    /// settings echo used to produce them.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <see cref="WorldMap"/> is rectangular and cylindrical: the
    /// horizontal axis wraps (column <c>Width - 1</c> connects to column
    /// <c>0</c>), the vertical axis is bounded (top and bottom rows are
    /// the poles). Lookup and indexing wrap horizontally automatically.
    /// Rationale and trade-offs are in ADR-0009.
    /// </para>
    /// <para>
    /// Mutability follows the documented contract from ADR-0005:
    /// dimensions and seed are immutable after construction; per-tile
    /// state may be mutated by generation stages and (with care) by
    /// buyers post-generation.
    /// </para>
    /// <para>
    /// Buyers receive a <see cref="WorldMap"/> from the generation entry
    /// point; the constructor is internal so the generation pipeline is
    /// the only legitimate producer of fully-populated maps.
    /// </para>
    /// </remarks>
    public sealed class WorldMap
    {
        private readonly HexTile[] _tiles;

        // River state (ADR-0024). Empty until RiverStage runs. The
        // list is deterministically sorted for stable iteration; the
        // parallel set backs O(1) HasRiverAt / CountRiverEdgesAround
        // queries. Both stay in sync via SetRivers.
        private RiverEdge[] _riverEdges = Array.Empty<RiverEdge>();
        private HashSet<RiverEdge> _riverEdgeSet;

        /// <summary>The map's width in tiles (number of columns).</summary>
        public int Width { get; }

        /// <summary>The map's height in tiles (number of rows).</summary>
        public int Height { get; }

        /// <summary>
        /// The seed used to generate this map. Echoed from the generation
        /// settings so callers can reproduce the same map by feeding the
        /// same seed back into <c>HexWorldGenerator.Generate</c>.
        /// </summary>
        public ulong Seed { get; }

        /// <summary>
        /// Read-only view of every tile in raster order
        /// (<c>row * Width + col</c>). Buyers may iterate freely.
        /// </summary>
        public IReadOnlyList<HexTile> Tiles => _tiles;

        /// <summary>
        /// Every river segment on the map, in canonical form and stable
        /// deterministic order (sorted by owner Q, R, direction).
        /// Empty until the <c>RiverStage</c> runs (or if the buyer
        /// disables rivers via <c>settings.RiverDensity = 0</c>).
        /// See <see cref="RiverEdge"/> and ADR-0024.
        /// </summary>
        public IReadOnlyList<RiverEdge> RiverEdges => _riverEdges;

        /// <summary>
        /// Constructs an empty map (every tile initialized to default
        /// NaN / Unset state) of the given size, recording the seed for
        /// later reproduction.
        /// </summary>
        /// <param name="width">Number of columns. Must be positive.</param>
        /// <param name="height">Number of rows. Must be positive.</param>
        /// <param name="seed">The generation seed to echo.</param>
        /// <exception cref="ArgumentOutOfRangeException">
        /// Thrown when <paramref name="width"/> or <paramref name="height"/>
        /// is non-positive.
        /// </exception>
        internal WorldMap(int width, int height, ulong seed)
        {
            if (width <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(width), width, "Width must be positive.");
            }
            if (height <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(height), height, "Height must be positive.");
            }

            Width = width;
            Height = height;
            Seed = seed;

            _tiles = new HexTile[width * height];
            for (int row = 0; row < height; row++)
            {
                for (int col = 0; col < width; col++)
                {
                    int index = row * width + col;
                    HexCoord coord = HexCoordExtensions.FromOffsetOddR(col, row);
                    _tiles[index] = new HexTile(coord, index);
                }
            }
        }

        /// <summary>
        /// Returns <c>true</c> if <paramref name="coord"/> resolves to a
        /// real tile inside this map. Horizontal wrap means any column
        /// is in bounds; only the row constraint can fail.
        /// </summary>
        public bool Contains(HexCoord coord) =>
            CoordinateSpace.IsInVerticalBounds(coord, Height);

        /// <summary>
        /// Wraps <paramref name="coord"/>'s horizontal component into
        /// canonical range. Row is unchanged; if it was out of vertical
        /// bounds before, it remains so. Pair with <see cref="Contains"/>
        /// for full validation.
        /// </summary>
        /// <remarks>
        /// Use this to canonicalize coords before storing them in
        /// buyer-side dictionaries, comparing them with <c>==</c>, or
        /// indexing parallel arrays.
        /// <c>HexCoord</c> equality is a literal Q/R comparison, so a
        /// coord and its normalized equivalent will appear unequal even
        /// though they refer to the same tile.
        /// </remarks>
        public HexCoord Normalize(HexCoord coord) =>
            CoordinateSpace.Normalize(coord, Width);

        /// <summary>
        /// Attempts to look up the tile at <paramref name="coord"/>.
        /// Wraps the horizontal component automatically; returns
        /// <c>false</c> (without throwing and without leaking null
        /// out of the data layer in a way that requires the caller to
        /// null-check) when the row is outside <c>[0, Height)</c>.
        /// </summary>
        /// <param name="coord">The coord to look up.</param>
        /// <param name="tile">
        /// On <c>true</c>, the resolved tile. On <c>false</c>, <c>null</c>;
        /// callers must check the bool before dereferencing.
        /// </param>
        /// <returns>
        /// <c>true</c> if the coord resolved to a real tile; <c>false</c>
        /// when the row is out of bounds.
        /// </returns>
        public bool TryGetTile(HexCoord coord, out HexTile tile)
        {
            if (!CoordinateSpace.IsInVerticalBounds(coord, Height))
            {
                tile = null;
                return false;
            }

            int index = CoordinateSpace.IndexOf(coord, Width);
            tile = _tiles[index];
            return true;
        }

        /// <summary>
        /// Attempts to compute the tile index for <paramref name="coord"/>.
        /// Wraps horizontally; returns <c>false</c> for out-of-vertical-
        /// bounds rows.
        /// </summary>
        /// <param name="coord">The coord to look up.</param>
        /// <param name="index">
        /// On <c>true</c>, the raster-order tile index in <c>[0, Width*Height)</c>.
        /// On <c>false</c>, <c>-1</c>.
        /// </param>
        public bool TryGetIndex(HexCoord coord, out int index)
        {
            if (!CoordinateSpace.IsInVerticalBounds(coord, Height))
            {
                index = -1;
                return false;
            }

            index = CoordinateSpace.IndexOf(coord, Width);
            return true;
        }

        /// <summary>
        /// Returns the tile at the given raster-order index.
        /// </summary>
        /// <param name="index">A tile index in <c>[0, Width*Height)</c>.</param>
        /// <returns>The tile at that index.</returns>
        /// <exception cref="ArgumentOutOfRangeException">
        /// Thrown when <paramref name="index"/> is outside the tile
        /// array's bounds. Indexed access is the array-style entry point
        /// and treats out-of-range as a programmer error; for
        /// coordinate-based safe lookup, use <see cref="TryGetTile"/>.
        /// </exception>
        public HexTile GetTileByIndex(int index)
        {
            if (index < 0 || index >= _tiles.Length)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(index),
                    index,
                    $"Index must be in [0, {_tiles.Length}).");
            }
            return _tiles[index];
        }

        /// <summary>
        /// Returns <c>true</c> if a river runs along the edge between
        /// <paramref name="coord"/> and its neighbor in
        /// <paramref name="direction"/>. Accepts either endpoint /
        /// direction naming — the query is canonicalized internally.
        /// </summary>
        /// <param name="coord">One endpoint of the queried edge.</param>
        /// <param name="direction">
        /// Direction from <paramref name="coord"/> toward the other
        /// endpoint.
        /// </param>
        public bool HasRiverAt(HexCoord coord, HexDirection direction)
        {
            if (_riverEdgeSet == null || _riverEdgeSet.Count == 0) return false;
            HexCoord canonical = Normalize(coord);
            RiverEdge query = RiverEdge.Canonicalize(canonical, direction, Width);
            return _riverEdgeSet.Contains(query);
        }

        /// <summary>
        /// Returns the number of river edges incident to
        /// <paramref name="coord"/>, in <c>[0, 6]</c>. Convenience
        /// helper for buyers implementing river-adjacency bonuses
        /// (starting positions, fertility scoring, resource yields).
        /// </summary>
        public int CountRiverEdgesAround(HexCoord coord)
        {
            if (_riverEdgeSet == null || _riverEdgeSet.Count == 0) return 0;
            HexCoord canonical = Normalize(coord);
            int count = 0;
            for (int d = 0; d < 6; d++)
            {
                RiverEdge query = RiverEdge.Canonicalize(canonical, (HexDirection)d, Width);
                if (_riverEdgeSet.Contains(query)) count++;
            }
            return count;
        }

        // Commits the pipeline's collected river set into the map.
        // Sorted for deterministic iteration order (RiverEdges is
        // documented as stable). Called once by RiverStage at the end
        // of its run; not exposed to buyers.
        internal void SetRivers(IEnumerable<RiverEdge> edges)
        {
            if (edges == null)
            {
                _riverEdges = Array.Empty<RiverEdge>();
                _riverEdgeSet = null;
                return;
            }

            var set = new HashSet<RiverEdge>();
            foreach (RiverEdge e in edges) set.Add(e);

            RiverEdge[] arr = new RiverEdge[set.Count];
            set.CopyTo(arr);
            Array.Sort(arr, RiverEdgeOrder.Instance);

            _riverEdges = arr;
            _riverEdgeSet = set;
        }

        // Stable order used to sort RiverEdges for deterministic
        // iteration: owner Q, owner R, then direction.
        private sealed class RiverEdgeOrder : IComparer<RiverEdge>
        {
            public static readonly RiverEdgeOrder Instance = new RiverEdgeOrder();

            public int Compare(RiverEdge a, RiverEdge b)
            {
                int cmp = a.Coord.Q.CompareTo(b.Coord.Q);
                if (cmp != 0) return cmp;
                cmp = a.Coord.R.CompareTo(b.Coord.R);
                if (cmp != 0) return cmp;
                return ((int)a.Direction).CompareTo((int)b.Direction);
            }
        }
    }
}
