using System;
using System.Collections.Generic;
using HexWorldStudio.Coordinates;
using HexWorldStudio.Data;
using NUnit.Framework;

namespace HexWorldStudio.Tests
{
    /// <summary>
    /// EditMode tests for <see cref="WorldMap"/> — construction,
    /// tile-index invariants, cylindrical wrap on the horizontal axis,
    /// vertical bounds, and the Contains / Normalize / TryGetTile
    /// contracts.
    /// </summary>
    public class WorldMapTests
    {
        private const int Width = 20;
        private const int Height = 12;
        private const ulong SampleSeed = 0xCAFEBABEDEADBEEFUL;

        private static WorldMap MakeMap() => new WorldMap(Width, Height, SampleSeed);

        // ---------- Construction & invariants ----------

        [Test]
        public void Constructor_StoresWidthHeightSeed()
        {
            WorldMap map = MakeMap();
            Assert.That(map.Width, Is.EqualTo(Width));
            Assert.That(map.Height, Is.EqualTo(Height));
            Assert.That(map.Seed, Is.EqualTo(SampleSeed));
        }

        [Test]
        public void Constructor_ZeroOrNegativeWidth_Throws()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new WorldMap(0, Height, 0UL));
            Assert.Throws<ArgumentOutOfRangeException>(() => new WorldMap(-1, Height, 0UL));
        }

        [Test]
        public void Constructor_ZeroOrNegativeHeight_Throws()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new WorldMap(Width, 0, 0UL));
            Assert.Throws<ArgumentOutOfRangeException>(() => new WorldMap(Width, -1, 0UL));
        }

        [Test]
        public void Tiles_CountEqualsWidthTimesHeight()
        {
            WorldMap map = MakeMap();
            Assert.That(map.Tiles.Count, Is.EqualTo(Width * Height));
        }

        [Test]
        public void Tiles_AllIndicesAreContiguousZeroToCountMinusOne()
        {
            WorldMap map = MakeMap();
            for (int i = 0; i < map.Tiles.Count; i++)
            {
                Assert.That(map.Tiles[i].TileIndex, Is.EqualTo(i));
            }
        }

        [Test]
        public void Tiles_AllCoordsAreCanonical()
        {
            // Every freshly-constructed tile's Coord should equal its own
            // normalization. Pins the invariant that the constructor
            // produces canonical coords, so wrap-round-trip tests below
            // have a defined ground truth.
            WorldMap map = MakeMap();
            foreach (HexTile tile in map.Tiles)
            {
                Assert.That(tile.Coord, Is.EqualTo(map.Normalize(tile.Coord)),
                    $"Tile {tile.TileIndex} has non-canonical Coord {tile.Coord}.");
            }
        }

        [Test]
        public void Tiles_AllCoordsAreUnique()
        {
            WorldMap map = MakeMap();
            var seen = new HashSet<HexCoord>();
            foreach (HexTile tile in map.Tiles)
            {
                Assert.That(seen.Add(tile.Coord), Is.True,
                    $"Duplicate Coord {tile.Coord} at tile index {tile.TileIndex}.");
            }
        }

        // ---------- Vertical bounds (no wrap) ----------

        [Test]
        public void Contains_RowsInBounds_ReturnsTrue()
        {
            WorldMap map = MakeMap();
            for (int row = 0; row < Height; row++)
            {
                HexCoord c = HexCoordExtensions.FromOffsetOddR(0, row);
                Assert.That(map.Contains(c), Is.True, $"Row {row} should be in bounds.");
            }
        }

        [Test]
        public void Contains_NegativeRow_ReturnsFalse()
        {
            WorldMap map = MakeMap();
            HexCoord c = HexCoordExtensions.FromOffsetOddR(0, -1);
            Assert.That(map.Contains(c), Is.False);
        }

        [Test]
        public void Contains_RowAtOrBeyondHeight_ReturnsFalse()
        {
            WorldMap map = MakeMap();
            HexCoord atHeight = HexCoordExtensions.FromOffsetOddR(0, Height);
            HexCoord beyondHeight = HexCoordExtensions.FromOffsetOddR(0, Height + 5);
            Assert.That(map.Contains(atHeight), Is.False);
            Assert.That(map.Contains(beyondHeight), Is.False);
        }

        [Test]
        public void Contains_AnyHorizontalDisplacement_DoesNotChangeResult()
        {
            // The horizontal axis wraps, so Contains depends only on row.
            WorldMap map = MakeMap();
            HexCoord baseCoord = HexCoordExtensions.FromOffsetOddR(0, 3);
            bool expected = map.Contains(baseCoord);
            for (int dq = -Width * 3; dq <= Width * 3; dq += 7)
            {
                HexCoord shifted = new HexCoord(baseCoord.Q + dq, baseCoord.R);
                Assert.That(map.Contains(shifted), Is.EqualTo(expected),
                    $"Contains result differed for horizontal shift {dq}.");
            }
        }

        // ---------- Normalize ----------

        [Test]
        public void Normalize_AlreadyCanonical_ReturnsEquivalentCoord()
        {
            WorldMap map = MakeMap();
            for (int row = 0; row < Height; row++)
            {
                for (int col = 0; col < Width; col++)
                {
                    HexCoord c = HexCoordExtensions.FromOffsetOddR(col, row);
                    HexCoord n = map.Normalize(c);
                    Assert.That(n, Is.EqualTo(c),
                        $"Normalize altered already-canonical coord at (col={col}, row={row}).");
                }
            }
        }

        [Test]
        public void Normalize_ColumnBeyondWidth_WrapsBackInside()
        {
            WorldMap map = MakeMap();
            HexCoord original = HexCoordExtensions.FromOffsetOddR(0, 4);
            HexCoord beyond = HexCoordExtensions.FromOffsetOddR(Width, 4);
            Assert.That(map.Normalize(beyond), Is.EqualTo(original));
        }

        [Test]
        public void Normalize_NegativeColumn_WrapsToPositiveSide()
        {
            WorldMap map = MakeMap();
            HexCoord rightEdge = HexCoordExtensions.FromOffsetOddR(Width - 1, 5);
            HexCoord beyondLeft = HexCoordExtensions.FromOffsetOddR(-1, 5);
            Assert.That(map.Normalize(beyondLeft), Is.EqualTo(rightEdge));
        }

        [Test]
        public void Normalize_DoesNotChangeRow()
        {
            WorldMap map = MakeMap();
            // Out-of-vertical-bounds rows still get their Q wrapped, but
            // R is untouched. The result's offset row equals the input's.
            HexCoord c = HexCoordExtensions.FromOffsetOddR(Width * 3 + 2, Height + 10);
            HexCoord n = map.Normalize(c);
            (int _, int rowIn) = c.ToOffsetOddR();
            (int _, int rowOut) = n.ToOffsetOddR();
            Assert.That(rowOut, Is.EqualTo(rowIn));
        }

        [Test]
        public void Normalize_LargePositiveAndNegativeShifts_LandInCanonicalRange()
        {
            WorldMap map = MakeMap();
            HexCoord baseCoord = HexCoordExtensions.FromOffsetOddR(7, 6);
            for (int loops = -5; loops <= 5; loops++)
            {
                HexCoord shifted = new HexCoord(baseCoord.Q + loops * Width, baseCoord.R);
                Assert.That(map.Normalize(shifted), Is.EqualTo(baseCoord),
                    $"Shift of {loops} widths did not normalize to the base.");
            }
        }

        // ---------- TryGetTile ----------

        [Test]
        public void TryGetTile_InBounds_ReturnsTrueWithMatchingCoord()
        {
            WorldMap map = MakeMap();
            HexCoord c = HexCoordExtensions.FromOffsetOddR(5, 3);
            bool ok = map.TryGetTile(c, out HexTile tile);
            Assert.That(ok, Is.True);
            Assert.That(tile, Is.Not.Null);
            Assert.That(tile.Coord, Is.EqualTo(c));
        }

        [Test]
        public void TryGetTile_OutOfVerticalBounds_ReturnsFalseAndNull()
        {
            WorldMap map = MakeMap();
            HexCoord above = HexCoordExtensions.FromOffsetOddR(3, -1);
            HexCoord below = HexCoordExtensions.FromOffsetOddR(3, Height);

            Assert.That(map.TryGetTile(above, out HexTile aTile), Is.False);
            Assert.That(aTile, Is.Null);

            Assert.That(map.TryGetTile(below, out HexTile bTile), Is.False);
            Assert.That(bTile, Is.Null);
        }

        [Test]
        public void TryGetTile_WrapsAcrossRightSeam_ReturnsLeftmostTile()
        {
            WorldMap map = MakeMap();
            HexCoord leftEdge = HexCoordExtensions.FromOffsetOddR(0, 4);
            HexCoord beyondRight = HexCoordExtensions.FromOffsetOddR(Width, 4);

            map.TryGetTile(leftEdge, out HexTile expected);
            bool ok = map.TryGetTile(beyondRight, out HexTile wrapped);
            Assert.That(ok, Is.True);
            Assert.That(wrapped, Is.SameAs(expected));
        }

        [Test]
        public void TryGetTile_WrapsAcrossLeftSeam_ReturnsRightmostTile()
        {
            WorldMap map = MakeMap();
            HexCoord rightEdge = HexCoordExtensions.FromOffsetOddR(Width - 1, 7);
            HexCoord beyondLeft = HexCoordExtensions.FromOffsetOddR(-1, 7);

            map.TryGetTile(rightEdge, out HexTile expected);
            bool ok = map.TryGetTile(beyondLeft, out HexTile wrapped);
            Assert.That(ok, Is.True);
            Assert.That(wrapped, Is.SameAs(expected));
        }

        [Test]
        public void TryGetTile_NeighborAcrossSeam_ResolvesToSameRowOnFarSide()
        {
            // The east neighbor of the rightmost tile should resolve to
            // the leftmost tile of the same row — that's the whole point
            // of cylindrical wrap.
            WorldMap map = MakeMap();
            int row = 5;
            HexCoord rightEdge = HexCoordExtensions.FromOffsetOddR(Width - 1, row);
            HexCoord eastNeighbor = rightEdge.Neighbor(HexDirection.East);

            bool ok = map.TryGetTile(eastNeighbor, out HexTile resolved);
            Assert.That(ok, Is.True);
            (int col, int r) = resolved.Coord.ToOffsetOddR();
            Assert.That(r, Is.EqualTo(row));
            Assert.That(col, Is.EqualTo(0));
        }

        // ---------- TryGetIndex ----------

        [Test]
        public void TryGetIndex_InBounds_MatchesGetTileByIndex()
        {
            WorldMap map = MakeMap();
            HexCoord c = HexCoordExtensions.FromOffsetOddR(7, 9);

            Assert.That(map.TryGetIndex(c, out int idx), Is.True);
            Assert.That(map.GetTileByIndex(idx).Coord, Is.EqualTo(c));
        }

        [Test]
        public void TryGetIndex_OutOfVerticalBounds_ReturnsFalseAndNegativeOne()
        {
            WorldMap map = MakeMap();
            HexCoord c = HexCoordExtensions.FromOffsetOddR(0, Height + 1);
            Assert.That(map.TryGetIndex(c, out int idx), Is.False);
            Assert.That(idx, Is.EqualTo(-1));
        }

        // ---------- GetTileByIndex ----------

        [Test]
        public void GetTileByIndex_ValidIndex_ReturnsTileWithMatchingIndex()
        {
            WorldMap map = MakeMap();
            HexTile tile = map.GetTileByIndex(42);
            Assert.That(tile.TileIndex, Is.EqualTo(42));
        }

        [Test]
        public void GetTileByIndex_NegativeIndex_Throws()
        {
            WorldMap map = MakeMap();
            Assert.Throws<ArgumentOutOfRangeException>(() => map.GetTileByIndex(-1));
        }

        [Test]
        public void GetTileByIndex_BeyondLastTile_Throws()
        {
            WorldMap map = MakeMap();
            Assert.Throws<ArgumentOutOfRangeException>(() => map.GetTileByIndex(Width * Height));
        }
    }
}
