using HexWorldStudio.Coordinates;
using HexWorldStudio.Data;
using NUnit.Framework;

namespace HexWorldStudio.Tests
{
    /// <summary>
    /// EditMode tests for <see cref="RiverEdge"/> — value equality,
    /// canonical form for both non-wrap and wrap-crossing edges,
    /// direction opposite semantics.
    /// </summary>
    public class RiverEdgeTests
    {
        private const int Width = 16;

        [Test]
        public void Opposite_ReturnsRedBlobOppositeDirection()
        {
            Assert.That(RiverEdge.Opposite(HexDirection.East),      Is.EqualTo(HexDirection.West));
            Assert.That(RiverEdge.Opposite(HexDirection.West),      Is.EqualTo(HexDirection.East));
            Assert.That(RiverEdge.Opposite(HexDirection.NorthEast), Is.EqualTo(HexDirection.SouthWest));
            Assert.That(RiverEdge.Opposite(HexDirection.SouthWest), Is.EqualTo(HexDirection.NorthEast));
            Assert.That(RiverEdge.Opposite(HexDirection.NorthWest), Is.EqualTo(HexDirection.SouthEast));
            Assert.That(RiverEdge.Opposite(HexDirection.SouthEast), Is.EqualTo(HexDirection.NorthWest));
        }

        [Test]
        public void Equality_TwoIdenticalEdges_AreEqual()
        {
            var a = new RiverEdge(new HexCoord(3, 4), HexDirection.East);
            var b = new RiverEdge(new HexCoord(3, 4), HexDirection.East);
            Assert.That(a.Equals(b), Is.True);
            Assert.That(a == b, Is.True);
            Assert.That(a.GetHashCode(), Is.EqualTo(b.GetHashCode()));
        }

        [Test]
        public void Equality_DifferentDirection_NotEqual()
        {
            var a = new RiverEdge(new HexCoord(3, 4), HexDirection.East);
            var b = new RiverEdge(new HexCoord(3, 4), HexDirection.NorthEast);
            Assert.That(a.Equals(b), Is.False);
            Assert.That(a != b, Is.True);
        }

        [Test]
        public void Canonicalize_NonWrapEdge_TwoNamingsCollapseToSameForm()
        {
            // Edge between (2, 3) and its East neighbor (3, 3).
            // Both (2, 3, East) and (3, 3, West) must canonicalize
            // to the same value. Lex-smaller owner is (2, 3).
            var a = RiverEdge.Canonicalize(new HexCoord(2, 3), HexDirection.East, Width);
            var b = RiverEdge.Canonicalize(new HexCoord(3, 3), HexDirection.West, Width);

            Assert.That(a, Is.EqualTo(b));
            Assert.That(a.Coord, Is.EqualTo(new HexCoord(2, 3)));
            Assert.That(a.Direction, Is.EqualTo(HexDirection.East));
        }

        [Test]
        public void Canonicalize_WrapCrossingEdge_TwoNamingsCollapseToSameForm()
        {
            // Edge between (0, 3) and (Width-1, 3) via cylindrical
            // wrap. Naming (0, 3, West) references raw (-1, 3) which
            // normalizes to (Width-1, 3). Naming (Width-1, 3, East)
            // references raw (Width, 3) which normalizes to (0, 3).
            // Both must canonicalize to the same value; lex-smaller
            // owner is (0, 3).
            var a = RiverEdge.Canonicalize(new HexCoord(0, 3), HexDirection.West, Width);
            var b = RiverEdge.Canonicalize(new HexCoord(Width - 1, 3), HexDirection.East, Width);

            Assert.That(a, Is.EqualTo(b));
            Assert.That(a.Coord, Is.EqualTo(new HexCoord(0, 3)));
            Assert.That(a.Direction, Is.EqualTo(HexDirection.West));
        }

        [Test]
        public void Canonicalize_AllSixDirectionsFromCenter_RoundTripsThroughOpposite()
        {
            // For each of the six directions, canonicalizing (a, dir)
            // and (neighbor, opposite(dir)) must give the same value.
            HexCoord center = new HexCoord(5, 5);
            for (int i = 0; i < 6; i++)
            {
                HexDirection dir = (HexDirection)i;
                HexCoord neighbor = center.Neighbor(dir);

                var forward = RiverEdge.Canonicalize(center, dir, Width);
                var reverse = RiverEdge.Canonicalize(neighbor, RiverEdge.Opposite(dir), Width);

                Assert.That(reverse, Is.EqualTo(forward),
                    $"Direction {dir}: naming from neighbor+opposite failed to canonicalize equally.");
            }
        }

        [Test]
        public void ToString_ProducesReadableDiagnostic()
        {
            var e = new RiverEdge(new HexCoord(7, 2), HexDirection.NorthEast);
            string s = e.ToString();
            Assert.That(s, Does.Contain("(7, 2)"));
            Assert.That(s, Does.Contain("NorthEast"));
        }
    }
}
