using HexWorldStudio.Generation;
using NUnit.Framework;

namespace HexWorldStudio.Tests
{
    /// <summary>
    /// EditMode tests for <see cref="WorldGenerationSettings"/> —
    /// constructor stores values without validation, reference-type
    /// semantics.
    /// </summary>
    public class WorldGenerationSettingsTests
    {
        [Test]
        public void Constructor_StoresWidthHeightSeed()
        {
            var s = new WorldGenerationSettings(80, 50, 0xDEADBEEFCAFEBABEUL);
            Assert.That(s.Width, Is.EqualTo(80));
            Assert.That(s.Height, Is.EqualTo(50));
            Assert.That(s.Seed, Is.EqualTo(0xDEADBEEFCAFEBABEUL));
        }

        [Test]
        public void Constructor_AcceptsZeroOrNegativeWidth_WithoutThrowing()
        {
            // Validation happens at Generate(), not in the ctor — settings
            // can be built incrementally from inspector fields.
            Assert.DoesNotThrow(() => new WorldGenerationSettings(0, 50, 0UL));
            Assert.DoesNotThrow(() => new WorldGenerationSettings(-5, 50, 0UL));
        }

        [Test]
        public void Constructor_AcceptsZeroOrNegativeHeight_WithoutThrowing()
        {
            Assert.DoesNotThrow(() => new WorldGenerationSettings(80, 0, 0UL));
            Assert.DoesNotThrow(() => new WorldGenerationSettings(80, -5, 0UL));
        }

        [Test]
        public void TwoInstancesWithSameValues_AreNotEqualUnderDefaultEquals()
        {
            // Pins WorldGenerationSettings as a normal reference type
            // (configuration container), not a value record. Buyers
            // should not rely on instance equality to identify
            // configurations.
            var a = new WorldGenerationSettings(80, 50, 42UL);
            var b = new WorldGenerationSettings(80, 50, 42UL);
            Assert.That(a.Equals(b), Is.False);
            Assert.That(ReferenceEquals(a, b), Is.False);
        }
    }
}
