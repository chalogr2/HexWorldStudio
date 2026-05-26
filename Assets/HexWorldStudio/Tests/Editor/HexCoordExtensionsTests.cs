using System;
using System.Collections.Generic;
using System.Linq;
using HexWorldStudio.Coordinates;
using NUnit.Framework;

namespace HexWorldStudio.Tests
{
    /// <summary>
    /// EditMode tests for <see cref="HexCoordExtensions"/> — distance,
    /// neighbors, range, line drawing, and offset round-trips.
    /// </summary>
    public class HexCoordExtensionsTests
    {
        // The same axial unit vectors HexCoordExtensions uses internally,
        // restated here so the tests don't depend on the production
        // private table; pins the contract from the outside.
        private static readonly HexCoord[] s_expectedDirections = new HexCoord[]
        {
            new HexCoord(+1,  0), // East
            new HexCoord(+1, -1), // NorthEast
            new HexCoord( 0, -1), // NorthWest
            new HexCoord(-1,  0), // West
            new HexCoord(-1, +1), // SouthWest
            new HexCoord( 0, +1), // SouthEast
        };

        [Test]
        public void Distance_ToSelf_IsZero()
        {
            var a = new HexCoord(3, -1);
            Assert.That(a.Distance(a), Is.EqualTo(0));
        }

        [TestCase(0)]
        [TestCase(1)]
        [TestCase(2)]
        [TestCase(3)]
        [TestCase(4)]
        [TestCase(5)]
        public void Distance_AdjacentTiles_IsOne(int directionIndex)
        {
            var center = new HexCoord(2, -3);
            HexCoord neighbor = center.Neighbor((HexDirection)directionIndex);
            Assert.That(center.Distance(neighbor), Is.EqualTo(1));
        }

        [Test]
        public void Distance_TwoSteps_IsTwo()
        {
            var a = new HexCoord(0, 0);
            var b = new HexCoord(2, 0); // two steps east
            Assert.That(a.Distance(b), Is.EqualTo(2));

            var c = new HexCoord(1, -2); // (1,-1) then (0,-1)
            Assert.That(a.Distance(c), Is.EqualTo(2));
        }

        [Test]
        public void Distance_TenSteps_IsTen()
        {
            var a = new HexCoord(0, 0);
            var b = new HexCoord(10, 0);
            Assert.That(a.Distance(b), Is.EqualTo(10));
        }

        [Test]
        public void Distance_IsSymmetric()
        {
            (HexCoord, HexCoord)[] pairs = new[]
            {
                (new HexCoord(0, 0), new HexCoord(3, -5)),
                (new HexCoord(-2, 7), new HexCoord(4, -1)),
                (new HexCoord(100, -50), new HexCoord(-100, 50)),
            };

            foreach (var (a, b) in pairs)
            {
                Assert.That(a.Distance(b), Is.EqualTo(b.Distance(a)),
                    $"Distance not symmetric for {a} and {b}.");
            }
        }

        [TestCase(0)]
        [TestCase(1)]
        [TestCase(2)]
        [TestCase(3)]
        [TestCase(4)]
        [TestCase(5)]
        public void Neighbor_EachDirection_IsExactlyOneStepAway(int directionIndex)
        {
            var center = new HexCoord(0, 0);
            HexCoord n = center.Neighbor((HexDirection)directionIndex);
            Assert.That(center.Distance(n), Is.EqualTo(1));
        }

        [Test]
        public void Neighbors_ReturnsExactlySixCoords()
        {
            var center = new HexCoord(3, -2);
            int count = center.Neighbors().Count();
            Assert.That(count, Is.EqualTo(6));
        }

        [Test]
        public void Neighbors_AllReturnedCoordsAreUnique()
        {
            var center = new HexCoord(0, 0);
            List<HexCoord> neighbors = center.Neighbors().ToList();
            Assert.That(neighbors.Distinct().Count(), Is.EqualTo(neighbors.Count));
        }

        [Test]
        public void Neighbors_OrderMatchesHexDirectionEnum()
        {
            var center = new HexCoord(5, -1);
            HexCoord[] neighbors = center.Neighbors().ToArray();

            for (int i = 0; i < s_expectedDirections.Length; i++)
            {
                HexCoord expected = new HexCoord(
                    center.Q + s_expectedDirections[i].Q,
                    center.R + s_expectedDirections[i].R);
                Assert.That(neighbors[i], Is.EqualTo(expected),
                    $"Neighbor index {i} (HexDirection.{(HexDirection)i}) did not match the expected vector.");
            }
        }

        [Test]
        public void Range_RadiusZero_ReturnsOnlyCenter()
        {
            var center = new HexCoord(2, -3);
            IReadOnlyList<HexCoord> range = center.Range(0);
            Assert.That(range.Count, Is.EqualTo(1));
            Assert.That(range[0], Is.EqualTo(center));
        }

        [Test]
        public void Range_RadiusOne_ContainsCenterAndAllNeighbors()
        {
            var center = new HexCoord(0, 0);
            IReadOnlyList<HexCoord> range = center.Range(1);
            Assert.That(range.Count, Is.EqualTo(7));

            HashSet<HexCoord> set = new HashSet<HexCoord>(range);
            Assert.That(set.Contains(center), Is.True, "Range(1) must contain the center.");
            foreach (HexCoord n in center.Neighbors())
            {
                Assert.That(set.Contains(n), Is.True, $"Range(1) must contain neighbor {n}.");
            }
        }

