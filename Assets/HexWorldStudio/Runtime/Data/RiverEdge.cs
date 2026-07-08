using System;
using HexWorldStudio.Coordinates;

namespace HexWorldStudio.Data
{
    /// <summary>
    /// A single river segment: the shared edge between two neighboring
    /// hex tiles. Edge-based river representation per ADR-0024 —
    /// rivers flow along tile boundaries rather than through tile
    /// centers, so a river visually separates two hexes into distinct
    /// banks (Civ V / VI convention).
    /// </summary>
    /// <remarks>
    /// <para>
    /// Value type: two <see cref="RiverEdge"/> instances are equal
    /// when their <see cref="Coord"/> and <see cref="Direction"/>
    /// match exactly. Because the same physical edge has two possible
    /// namings (<c>(A, A→B)</c> and <c>(B, B→A)</c>), instances stored
    /// in <see cref="WorldMap.RiverEdges"/> are always in <b>canonical
    /// form</b>: the <see cref="Coord"/> is the lex-smaller of the two
    /// endpoints under <c>(Q, R)</c> ordering, and
    /// <see cref="Direction"/> points from that endpoint toward the
    /// other. Use <see cref="Canonicalize"/> to convert any
    /// <c>(coord, direction)</c> pair to this form.
    /// </para>
    /// <para>
    /// Cylindrical wrap is handled by the canonicalizer: an edge that
    /// crosses the seam between column <c>0</c> and column
    /// <c>Width - 1</c> is stored with <see cref="Coord"/> at
    /// <c>(0, R)</c> and <see cref="Direction"/> pointing
    /// <see cref="HexDirection.West"/>; the raw neighbor coordinate
    /// <c>(-1, R)</c> resolves to <c>(Width - 1, R)</c> via map
    /// normalization when rendered.
    /// </para>
    /// <para>
    /// The two endpoint tiles are queried via
    /// <c>WorldMap.TryGetTile(Coord)</c> and
    /// <c>WorldMap.TryGetTile(Coord.Neighbor(Direction))</c>. The
    /// second call may return a non-canonical raw coord that the map
    /// wraps automatically.
    /// </para>
    /// </remarks>
    [Serializable]
    public readonly struct RiverEdge : IEquatable<RiverEdge>
    {
        /// <summary>
        /// The canonical (lex-smaller) endpoint of the edge. Always in
        /// the map's canonical column range <c>[0, Width)</c>.
        /// </summary>
        public HexCoord Coord { get; }

        /// <summary>
        /// The direction from <see cref="Coord"/> toward the other
        /// endpoint. In wrap-crossing edges, the raw neighbor may be
        /// outside <c>[0, Width)</c> and requires normalization.
        /// </summary>
        public HexDirection Direction { get; }

        /// <summary>
        /// Constructs a river edge from an already-canonical
        /// <paramref name="coord"/> / <paramref name="direction"/>
        /// pair. Prefer <see cref="Canonicalize"/> when the input's
        /// canonical form is unknown — this constructor performs no
        /// canonicalization.
        /// </summary>
        public RiverEdge(HexCoord coord, HexDirection direction)
        {
            Coord = coord;
            Direction = direction;
        }

        /// <summary>
        /// Returns the canonical form of the edge named by
        /// <paramref name="owner"/> / <paramref name="direction"/>.
        /// Both <c>(A, A→B)</c> and <c>(B, B→A)</c> collapse to the
        /// same result: the lex-smaller endpoint owns the edge and
        /// the direction points at the other one.
        /// </summary>
        /// <param name="owner">
        /// One endpoint of the edge, expected in canonical column
        /// range <c>[0, mapWidth)</c>.
        /// </param>
        /// <param name="direction">
        /// The direction from <paramref name="owner"/> toward the
        /// other endpoint.
        /// </param>
        /// <param name="mapWidth">
        /// Column width of the owning <see cref="WorldMap"/>. Used to
        /// canonicalize wrap-crossing edges. Must be positive.
        /// </param>
        public static RiverEdge Canonicalize(HexCoord owner, HexDirection direction, int mapWidth)
        {
            HexCoord otherRaw = owner.Neighbor(direction);
            HexCoord otherCanonical = CoordinateSpace.Normalize(otherRaw, mapWidth);

            if (LexLess(owner, otherCanonical))
            {
                return new RiverEdge(owner, direction);
            }
            return new RiverEdge(otherCanonical, Opposite(direction));
        }

        /// <summary>
        /// Returns the opposite of the given hex direction — the
        /// direction pointing back along the same axis.
        /// <c>East ↔ West</c>, <c>NorthEast ↔ SouthWest</c>,
        /// <c>NorthWest ↔ SouthEast</c>.
        /// </summary>
        public static HexDirection Opposite(HexDirection direction) =>
            (HexDirection)(((int)direction + 3) % 6);

        private static bool LexLess(HexCoord a, HexCoord b) =>
            a.Q < b.Q || (a.Q == b.Q && a.R < b.R);

        /// <inheritdoc/>
        public bool Equals(RiverEdge other) =>
            Coord.Equals(other.Coord) && Direction == other.Direction;

        /// <inheritdoc/>
        public override bool Equals(object obj) => obj is RiverEdge e && Equals(e);

        /// <inheritdoc/>
        public override int GetHashCode()
        {
            unchecked
            {
                return (Coord.GetHashCode() * 397) ^ (int)Direction;
            }
        }

        /// <summary>Value equality operator.</summary>
        public static bool operator ==(RiverEdge a, RiverEdge b) => a.Equals(b);

        /// <summary>Value inequality operator.</summary>
        public static bool operator !=(RiverEdge a, RiverEdge b) => !a.Equals(b);

        /// <inheritdoc/>
        public override string ToString() => $"River[{Coord} → {Direction}]";
    }
}
