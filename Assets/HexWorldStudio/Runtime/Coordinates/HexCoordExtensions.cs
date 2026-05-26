using System;
using System.Collections.Generic;

namespace HexWorldStudio.Coordinates
{
    /// <summary>
    /// Geometry helpers over <see cref="HexCoord"/>: distance, neighbors,
    /// range, line drawing, and offset-coordinate conversions.
    /// </summary>
    /// <remarks>
    /// All methods are pure, allocation-aware, and deterministic. Iteration
    /// order in any returned sequence is stable across runs and platforms
    /// and forms part of the API contract — it must not change without an
    /// ADR.
    /// </remarks>
    public static class HexCoordExtensions
    {
        // Axial unit vectors indexed by HexDirection (0..5). Index order
        // matches the enum exactly and is part of the API contract.
        private static readonly HexCoord[] s_directions = new HexCoord[]
        {
            new HexCoord(+1,  0), // East
            new HexCoord(+1, -1), // NorthEast
            new HexCoord( 0, -1), // NorthWest
            new HexCoord(-1,  0), // West
            new HexCoord(-1, +1), // SouthWest
            new HexCoord( 0, +1), // SouthEast
        };

        /// <summary>
        /// Returns the hex-grid distance between <paramref name="a"/> and
        /// <paramref name="b"/>, measured in steps along the six cardinal
        /// hex axes.
        /// </summary>
        /// <param name="a">The first coordinate.</param>
        /// <param name="b">The second coordinate.</param>
        /// <returns>
        /// The number of steps separating the two coordinates. Zero when
        /// the coordinates are equal. Symmetric: <c>Distance(a, b)</c>
        /// equals <c>Distance(b, a)</c>.
        /// </returns>
        public static int Distance(this HexCoord a, HexCoord b)
        {
            int dq = a.Q - b.Q;
            int dr = a.R - b.R;
            int ds = a.S - b.S;
            return (Math.Abs(dq) + Math.Abs(dr) + Math.Abs(ds)) / 2;
        }

        /// <summary>
        /// Returns the neighbor of <paramref name="c"/> in the given
        /// <paramref name="direction"/>.
        /// </summary>
        /// <param name="c">The center coordinate.</param>
        /// <param name="direction">The direction in which to step.</param>
        /// <returns>The neighboring coordinate, one step from <paramref name="c"/>.</returns>
        /// <exception cref="ArgumentOutOfRangeException">
        /// Thrown when <paramref name="direction"/> is not one of the six
        /// defined <see cref="HexDirection"/> values.
        /// </exception>
        public static HexCoord Neighbor(this HexCoord c, HexDirection direction)
        {
            int idx = (int)direction;
            if (idx < 0 || idx >= s_directions.Length)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(direction),
                    "HexDirection must be one of the six defined values.");
            }

