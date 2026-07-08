using System.Collections.Generic;
using HexWorldStudio.Coordinates;
using HexWorldStudio.Data;
using NUnit.Framework;

namespace HexWorldStudio.Tests
{
    /// <summary>
    /// EditMode tests for <see cref="WorldMap"/>'s river-adjacency
    /// query surface — <see cref="WorldMap.HasRiverAt"/>,
    /// <see cref="WorldMap.CountRiverEdgesAround"/>, and the empty
    /// / stable-order contracts on <see cref="WorldMap.RiverEdges"/>.
    /// </summary>
    public class WorldMapRiversTests
    {
        private const int Width = 12;
        private const int Height = 8;
        private const ulong SampleSeed = 0xC0DEuL;

        [Test]
        public void RiverEdges_BeforeAnySetRivers_IsEmpty()
        {
            var map = new WorldMap(Width, Height, SampleSeed);
            Assert.That(map.RiverEdges, Is.Not.Null);
            Assert.That(map.RiverEdges.Count, Is.EqualTo(0));
        }

        [Test]
        public void HasRiverAt_EmptyMap_ReturnsFalseForEveryEdge()
        {
            var map = new WorldMap(Width, Height, SampleSeed);
            foreach (HexTile t in map.Tiles)
            {
                for (int d = 0; d < 6; d++)
                {
                    Assert.That(map.HasRiverAt(t.Coord, (HexDirection)d), Is.False);
                }
            }
        }

        [Test]
        public void CountRiverEdgesAround_EmptyMap_AlwaysZero()
        {
            var map = new WorldMap(Width, Height, SampleSeed);
            foreach (HexTile t in map.Tiles)
            {
                Assert.That(map.CountRiverEdgesAround(t.Coord), Is.EqualTo(0));
            }
        }

        [Test]
        public void HasRiverAt_AcceptsEitherEndpointNaming()
        {
            var map = new WorldMap(Width, Height, SampleSeed);
            HexCoord a = new HexCoord(3, 3);
            RiverEdge e = RiverEdge.Canonicalize(a, HexDirection.East, Width);
            map.SetRivers(new[] { e });

            // Both (a, East) and (a.Neighbor(East), West) must
            // resolve to a hit — the query is direction-agnostic.
            Assert.That(map.HasRiverAt(a, HexDirection.East), Is.True);
            Assert.That(map.HasRiverAt(a.Neighbor(HexDirection.East), HexDirection.West), Is.True);
        }

        [Test]
        public void HasRiverAt_WrongDirection_MissesTheEdge()
        {
            var map = new WorldMap(Width, Height, SampleSeed);
            HexCoord a = new HexCoord(3, 3);
            RiverEdge e = RiverEdge.Canonicalize(a, HexDirection.East, Width);
            map.SetRivers(new[] { e });

            Assert.That(map.HasRiverAt(a, HexDirection.West), Is.False);
            Assert.That(map.HasRiverAt(a, HexDirection.NorthEast), Is.False);
        }

        [Test]
        public void CountRiverEdgesAround_ThreeIncidentEdges_ReturnsThree()
        {
            var map = new WorldMap(Width, Height, SampleSeed);
            HexCoord center = new HexCoord(5, 4);
            var edges = new List<RiverEdge>
            {
                RiverEdge.Canonicalize(center, HexDirection.East, Width),
                RiverEdge.Canonicalize(center, HexDirection.NorthEast, Width),
                RiverEdge.Canonicalize(center, HexDirection.SouthEast, Width),
            };
            map.SetRivers(edges);

            Assert.That(map.CountRiverEdgesAround(center), Is.EqualTo(3));
        }

        [Test]
        public void SetRivers_DeduplicatesAlternateNamings()
        {
            var map = new WorldMap(Width, Height, SampleSeed);
            HexCoord a = new HexCoord(4, 4);
            // Two names for the same edge — both must collapse.
            var e1 = RiverEdge.Canonicalize(a, HexDirection.East, Width);
            var e2 = RiverEdge.Canonicalize(a.Neighbor(HexDirection.East), HexDirection.West, Width);
            map.SetRivers(new[] { e1, e2 });

            Assert.That(map.RiverEdges.Count, Is.EqualTo(1),
                "Alternate namings of the same edge must be deduplicated on write.");
        }

        [Test]
        public void SetRivers_ProducesStableSortedOrder()
        {
            var map = new WorldMap(Width, Height, SampleSeed);
            var input = new List<RiverEdge>
            {
                RiverEdge.Canonicalize(new HexCoord(7, 2), HexDirection.East, Width),
                RiverEdge.Canonicalize(new HexCoord(1, 1), HexDirection.NorthEast, Width),
                RiverEdge.Canonicalize(new HexCoord(3, 3), HexDirection.West, Width),
            };
            map.SetRivers(input);

            // Documented order: owner.Q asc, then owner.R asc, then Direction asc.
            // Assert non-decreasing under that lex order.
            for (int i = 1; i < map.RiverEdges.Count; i++)
            {
                RiverEdge prev = map.RiverEdges[i - 1];
                RiverEdge cur  = map.RiverEdges[i];
                bool nonDecreasing =
                    prev.Coord.Q < cur.Coord.Q ||
                    (prev.Coord.Q == cur.Coord.Q && prev.Coord.R < cur.Coord.R) ||
                    (prev.Coord.Q == cur.Coord.Q && prev.Coord.R == cur.Coord.R && (int)prev.Direction <= (int)cur.Direction);
                Assert.That(nonDecreasing, Is.True,
                    $"RiverEdges order broken between index {i - 1} and {i}: {prev} then {cur}");
            }
        }

        [Test]
        public void SetRivers_Null_ClearsExistingRivers()
        {
            var map = new WorldMap(Width, Height, SampleSeed);
            map.SetRivers(new[] { RiverEdge.Canonicalize(new HexCoord(1, 1), HexDirection.East, Width) });
            Assert.That(map.RiverEdges.Count, Is.EqualTo(1));

            map.SetRivers(null);

            Assert.That(map.RiverEdges.Count, Is.EqualTo(0));
        }
    }
}
