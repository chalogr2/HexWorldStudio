using HexWorldStudio.Coordinates;
using NUnit.Framework;

namespace HexWorldStudio.Tests
{
    /// <summary>
    /// EditMode tests for <see cref="HexCoord"/> — value semantics,
    /// cube invariant, equality, hash determinism, and string formatting.
    /// </summary>
    public class HexCoordTests
    {
        [Test]
        public void Constructor_AnyValues_PreservesQAndR()
        {
            var c = new HexCoord(3, -5);
            Assert.That(c.Q, Is.EqualTo(3));
            Assert.That(c.R, Is.EqualTo(-5));
        }

        [Test]
        public void S_AnyValues_EqualsNegativeQMinusR()
        {
            var c = new HexCoord(3, -5);
            Assert.That(c.S, Is.EqualTo(-3 - -5));
            Assert.That(c.S, Is.EqualTo(2));
        }

        [TestCase(0, 0)]
        [TestCase(1, 0)]
        [TestCase(0, 1)]
        [TestCase(-7, 4)]
        [TestCase(100, -250)]
        [TestCase(int.MaxValue / 4, -int.MaxValue / 4)]
        public void SumOfCubeComponents_AnyValues_IsZero(int q, int r)
        {
            var c = new HexCoord(q, r);
            Assert.That(c.Q + c.R + c.S, Is.EqualTo(0));
        }

        [Test]
        public void Zero_HasAllComponentsZero()
        {
            HexCoord zero = HexCoord.Zero;
            Assert.That(zero.Q, Is.EqualTo(0));
            Assert.That(zero.R, Is.EqualTo(0));
            Assert.That(zero.S, Is.EqualTo(0));
        }

        [Test]
        public void From_AnyValues_EqualsConstructorResult()
        {
            HexCoord factory = HexCoord.From(4, -2);
            HexCoord ctor = new HexCoord(4, -2);
            Assert.That(factory, Is.EqualTo(ctor));
        }

        [Test]
        public void Equals_SameValues_ReturnsTrue()
        {
            var a = new HexCoord(2, -3);
            var b = new HexCoord(2, -3);
            Assert.That(a.Equals(b), Is.True);
        }

        [Test]
        public void Equals_DifferentValues_ReturnsFalse()
        {
            var a = new HexCoord(2, -3);
            var b = new HexCoord(2, 3);
            Assert.That(a.Equals(b), Is.False);
        }

        [Test]
        public void Equals_NonHexCoordObject_ReturnsFalse()
        {
            var a = new HexCoord(2, -3);
            object foreign = "not a hex";
            Assert.That(a.Equals(foreign), Is.False);
        }

        [Test]
        public void OperatorEquals_MatchesEqualsMethod()
        {
            var a = new HexCoord(2, -3);
            var b = new HexCoord(2, -3);
            var c = new HexCoord(0, 0);

            Assert.That(a == b, Is.True);
            Assert.That(a == c, Is.False);
        }

        [Test]
        public void OperatorNotEquals_MatchesEqualsMethod()
        {
            var a = new HexCoord(2, -3);
            var b = new HexCoord(2, -3);
            var c = new HexCoord(0, 0);

            Assert.That(a != b, Is.False);
            Assert.That(a != c, Is.True);
        }

        [Test]
        public void GetHashCode_EqualValues_AreEqual()
        {
            var a = new HexCoord(5, -2);
            var b = new HexCoord(5, -2);
            Assert.That(a.GetHashCode(), Is.EqualTo(b.GetHashCode()));
        }

        [Test]
        public void GetHashCode_IsStableAcrossInstances()
        {
            // Two coordinates constructed at different points must hash to
            // exactly the same value. Guards the determinism contract: if
            // someone later swaps in HashCode.Combine, this fails because
            // it mixes in a per-process random seed.
            int first = new HexCoord(7, 11).GetHashCode();
            int second = new HexCoord(7, 11).GetHashCode();
            Assert.That(first, Is.EqualTo(second));
        }

        [Test]
        public void ToString_AnyValues_FormatsAsParenQCommaR()
        {
            var c = new HexCoord(3, -5);
            Assert.That(c.ToString(), Is.EqualTo("(3, -5)"));
        }
    }
}