            HexCoord d = s_directions[idx];
            return new HexCoord(c.Q + d.Q, c.R + d.R);
        }

        /// <summary>
        /// Enumerates the six neighbors of <paramref name="c"/> in
        /// <see cref="HexDirection"/> order, from <see cref="HexDirection.East"/>
        /// through <see cref="HexDirection.SouthEast"/>.
        /// </summary>
        /// <param name="c">The center coordinate.</param>
        /// <returns>An enumeration of exactly six neighbor coordinates.</returns>
        /// <remarks>
        /// Iteration order is guaranteed deterministic across runs and
        /// platforms, and matches the numeric order of
        /// <see cref="HexDirection"/>.
        /// </remarks>
        public static IEnumerable<HexCoord> Neighbors(this HexCoord c)
        {
            for (int i = 0; i < s_directions.Length; i++)
            {
                HexCoord d = s_directions[i];
                yield return new HexCoord(c.Q + d.Q, c.R + d.R);
            }
        }

        /// <summary>
        /// Returns every coordinate within <paramref name="radius"/> steps
        /// of <paramref name="center"/>, inclusive (including
        /// <paramref name="center"/> itself).
        /// </summary>
        /// <param name="center">The center coordinate.</param>
        /// <param name="radius">
        /// The maximum step distance from the center. Must be non-negative.
        /// </param>
        /// <returns>
        /// A list of <c>3 * radius * (radius + 1) + 1</c> coordinates.
        /// Iteration order is deterministic but treated as an implementation
        /// detail; callers should not rely on it beyond what the type
        /// guarantees.
        /// </returns>
        /// <exception cref="ArgumentOutOfRangeException">
        /// Thrown when <paramref name="radius"/> is negative.
        /// </exception>
        public static IReadOnlyList<HexCoord> Range(this HexCoord center, int radius)
        {
            if (radius < 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(radius),
                    "Radius must be non-negative.");
            }

            int count = 3 * radius * (radius + 1) + 1;
            HexCoord[] result = new HexCoord[count];
            int i = 0;
            for (int dq = -radius; dq <= radius; dq++)
            {
                int drMin = Math.Max(-radius, -dq - radius);
                int drMax = Math.Min(radius, -dq + radius);
                for (int dr = drMin; dr <= drMax; dr++)
                {
                    result[i++] = new HexCoord(center.Q + dq, center.R + dr);
                }
            }

            return result;
        }

        /// <summary>
        /// Returns the line of hex cells from <paramref name="a"/> to
        /// <paramref name="b"/>, inclusive of both endpoints.
        /// </summary>
        /// <param name="a">The starting coordinate.</param>
        /// <param name="b">The ending coordinate.</param>
        /// <returns>
        /// A list of <c>Distance(a, b) + 1</c> coordinates. The first entry
        /// is always <paramref name="a"/> and the last entry is always
        /// <paramref name="b"/>; consecutive entries are always one step
        /// apart. When <paramref name="a"/> equals <paramref name="b"/> the
        /// list contains the single coordinate.
        /// </returns>
        /// <remarks>
        /// Implemented as cube linear interpolation followed by nearest-hex
        /// rounding (the standard Red Blob line-draw algorithm). Pure: no
        /// obstacle awareness. True line-of-sight queries require a
        /// <c>WorldMap</c> and will be added in a later slice.
        /// </remarks>
        public static IReadOnlyList<HexCoord> Line(this HexCoord a, HexCoord b)
        {
            int n = a.Distance(b);
            HexCoord[] result = new HexCoord[n + 1];

            for (int i = 0; i <= n; i++)
            {
                double t = n == 0 ? 0.0 : (double)i / n;
                double q = a.Q + (b.Q - a.Q) * t;
                double r = a.R + (b.R - a.R) * t;
                double s = a.S + (b.S - a.S) * t;
                result[i] = CubeRound(q, r, s);
            }

            return result;
        }

        /// <summary>
        /// Converts an axial <see cref="HexCoord"/> to <c>(col, row)</c>
        /// using the odd-r offset convention (odd rows shifted right).
        /// </summary>
        /// <param name="c">The axial coordinate to convert.</param>
        /// <returns>The corresponding <c>(col, row)</c> offset pair.</returns>
        public static (int col, int row) ToOffsetOddR(this HexCoord c)
        {
            int col = c.Q + (c.R - (c.R & 1)) / 2;
            int row = c.R;
            return (col, row);
        }

        /// <summary>
        /// Converts an odd-r offset <c>(col, row)</c> pair to an axial
        /// <see cref="HexCoord"/>.
        /// </summary>
        /// <param name="col">The offset column.</param>
        /// <param name="row">The offset row.</param>
        /// <returns>The corresponding axial <see cref="HexCoord"/>.</returns>
        public static HexCoord FromOffsetOddR(int col, int row)
        {
            int q = col - (row - (row & 1)) / 2;
            int r = row;
            return new HexCoord(q, r);
        }

        /// <summary>
        /// Converts an axial <see cref="HexCoord"/> to <c>(col, row)</c>
        /// using the even-r offset convention (even rows shifted right).
        /// </summary>
        /// <param name="c">The axial coordinate to convert.</param>
        /// <returns>The corresponding <c>(col, row)</c> offset pair.</returns>
        public static (int col, int row) ToOffsetEvenR(this HexCoord c)
        {
            int col = c.Q + (c.R + (c.R & 1)) / 2;
            int row = c.R;
            return (col, row);
        }

        /// <summary>
        /// Converts an even-r offset <c>(col, row)</c> pair to an axial
        /// <see cref="HexCoord"/>.
        /// </summary>
        /// <param name="col">The offset column.</param>
        /// <param name="row">The offset row.</param>
        /// <returns>The corresponding axial <see cref="HexCoord"/>.</returns>
        public static HexCoord FromOffsetEvenR(int col, int row)
        {
            int q = col - (row + (row & 1)) / 2;
            int r = row;
            return new HexCoord(q, r);
        }

        // Nearest-hex rounding for cube coordinates: reassigns the component
        // with the largest rounding error so the cube identity q+r+s=0 is
        // preserved. Standard Red Blob algorithm.
        private static HexCoord CubeRound(double q, double r, double s)
        {
            int rq = (int)Math.Round(q);
            int rr = (int)Math.Round(r);
            int rs = (int)Math.Round(s);

            double dq = Math.Abs(rq - q);
            double dr = Math.Abs(rr - r);
            double ds = Math.Abs(rs - s);

            if (dq > dr && dq > ds)
            {
                rq = -rr - rs;
            }
            else if (dr > ds)
            {
                rr = -rq - rs;
            }
            // else: rs is the worst; rq and rr already round-trip via Q+R+S=0.

            return new HexCoord(rq, rr);
        }
    }
}
