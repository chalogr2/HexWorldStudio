using System;
using HexWorldStudio.Coordinates;

namespace HexWorldStudio.Data
{
    /// <summary>
    /// A single vertex of the hex grid: the point where exactly three
    /// hexes meet. Identified by an owning tile plus a corner index in
    /// <c>[0, 6)</c>, matching the ring-vertex order the renderer's
    /// <c>HexMesh</c> builds (index <c>0</c> at 90°, increasing
    /// counter-clockwise in 60° steps).
    /// </summary>
    /// <remarks>
    /// <para>
    /// Corners are the natural unit for river topology (ADR-0025).
    /// Each corner has exactly three neighbouring corners, and a step
    /// from one corner to an adjacent corner traverses <b>exactly one
    /// hex edge</b> — so a path expressed as a corner sequence is
    /// edge-connected by construction, with no post-hoc bridging.
    /// </para>
    /// <para>
    /// Because three tiles meet at every corner, the same physical
    /// vertex has three equally valid <c>(tile, index)</c> namings.
    /// Instances used as dictionary keys or set members must be in
    /// <b>canonical form</b>: the naming whose tile is lex-smallest
    /// under <c>(Q, R)</c> ordering. Use <see cref="Canonicalize"/>.
    /// This mirrors the convention <see cref="RiverEdge"/> uses for
    /// edges.
    /// </para>
    /// <para>
    /// Lives in <c>HexWorldStudio.Data</c> rather than
    /// <c>HexWorldStudio.Coordinates</c> because it is topology-aware:
    /// every method takes a <c>mapWidth</c> and resolves cylindrical
    /// wrap. <see cref="HexCoord"/> deliberately knows nothing about
    /// wrap; <see cref="RiverEdge"/> sets the same precedent.
    /// </para>
    /// <para>
    /// Geometry reference — corner <c>c</c> of a tile sits at angle
    /// <c>90° + 60°·c</c> and is shared with the neighbours in
    /// directions <see cref="DirectionA"/><c>(c)</c> and
    /// <see cref="DirectionB"/><c>(c)</c>:
    /// </para>
    /// <list type="table">
    /// <listheader><term>Corner</term><description>Angle — shared with</description></listheader>
    /// <item><term>0</term><description>90° — SouthEast, SouthWest</description></item>
    /// <item><term>1</term><description>150° — SouthWest, West</description></item>
    /// <item><term>2</term><description>210° — West, NorthWest</description></item>
    /// <item><term>3</term><description>270° — NorthWest, NorthEast</description></item>
    /// <item><term>4</term><description>330° — NorthEast, East</description></item>
    /// <item><term>5</term><description>30° — East, SouthEast</description></item>
    /// </list>
    /// </remarks>
    [Serializable]
    public readonly struct HexCorner : IEquatable<HexCorner>
    {
        /// <summary>Number of corners on a hex tile.</summary>
        public const int CornersPerTile = 6;

        // The two neighbour directions bracketing each corner, derived
        // from the pointy-top layout: corners sit at 30° + 60°k and
        // neighbour directions at 60°k, so corner c at angle 90° + 60°c
        // is bracketed by the directions at (90 + 60c) ± 30°.
        //
        // Explicit tables rather than modular arithmetic: the closed
        // forms (5 - c) and (5 - ((c + 1) % 6)) are correct but read as
        // a puzzle, and this mapping is the one thing in the river
        // subsystem that has to be obviously right.
        private static readonly HexDirection[] s_dirA =
        {
            HexDirection.SouthEast, // corner 0 @  90°
            HexDirection.SouthWest, // corner 1 @ 150°
            HexDirection.West,      // corner 2 @ 210°
            HexDirection.NorthWest, // corner 3 @ 270°
            HexDirection.NorthEast, // corner 4 @ 330°
            HexDirection.East,      // corner 5 @  30°
        };

        private static readonly HexDirection[] s_dirB =
        {
            HexDirection.SouthWest, // corner 0
            HexDirection.West,      // corner 1
            HexDirection.NorthWest, // corner 2
            HexDirection.NorthEast, // corner 3
            HexDirection.East,      // corner 4
            HexDirection.SouthEast, // corner 5
        };

        /// <summary>
        /// The neighbour direction bracketing the given corner on its
        /// clockwise-trailing side. Exposed so renderers and buyer code
        /// reproduce the same corner geometry the generator uses.
        /// </summary>
        /// <param name="cornerIndex">Corner index; wrapped into <c>[0, 6)</c>.</param>
        public static HexDirection DirectionA(int cornerIndex) => s_dirA[Wrap(cornerIndex)];

        /// <summary>
        /// The neighbour direction bracketing the given corner on its
        /// counter-clockwise-leading side. See <see cref="DirectionA"/>.
        /// </summary>
        /// <param name="cornerIndex">Corner index; wrapped into <c>[0, 6)</c>.</param>
        public static HexDirection DirectionB(int cornerIndex) => s_dirB[Wrap(cornerIndex)];

        /// <summary>The owning tile of this naming of the corner.</summary>
        public HexCoord Coord { get; }

        /// <summary>Corner index in <c>[0, 6)</c> relative to <see cref="Coord"/>.</summary>
        public int Index { get; }

        /// <summary>
        /// Constructs a corner from an already-canonical
        /// <paramref name="coord"/> / <paramref name="index"/> pair.
        /// Prefer <see cref="Canonicalize"/> when the canonical form is
        /// unknown — this constructor performs no canonicalization.
        /// </summary>
        /// <param name="coord">The owning tile.</param>
        /// <param name="index">Corner index; wrapped into <c>[0, 6)</c>.</param>
        public HexCorner(HexCoord coord, int index)
        {
            Coord = coord;
            Index = Wrap(index);
        }

        /// <summary>
        /// Returns the canonical naming of the corner identified by
        /// <paramref name="tile"/> / <paramref name="index"/>. All three
        /// namings of the same physical vertex collapse to one value:
        /// the one whose tile is lex-smallest under <c>(Q, R)</c>.
        /// </summary>
        /// <param name="tile">Any tile touching the corner.</param>
        /// <param name="index">The corner's index relative to <paramref name="tile"/>.</param>
        /// <param name="mapWidth">
        /// Column width of the owning map, used to normalize
        /// wrap-crossing tiles. Must be positive.
        /// </param>
        public static HexCorner Canonicalize(HexCoord tile, int index, int mapWidth)
        {
            int c = Wrap(index);

            // The three namings. (T, c) == (T.Neighbor(A[c]), c + 2)
            // == (T.Neighbor(B[c]), c + 4); verified against HexMesh
            // ring positions.
            HexCoord t0 = CoordinateSpace.Normalize(tile, mapWidth);
            HexCoord t1 = CoordinateSpace.Normalize(tile.Neighbor(s_dirA[c]), mapWidth);
            HexCoord t2 = CoordinateSpace.Normalize(tile.Neighbor(s_dirB[c]), mapWidth);

            HexCoord bestTile = t0;
            int bestIndex = c;

            if (LexLess(t1, bestTile))
            {
                bestTile = t1;
                bestIndex = Wrap(c + 2);
            }
            if (LexLess(t2, bestTile))
            {
                bestTile = t2;
                bestIndex = Wrap(c + 4);
            }

            return new HexCorner(bestTile, bestIndex);
        }

        /// <summary>
        /// Outputs the three tiles meeting at this corner, normalized
        /// into the map's canonical column range. Allocation-free.
        /// </summary>
        /// <param name="mapWidth">Column width of the owning map.</param>
        /// <param name="a">The owning tile, <see cref="Coord"/>.</param>
        /// <param name="b">The neighbour in <see cref="DirectionA"/>.</param>
        /// <param name="c">The neighbour in <see cref="DirectionB"/>.</param>
        public void GetIncidentTiles(int mapWidth, out HexCoord a, out HexCoord b, out HexCoord c)
        {
            a = CoordinateSpace.Normalize(Coord, mapWidth);
            b = CoordinateSpace.Normalize(Coord.Neighbor(s_dirA[Index]), mapWidth);
            c = CoordinateSpace.Normalize(Coord.Neighbor(s_dirB[Index]), mapWidth);
        }

        /// <summary>
        /// Outputs the three corners adjacent to this one, in canonical
        /// form. Each is reachable by traversing exactly one hex edge;
        /// use <see cref="TryGetSharedEdge"/> to name that edge.
        /// Allocation-free.
        /// </summary>
        /// <param name="mapWidth">Column width of the owning map.</param>
        /// <param name="a">Adjacent corner across the <see cref="DirectionA"/> edge.</param>
        /// <param name="b">Adjacent corner across the <see cref="DirectionB"/> edge.</param>
        /// <param name="c">
        /// Adjacent corner across the edge joining the two neighbouring
        /// tiles — the one edge at this corner that does not touch
        /// <see cref="Coord"/>.
        /// </param>
        public void GetAdjacentCorners(int mapWidth, out HexCorner a, out HexCorner b, out HexCorner c)
        {
            ComputeSteps(mapWidth, out a, out _, out b, out _, out c, out _);
        }

        /// <summary>
        /// Returns the hex edge connecting this corner to
        /// <paramref name="other"/>, if the two are adjacent. The edge
        /// is returned in canonical <see cref="RiverEdge"/> form, which
        /// is simply the project's canonical naming for "an edge
        /// between two neighbouring hexes" — it carries no river
        /// semantics of its own.
        /// </summary>
        /// <param name="other">The candidate adjacent corner.</param>
        /// <param name="mapWidth">Column width of the owning map.</param>
        /// <param name="edge">The canonical shared edge, when adjacent.</param>
        /// <returns>
        /// <c>true</c> when the corners are adjacent; <c>false</c>
        /// otherwise, with <paramref name="edge"/> left at default.
        /// </returns>
        public bool TryGetSharedEdge(HexCorner other, int mapWidth, out RiverEdge edge)
        {
            ComputeSteps(mapWidth,
                out HexCorner a, out RiverEdge ea,
                out HexCorner b, out RiverEdge eb,
                out HexCorner c, out RiverEdge ec);

            HexCorner target = Canonicalize(other.Coord, other.Index, mapWidth);

            if (a.Equals(target)) { edge = ea; return true; }
            if (b.Equals(target)) { edge = eb; return true; }
            if (c.Equals(target)) { edge = ec; return true; }

            edge = default;
            return false;
        }

        // The three (adjacent corner, connecting edge) pairs for this
        // corner. Derivation, with A = DirectionA(i), B = DirectionB(i)
        // and i = Index:
        //
        //   * The edge from corner i to corner i-1 of Coord runs toward
        //     A; the edge from corner i to corner i+1 runs toward B.
        //     (The edge between corners c and c+1 of a tile lies toward
        //     B(c), and B(i-1) == A(i).)
        //   * The third edge at this corner joins the two neighbouring
        //     tiles and does not touch Coord. Re-naming this corner as
        //     (nbrA, i + 2) puts that edge toward A(i + 2) from nbrA,
        //     with far corner (nbrA, i + 1).
        private void ComputeSteps(int mapWidth,
            out HexCorner a, out RiverEdge ea,
            out HexCorner b, out RiverEdge eb,
            out HexCorner c, out RiverEdge ec)
        {
            int i = Index;
            HexDirection dirA = s_dirA[i];
            HexDirection dirB = s_dirB[i];

            a = Canonicalize(Coord, i - 1, mapWidth);
            ea = RiverEdge.Canonicalize(Coord, dirA, mapWidth);

            b = Canonicalize(Coord, i + 1, mapWidth);
            eb = RiverEdge.Canonicalize(Coord, dirB, mapWidth);

            HexCoord nbrA = Coord.Neighbor(dirA);
            c = Canonicalize(nbrA, i + 1, mapWidth);
            ec = RiverEdge.Canonicalize(
                CoordinateSpace.Normalize(nbrA, mapWidth), s_dirA[Wrap(i + 2)], mapWidth);
        }

        private static int Wrap(int index) => ((index % CornersPerTile) + CornersPerTile) % CornersPerTile;

        private static bool LexLess(HexCoord a, HexCoord b) =>
            a.Q < b.Q || (a.Q == b.Q && a.R < b.R);

        /// <inheritdoc/>
        public bool Equals(HexCorner other) => Coord.Equals(other.Coord) && Index == other.Index;

        /// <inheritdoc/>
        public override bool Equals(object obj) => obj is HexCorner v && Equals(v);

        /// <inheritdoc/>
        public override int GetHashCode()
        {
            unchecked
            {
                return (Coord.GetHashCode() * 397) ^ Index;
            }
        }

        /// <summary>Value equality operator.</summary>
        public static bool operator ==(HexCorner a, HexCorner b) => a.Equals(b);

        /// <summary>Value inequality operator.</summary>
        public static bool operator !=(HexCorner a, HexCorner b) => !a.Equals(b);

        /// <inheritdoc/>
        public override string ToString() => $"Corner[{Coord}#{Index}]";
    }
}
