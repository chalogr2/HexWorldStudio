using HexWorldStudio.Coordinates;

namespace HexWorldStudio.Data
{
    /// <summary>
    /// Stateless geometry helpers that convert between an axial
    /// <see cref="HexCoord"/> and the (col, row, tile-index) layout used
    /// by a rectangular cylindrical <see cref="WorldMap"/>.
    /// </summary>
    /// <remarks>
    /// Lives separately from <see cref="WorldMap"/> so the wrap / odd-r
    /// math is in one place, easy to inspect, and easy to test without a
    /// full <see cref="WorldMap"/> instance. Internal — buyers go through
    /// <see cref="WorldMap"/>'s public API rather than calling these
    /// helpers directly.
    /// </remarks>
    internal static class CoordinateSpace
    {
        /// <summary>
        /// Wraps the column component of <paramref name="coord"/> into
        /// <c>[0, width)</c> via a true mathematical mod (handles
        /// negative columns correctly), then converts back to axial.
        /// Row is left untouched.
        /// </summary>
        /// <param name="coord">The coordinate to normalize.</param>
        /// <param name="width">The map width, in tiles. Must be positive.</param>
        /// <returns>
        /// A coordinate whose offset column is in <c>[0, width)</c> and
        /// whose row is unchanged. If <paramref name="coord"/>'s row is
        /// out of vertical bounds, the returned coordinate is still
        /// canonical on Q but out of vertical bounds — pair with
        /// <see cref="IsInVerticalBounds"/> to check.
        /// </returns>
        public static HexCoord Normalize(HexCoord coord, int width)
        {
            (int col, int row) = coord.ToOffsetOddR();
            col = ((col % width) + width) % width;
            return HexCoordExtensions.FromOffsetOddR(col, row);
        }

        /// <summary>
        /// Returns <c>true</c> if <paramref name="coord"/>'s row is in
        /// <c>[0, height)</c>. The column is always considered in bounds
        /// because the horizontal axis wraps.
        /// </summary>
        /// <param name="coord">The coordinate to test.</param>
        /// <param name="height">The map height, in rows. Must be positive.</param>
        public static bool IsInVerticalBounds(HexCoord coord, int height)
        {
            (int _, int row) = coord.ToOffsetOddR();
            return row >= 0 && row < height;
        }

        /// <summary>
        /// Computes the raster-order tile index for
        /// <paramref name="coord"/> assuming a <paramref name="width"/>-
        /// wide cylindrical map. Wraps the column; assumes the row is
        /// already known to be in bounds (caller validates via
        /// <see cref="IsInVerticalBounds"/>).
        /// </summary>
        public static int IndexOf(HexCoord coord, int width)
        {
            (int col, int row) = coord.ToOffsetOddR();
            col = ((col % width) + width) % width;
            return row * width + col;
        }

        /// <summary>
        /// Converts a raster-order tile index back to the canonical
        /// axial coordinate that index represents.
        /// </summary>
        /// <param name="index">A tile index in <c>[0, width*height)</c>.</param>
        /// <param name="width">The map width.</param>
        public static HexCoord CoordFromIndex(int index, int width)
        {
            int row = index / width;
            int col = index % width;
            return HexCoordExtensions.FromOffsetOddR(col, row);
        }
    }
}
