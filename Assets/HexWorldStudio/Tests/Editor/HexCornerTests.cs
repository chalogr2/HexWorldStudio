using System.Collections.Generic;
using HexWorldStudio.Coordinates;
using HexWorldStudio.Data;
using NUnit.Framework;

namespace HexWorldStudio.Tests
{
    /// <summary>
    /// EditMode tests for <see cref="HexCorner"/> (ADR-0025) — the
    /// three-namings canonicalization contract, corner adjacency, and
    /// the corner-to-edge correspondence that the river walk depends
    /// on. If these pass, "a step between adjacent corners is exactly
    /// one hex edge" holds, which is what makes river chains connected
    /// by construction.
    /// </summary>
    public class HexCornerTests
    {
        private const int MapWidth = 20;

        // The two alternate namings of corner (tile, index): the same
        // physical vertex as seen from each of the other two tiles that
        // meet there.
        private static (HexCoord tile, int index)[] AllNamings(HexCoord tile, int index)
        {
            HexDirection a = HexCorner.DirectionA(index);
            HexDirection b = HexCorner.DirectionB(index);
            return new[]
            {
                (tile, index),
                (tile.Neighbor(a), index + 2),
                (tile.Neighbor(b), index + 4),
            };
        }

        [Test]
        public void Canonicalize_AllThreeNamings_CollapseToOneValue()
        {
            foreach (HexCoord tile in SampleTiles())
            {
                for (int i = 0; i < HexCorner.CornersPerTile; i++)
                {
                    HexCorner expected = HexCorner.Canonicalize(tile, i, MapWidth);
                    foreach ((HexCoord t, int idx) in AllNamings(tile, i))
                    {
                        Assert.That(HexCorner.Canonicalize(t, idx, MapWidth), Is.EqualTo(expected),
                            $"Naming ({t}#{idx}) did not collapse to the canonical form of ({tile}#{i}).");
                    }
                }
            }
        }

        [Test]
        public void Canonicalize_IsIdempotent()
        {
            foreach (HexCoord tile in SampleTiles())
            {
                for (int i = 0; i < HexCorner.CornersPerTile; i++)
                {
                    HexCorner once = HexCorner.Canonicalize(tile, i, MapWidth);
                    HexCorner twice = HexCorner.Canonicalize(once.Coord, once.Index, MapWidth);
                    Assert.That(twice, Is.EqualTo(once));
                }
            }
        }

        [Test]
        public void Canonicalize_AcrossWrapSeam_AgreesFromBothSides()
        {
            // Column 0 and column MapWidth-1 are neighbours on the
            // cylinder. A corner touching both must canonicalize
            // identically regardless of which side names it.
            var west = HexCoordExtensions.FromOffsetOddR(0, 4);
            var east = HexCoordExtensions.FromOffsetOddR(MapWidth - 1, 4);

            // Corner 2 of the column-0 tile is shared with its West
            // neighbour, which wraps to the column-(Width-1) tile.
            HexCorner fromWest = HexCorner.Canonicalize(west, 2, MapWidth);

            bool matched = false;
            for (int i = 0; i < HexCorner.CornersPerTile && !matched; i++)
            {
                matched = HexCorner.Canonicalize(east, i, MapWidth).Equals(fromWest);
            }
            Assert.That(matched, Is.True,
                "A corner on the wrap seam must be reachable from the tile on the far side of the seam.");
            Assert.That(fromWest.Coord.Q, Is.GreaterThanOrEqualTo(0),
                "Canonical corners must carry a normalized tile coordinate.");
        }

        [Test]
        public void GetIncidentTiles_ReturnsThreeDistinctTiles()
        {
            foreach (HexCoord tile in SampleTiles())
            {
                for (int i = 0; i < HexCorner.CornersPerTile; i++)
                {
                    HexCorner v = HexCorner.Canonicalize(tile, i, MapWidth);
                    v.GetIncidentTiles(MapWidth, out HexCoord a, out HexCoord b, out HexCoord c);

                    Assert.That(a, Is.Not.EqualTo(b));
                    Assert.That(b, Is.Not.EqualTo(c));
                    Assert.That(a, Is.Not.EqualTo(c));
                }
            }
        }

        [Test]
        public void GetAdjacentCorners_ReturnsThreeDistinctCorners()
        {
            foreach (HexCoord tile in SampleTiles())
            {
                for (int i = 0; i < HexCorner.CornersPerTile; i++)
                {
                    HexCorner v = HexCorner.Canonicalize(tile, i, MapWidth);
                    v.GetAdjacentCorners(MapWidth, out HexCorner a, out HexCorner b, out HexCorner c);

                    Assert.That(a, Is.Not.EqualTo(v));
                    Assert.That(b, Is.Not.EqualTo(v));
                    Assert.That(c, Is.Not.EqualTo(v));
                    Assert.That(a, Is.Not.EqualTo(b));
                    Assert.That(b, Is.Not.EqualTo(c));
                    Assert.That(a, Is.Not.EqualTo(c));
                }
            }
        }

