using System;

namespace HexWorldStudio.Coordinates
{
    /// <summary>
    /// Immutable axial coordinate for a single hex cell.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>HexCoord</c> stores the standard axial pair <c>(Q, R)</c>. The third
    /// cube component <see cref="S"/> is exposed as a derived property where
    /// <c>S = -Q - R</c>; the cube identity <c>Q + R + S = 0</c> always holds.
    /// </para>
    /// <para>
    /// The type is orientation-agnostic. Pointy-top vs flat-top is a rendering
    /// concern handled by the visualization layer; the data layer carries the
    /// same <c>(Q, R)</c> regardless of how a tile is drawn. Direction labels
    /// in <see cref="HexDirection"/> follow pointy-top convention, but the
    /// underlying axial vectors apply to any orientation.
    /// </para>
    /// <para>
    /// Equality is value-based. <see cref="GetHashCode"/> is platform-stable
    /// and deterministic across process restarts, supporting the project's
    /// determinism contract for seeded generation.
    /// </para>
    /// </remarks>
    [Serializable]
    public readonly struct HexCoord : IEquatable<HexCoord>
    {
        /// <summary>The axial Q component.</summary>
        public int Q { get; }

        /// <summary>The axial R component.</summary>
        public int R { get; }

        /// <summary>
        /// The implicit cube S component, equal to <c>-Q - R</c>.
        /// </summary>
        /// <remarks>
        /// Computed on access; not stored. The cube identity
        /// <c>Q + R + S = 0</c> is preserved for any <see cref="HexCoord"/>.
        /// </remarks>
        public int S => -Q - R;

        /// <summary>The origin coordinate <c>(0, 0)</c>.</summary>
        public static HexCoord Zero { get; } = new HexCoord(0, 0);

        /// <summary>
        /// Constructs an axial coordinate from <paramref name="q"/> and
        /// <paramref name="r"/>.
        /// </summary>
        /// <param name="q">The axial Q component.</param>
        /// <param name="r">The axial R component.</param>
        public HexCoord(int q, int r)
        {
            Q = q;
            R = r;
        }

        /// <summary>
        /// Factory equivalent of the <see cref="HexCoord(int, int)"/>
        /// constructor, provided for call-site readability.
        /// </summary>
        /// <param name="q">The axial Q component.</param>
        /// <param name="r">The axial R component.</param>
        /// <returns>A new <see cref="HexCoord"/> at <c>(q, r)</c>.</returns>
        public static HexCoord From(int q, int r) => new HexCoord(q, r);

        /// <summary>
        /// Returns <c>true</c> if the other coordinate has the same
        /// <see cref="Q"/> and <see cref="R"/> components.
        /// </summary>
        /// <param name="other">The coordinate to compare against.</param>
        public bool Equals(HexCoord other) => Q == other.Q && R == other.R;

        /// <inheritdoc/>
        public override bool Equals(object obj) => obj is HexCoord other && Equals(other);

        /// <summary>
        /// Returns a deterministic, platform-stable hash for this coordinate.
        /// </summary>
        /// <remarks>
        /// Implemented as <c>unchecked((Q * 397) ^ R)</c> rather than
        /// <see cref="HashCode.Combine(int, int)"/> so the value is identical
        /// across process restarts and platforms. The framework version mixes
        /// in a per-process random seed, which would break the project's
        /// determinism contract for any code path that hashes coordinates.
        /// </remarks>
        public override int GetHashCode()
        {
            unchecked
            {
                return (Q * 397) ^ R;
            }
        }

        /// <summary>
        /// Returns a string of the form <c>"(Q, R)"</c>.
        /// </summary>
        public override string ToString() => $"({Q}, {R})";

        /// <summary>Value equality operator.</summary>
        public static bool operator ==(HexCoord a, HexCoord b) => a.Equals(b);

        /// <summary>Value inequality operator.</summary>
        public static bool operator !=(HexCoord a, HexCoord b) => !a.Equals(b);
    }

    /// <summary>
    /// The six axial neighbor directions, in Red Blob canonical order.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Enum values <c>0..5</c> are used directly as indices into the internal
    /// direction-vector table used by <see cref="HexCoordExtensions"/>, so
    /// the numeric ordering is part of the API contract and must not change.
    /// </para>
    /// <para>
    /// Labels describe pointy-top orientation, which is the dominant
    /// convention in Red Blob Games' reference material and in Civ-style
    /// games. The underlying axial vectors are orientation-agnostic; the
    /// rendering layer is free to interpret them as either pointy-top or
    /// flat-top neighbors.
    /// </para>
    /// </remarks>
    public enum HexDirection
    {
        /// <summary>Direction <c>(+1, 0)</c>.</summary>
        East = 0,

        /// <summary>Direction <c>(+1, -1)</c>.</summary>
        NorthEast = 1,

        /// <summary>Direction <c>(0, -1)</c>.</summary>
        NorthWest = 2,

        /// <summary>Direction <c>(-1, 0)</c>.</summary>
        West = 3,

        /// <summary>Direction <c>(-1, +1)</c>.</summary>
        SouthWest = 4,

        /// <summary>Direction <c>(0, +1)</c>.</summary>
        SouthEast = 5,
    }
}