        [TestCase(0)]
        [TestCase(1)]
        [TestCase(2)]
        [TestCase(3)]
        [TestCase(4)]
        [TestCase(5)]
        public void Range_CountFormula_Holds(int radius)
        {
            var center = new HexCoord(0, 0);
            int expected = 3 * radius * (radius + 1) + 1;
            Assert.That(center.Range(radius).Count, Is.EqualTo(expected));
        }

        [Test]
        public void Range_NegativeRadius_Throws()
        {
            var center = new HexCoord(0, 0);
            Assert.Throws<ArgumentOutOfRangeException>(() => center.Range(-1));
        }

        [Test]
        public void Line_PointToSelf_ReturnsSingleton()
        {
            var a = new HexCoord(2, -3);
            IReadOnlyList<HexCoord> line = a.Line(a);
            Assert.That(line.Count, Is.EqualTo(1));
            Assert.That(line[0], Is.EqualTo(a));
        }

        [Test]
        public void Line_AdjacentTiles_ReturnsBothEndpoints()
        {
            var a = new HexCoord(0, 0);
            HexCoord b = a.Neighbor(HexDirection.East);
            IReadOnlyList<HexCoord> line = a.Line(b);
            Assert.That(line.Count, Is.EqualTo(2));
            Assert.That(line[0], Is.EqualTo(a));
            Assert.That(line[1], Is.EqualTo(b));
        }

        [Test]
        public void Line_LengthEqualsDistancePlusOne()
        {
            (HexCoord, HexCoord)[] pairs = new[]
            {
                (new HexCoord(0, 0), new HexCoord(5, 0)),
                (new HexCoord(-3, 2), new HexCoord(4, -1)),
                (new HexCoord(0, 0), new HexCoord(0, 10)),
            };

            foreach (var (a, b) in pairs)
            {
                int expected = a.Distance(b) + 1;
                Assert.That(a.Line(b).Count, Is.EqualTo(expected),
                    $"Line length mismatch from {a} to {b}.");
            }
        }

        [Test]
        public void Line_IncludesBothEndpoints()
        {
            var a = new HexCoord(-3, 2);
            var b = new HexCoord(4, -1);
            IReadOnlyList<HexCoord> line = a.Line(b);
            Assert.That(line[0], Is.EqualTo(a));
            Assert.That(line[line.Count - 1], Is.EqualTo(b));
        }

        [Test]
        public void Line_ConsecutiveCellsAreAdjacent()
        {
            var a = new HexCoord(-4, 3);
            var b = new HexCoord(5, -2);
            IReadOnlyList<HexCoord> line = a.Line(b);

            for (int i = 1; i < line.Count; i++)
            {
                Assert.That(line[i - 1].Distance(line[i]), Is.EqualTo(1),
                    $"Cells at index {i - 1} and {i} are not adjacent.");
            }
        }

        [TestCase(0, 0)]
        [TestCase(1, 0)]
        [TestCase(0, 1)]
        [TestCase(-3, 5)]
        [TestCase(7, -4)]
        [TestCase(-12, -8)]
        public void OffsetOddR_RoundTrips(int q, int r)
        {
            var original = new HexCoord(q, r);
            (int col, int row) = original.ToOffsetOddR();
            HexCoord recovered = HexCoordExtensions.FromOffsetOddR(col, row);
            Assert.That(recovered, Is.EqualTo(original));
        }

        [TestCase(0, 0)]
        [TestCase(1, 0)]
        [TestCase(0, 1)]
        [TestCase(-3, 5)]
        [TestCase(7, -4)]
        [TestCase(-12, -8)]
        public void OffsetEvenR_RoundTrips(int q, int r)
        {
            var original = new HexCoord(q, r);
            (int col, int row) = original.ToOffsetEvenR();
            HexCoord recovered = HexCoordExtensions.FromOffsetEvenR(col, row);
            Assert.That(recovered, Is.EqualTo(original));
        }

        [Test]
        public void ToOffsetOddR_KnownValues_MatchesReference()
        {
            // Hand-computed values that pin the odd-r convention
            // (odd rows shifted right). Mismatches mean the convention
            // has been silently changed.
            Assert.That(new HexCoord(0, 0).ToOffsetOddR(), Is.EqualTo((0, 0)));
            Assert.That(new HexCoord(1, 0).ToOffsetOddR(), Is.EqualTo((1, 0)));
            Assert.That(new HexCoord(0, 1).ToOffsetOddR(), Is.EqualTo((0, 1)));
            Assert.That(new HexCoord(0, 2).ToOffsetOddR(), Is.EqualTo((1, 2)));
            Assert.That(new HexCoord(-2, 3).ToOffsetOddR(), Is.EqualTo((-1, 3)));
        }

        [Test]
        public void ToOffsetEvenR_KnownValues_MatchesReference()
        {
            // Hand-computed values that pin the even-r convention
            // (even rows shifted right).
            Assert.That(new HexCoord(0, 0).ToOffsetEvenR(), Is.EqualTo((0, 0)));
            Assert.That(new HexCoord(1, 0).ToOffsetEvenR(), Is.EqualTo((1, 0)));
            Assert.That(new HexCoord(0, 1).ToOffsetEvenR(), Is.EqualTo((1, 1)));
            Assert.That(new HexCoord(0, 2).ToOffsetEvenR(), Is.EqualTo((1, 2)));
            Assert.That(new HexCoord(-2, 3).ToOffsetEvenR(), Is.EqualTo((0, 3)));
        }
    }
}