        [Test]
        public void Adjacency_IsSymmetric()
        {
            foreach (HexCoord tile in SampleTiles())
            {
                for (int i = 0; i < HexCorner.CornersPerTile; i++)
                {
                    HexCorner v = HexCorner.Canonicalize(tile, i, MapWidth);
                    foreach (HexCorner n in Adjacent(v))
                    {
                        Assert.That(Adjacent(n), Contains.Item(v),
                            $"{n} is adjacent to {v} but not vice-versa.");
                    }
                }
            }
        }

        [Test]
        public void AdjacentCorners_ShareExactlyTwoIncidentTiles()
        {
            // The structural fact the whole model rests on: two
            // adjacent corners have exactly two tiles in common, and
            // those two tiles are the endpoints of the hex edge that
            // separates them.
            foreach (HexCoord tile in SampleTiles())
            {
                for (int i = 0; i < HexCorner.CornersPerTile; i++)
                {
                    HexCorner v = HexCorner.Canonicalize(tile, i, MapWidth);
                    foreach (HexCorner n in Adjacent(v))
                    {
                        var shared = new List<HexCoord>(IncidentSet(v));
                        shared.RemoveAll(t => !IncidentSet(n).Contains(t));
                        Assert.That(shared.Count, Is.EqualTo(2),
                            $"{v} and {n} share {shared.Count} tiles, expected exactly 2.");
                    }
                }
            }
        }

        [Test]
        public void TryGetSharedEdge_NamesTheEdgeBetweenTheTwoSharedTiles()
        {
            foreach (HexCoord tile in SampleTiles())
            {
                for (int i = 0; i < HexCorner.CornersPerTile; i++)
                {
                    HexCorner v = HexCorner.Canonicalize(tile, i, MapWidth);
                    foreach (HexCorner n in Adjacent(v))
                    {
                        Assert.That(v.TryGetSharedEdge(n, MapWidth, out RiverEdge edge), Is.True,
                            $"{v} reported no shared edge with adjacent corner {n}.");

                        HexCoord near = CoordinateSpace.Normalize(edge.Coord, MapWidth);
                        HexCoord far = CoordinateSpace.Normalize(edge.Coord.Neighbor(edge.Direction), MapWidth);

                        // The named edge's two tiles must be exactly the
                        // two tiles the corners share.
                        var shared = new List<HexCoord>(IncidentSet(v));
                        shared.RemoveAll(t => !IncidentSet(n).Contains(t));

                        Assert.That(shared, Contains.Item(near),
                            $"Edge owner {near} is not shared by {v} and {n}.");
                        Assert.That(shared, Contains.Item(far),
                            $"Edge far endpoint {far} is not shared by {v} and {n}.");
                    }
                }
            }
        }

        [Test]
        public void TryGetSharedEdge_NonAdjacentCorner_ReturnsFalse()
        {
            var tile = HexCoordExtensions.FromOffsetOddR(5, 5);
            HexCorner v = HexCorner.Canonicalize(tile, 0, MapWidth);

            // The opposite corner of the same tile is two steps away.
            HexCorner opposite = HexCorner.Canonicalize(tile, 3, MapWidth);

            Assert.That(v.TryGetSharedEdge(opposite, MapWidth, out _), Is.False,
                "Opposite corners of a tile are not adjacent and share no edge.");
        }

        [Test]
        public void Equality_AndHashCode_AreValueBased()
        {
            var tile = HexCoordExtensions.FromOffsetOddR(3, 7);
            var a = new HexCorner(tile, 2);
            var b = new HexCorner(tile, 2);
            var c = new HexCorner(tile, 3);

            Assert.That(a, Is.EqualTo(b));
            Assert.That(a == b, Is.True);
            Assert.That(a != c, Is.True);
            Assert.That(a.GetHashCode(), Is.EqualTo(b.GetHashCode()));
        }

        [Test]
        public void Constructor_WrapsOutOfRangeIndex()
        {
            var tile = HexCoordExtensions.FromOffsetOddR(2, 2);
            Assert.That(new HexCorner(tile, 6).Index, Is.EqualTo(0));
            Assert.That(new HexCorner(tile, -1).Index, Is.EqualTo(5));
            Assert.That(new HexCorner(tile, 13).Index, Is.EqualTo(1));
        }

        // ---- helpers ---------------------------------------------

        private static IEnumerable<HexCoord> SampleTiles()
        {
            // Interior tiles only; wrap has its own dedicated test.
            for (int col = 4; col <= 8; col++)
            {
                for (int row = 3; row <= 7; row++)
                {
                    yield return HexCoordExtensions.FromOffsetOddR(col, row);
                }
            }
        }

        private static HexCorner[] Adjacent(HexCorner v)
        {
            v.GetAdjacentCorners(MapWidth, out HexCorner a, out HexCorner b, out HexCorner c);
            return new[] { a, b, c };
        }

        private static List<HexCoord> IncidentSet(HexCorner v)
        {
            v.GetIncidentTiles(MapWidth, out HexCoord a, out HexCoord b, out HexCoord c);
            return new List<HexCoord> { a, b, c };
        }
    }
}
