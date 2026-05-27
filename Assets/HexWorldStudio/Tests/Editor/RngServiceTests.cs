using System;
using System.Collections.Generic;
using HexWorldStudio.Generation.Rng;
using NUnit.Framework;

namespace HexWorldStudio.Tests
{
    /// <summary>
    /// EditMode tests for <see cref="RngService"/> — determinism, range
    /// correctness, distribution sanity, and the fork contract.
    /// </summary>
    public class RngServiceTests
    {
        private const ulong SampleSeed = 0x123456789ABCDEF0UL;

        [Test]
        public void Constructor_StoresSeed()
        {
            var rng = new RngService(SampleSeed);
            Assert.That(rng.Seed, Is.EqualTo(SampleSeed));
        }

        [Test]
        public void SameSeed_ProducesSameSequence()
        {
            var a = new RngService(SampleSeed);
            var b = new RngService(SampleSeed);
            for (int i = 0; i < 100; i++)
            {
                Assert.That(a.NextUInt(), Is.EqualTo(b.NextUInt()),
                    $"Sequence diverged at index {i}.");
            }
        }

        [Test]
        public void DifferentSeeds_ProduceDifferentSequences()
        {
            var a = new RngService(1UL);
            var b = new RngService(2UL);

            // Allow up to 1 collision in first 10 draws (astronomically
            // unlikely but not zero); fail only if every draw matches.
            int matches = 0;
            for (int i = 0; i < 10; i++)
            {
                if (a.NextUInt() == b.NextUInt()) matches++;
            }
            Assert.That(matches, Is.LessThan(10),
                "Sequences for seeds 1 and 2 collided on all 10 initial draws.");
        }

        [Test]
        public void NextSequence_IsStablePinned()
        {
            // PINNED REFERENCE TABLE.
            // First 8 NextUInt() outputs from seed 0x123456789ABCDEF0.
            // Captures the algorithm choice (PCG-XSH-RR-64-32) and the
            // initialization procedure (state = seed, burn one step). If
            // this test fails after a code change, the algorithm or seeding
            // has been altered — buyer save files and reproducibility are
            // at stake. Update only with deliberate ADR.
            uint[] expected = new uint[]
            {
                0x7B75F3C1u,
                0x2C1F919Au,
                0x7EC843F4u,
                0x71F83B2Eu,
                0x29019EA8u,
                0x57153F69u,
                0x06A8626Du,
                0x8E130446u,
            };

            var rng = new RngService(SampleSeed);
            uint[] actual = new uint[expected.Length];
            for (int i = 0; i < expected.Length; i++)
            {
                actual[i] = rng.NextUInt();
            }

            // Print the actuals to the test log so a failure surfaces the
            // values to pin.
            TestContext.WriteLine("Actual sequence (hex):");
            for (int i = 0; i < actual.Length; i++)
            {
                TestContext.WriteLine($"  [{i}] = 0x{actual[i]:X8}u");
            }

            Assert.That(actual, Is.EqualTo(expected),
                "Pinned PCG reference outputs changed — algorithm or seeding altered.");
        }

        [TestCase(0, 1)]
        [TestCase(0, 100)]
        [TestCase(-50, 50)]
        [TestCase(-1000, -500)]
        [TestCase(int.MinValue + 1, int.MinValue + 100)]
        [TestCase(int.MaxValue - 100, int.MaxValue)]
        public void NextInt_Bounded_RespectsRange(int min, int max)
        {
            var rng = new RngService(SampleSeed);
            for (int i = 0; i < 10000; i++)
            {
                int v = rng.NextInt(min, max);
                Assert.That(v, Is.GreaterThanOrEqualTo(min).And.LessThan(max),
                    $"Out-of-range sample at index {i}: {v} not in [{min}, {max}).");
            }
        }

        [Test]
        public void NextInt_Bounded_InvalidRange_Throws()
        {
            var rng = new RngService(SampleSeed);
            Assert.Throws<ArgumentException>(() => rng.NextInt(5, 5));
            Assert.Throws<ArgumentException>(() => rng.NextInt(5, 3));
        }

        [Test]
        public void NextFloat_IsInZeroOneInterval()
        {
            var rng = new RngService(SampleSeed);
            for (int i = 0; i < 10000; i++)
            {
                float v = rng.NextFloat();
                Assert.That(v, Is.GreaterThanOrEqualTo(0f).And.LessThan(1f),
                    $"NextFloat out of [0,1) at index {i}: {v}.");
            }
        }

        [TestCase(-1f, 1f)]
        [TestCase(0f, 100f)]
        [TestCase(-1000f, -500f)]
        public void NextFloat_Bounded_RespectsRange(float min, float max)
        {
            var rng = new RngService(SampleSeed);
            for (int i = 0; i < 10000; i++)
            {
                float v = rng.NextFloat(min, max);
                Assert.That(v, Is.GreaterThanOrEqualTo(min).And.LessThan(max),
                    $"Out-of-range sample at index {i}: {v} not in [{min}, {max}).");
            }
        }

        [Test]
        public void NextDouble_IsInZeroOneInterval()
        {
            var rng = new RngService(SampleSeed);
            for (int i = 0; i < 10000; i++)
            {
                double v = rng.NextDouble();
                Assert.That(v, Is.GreaterThanOrEqualTo(0.0).And.LessThan(1.0),
                    $"NextDouble out of [0,1) at index {i}: {v}.");
            }
        }

        [Test]
        public void NextBool_Uniform_RoughlyHalfTrue()
        {
            var rng = new RngService(SampleSeed);
            int trues = 0;
            const int samples = 100000;
            for (int i = 0; i < samples; i++)
            {
                if (rng.NextBool()) trues++;
            }
            double ratio = (double)trues / samples;
            Assert.That(ratio, Is.InRange(0.49, 0.51),
                $"NextBool() ratio of true was {ratio:F4}, expected ~0.5.");
        }

        [TestCase(0.25f)]
        [TestCase(0.50f)]
        [TestCase(0.75f)]
        public void NextBool_Weighted_ApproximatesProbability(float p)
        {
            var rng = new RngService(SampleSeed);
            int trues = 0;
            const int samples = 100000;
            for (int i = 0; i < samples; i++)
            {
                if (rng.NextBool(p)) trues++;
            }
            double ratio = (double)trues / samples;
            Assert.That(ratio, Is.InRange(p - 0.01, p + 0.01),
                $"NextBool({p}) ratio was {ratio:F4}, expected within {p}±0.01.");
        }

        [Test]
        public void NextBool_WeightedZero_AlwaysFalse()
        {
            var rng = new RngService(SampleSeed);
            for (int i = 0; i < 1000; i++)
            {
                Assert.That(rng.NextBool(0f), Is.False);
            }
        }

        [Test]
        public void NextBool_WeightedOne_AlwaysTrue()
        {
            var rng = new RngService(SampleSeed);
            for (int i = 0; i < 1000; i++)
            {
                Assert.That(rng.NextBool(1f), Is.True);
            }
        }

        [Test]
        public void Fork_ChildIsIndependentOfParent()
        {
            var parent = new RngService(SampleSeed);
            RngService child = parent.Fork();

            // Capture the child's first values before the parent draws.
            uint[] childBefore = new uint[10];
            for (int i = 0; i < 10; i++) childBefore[i] = child.NextUInt();

            // Reconstruct a sibling fork from a fresh parent in the same
            // state and have the *parent* draw aggressively in between.
            var parent2 = new RngService(SampleSeed);
            RngService child2 = parent2.Fork();
            for (int i = 0; i < 1000; i++) parent2.NextUInt();

            uint[] childAfter = new uint[10];
            for (int i = 0; i < 10; i++) childAfter[i] = child2.NextUInt();

            Assert.That(childAfter, Is.EqualTo(childBefore),
                "Parent draws after Fork() affected the child's sequence.");
        }

        [Test]
        public void Fork_TwoForksFromSameState_ProduceSameChild()
        {
            var p1 = new RngService(SampleSeed);
            var p2 = new RngService(SampleSeed);
            // Advance both parents identically.
            for (int i = 0; i < 50; i++) { p1.NextUInt(); p2.NextUInt(); }

            RngService c1 = p1.Fork();
            RngService c2 = p2.Fork();
            for (int i = 0; i < 50; i++)
            {
                Assert.That(c1.NextUInt(), Is.EqualTo(c2.NextUInt()),
                    $"Sibling children diverged at index {i}.");
            }
        }

        [Test]
        public void Fork_ParentContinuesUnaffected()
        {
            var p1 = new RngService(SampleSeed);
            var p2 = new RngService(SampleSeed);

            // p1 forks; p2 does not. Both should produce identical
            // subsequent sequences.
            _ = p1.Fork();

            for (int i = 0; i < 100; i++)
            {
                Assert.That(p1.NextUInt(), Is.EqualTo(p2.NextUInt()),
                    $"Parent diverged from no-fork baseline at index {i}.");
            }
        }

        [Test]
        public void ForkLabeled_SameLabel_SameSeed_DeterministicChild()
        {
            var p1 = new RngService(SampleSeed);
            var p2 = new RngService(SampleSeed);

            RngService c1 = p1.Fork("elevation");
            RngService c2 = p2.Fork("elevation");

            for (int i = 0; i < 50; i++)
            {
                Assert.That(c1.NextUInt(), Is.EqualTo(c2.NextUInt()),
                    $"Labeled children diverged at index {i}.");
            }
        }

        [Test]
        public void ForkLabeled_DifferentLabels_DifferentChildren()
        {
            var p = new RngService(SampleSeed);
            RngService cElev = p.Fork("elevation");
            RngService cRivers = p.Fork("rivers");

            // The first draws should differ. (Allow that a single value
            // could collide, but not all of the first 5.)
            int matches = 0;
            for (int i = 0; i < 5; i++)
            {
                if (cElev.NextUInt() == cRivers.NextUInt()) matches++;
            }
            Assert.That(matches, Is.LessThan(5),
                "Labeled forks with different labels produced identical sequences.");
        }

        [Test]
        public void ForkLabeled_NullLabel_Throws()
        {
            var p = new RngService(SampleSeed);
            Assert.Throws<ArgumentNullException>(() => p.Fork(null));
        }

        [Test]
        public void State_AdvancesAfterEachDraw()
        {
            var rng = new RngService(SampleSeed);
            ulong before = rng.State;
            rng.NextUInt();
            ulong after = rng.State;
            Assert.That(after, Is.Not.EqualTo(before));
        }

        [Test]
        public void SequenceLength_NoEarlyCollision_For100KSamples()
        {
            // Sanity: 100k draws should produce a healthy spread of distinct
            // values. With a 32-bit output and 100k draws the birthday
            // paradox predicts ~1.16 collisions on average, so we allow a
            // small number of duplicates rather than zero.
            var rng = new RngService(SampleSeed);
            var seen = new HashSet<uint>();
            int collisions = 0;
            for (int i = 0; i < 100000; i++)
            {
                if (!seen.Add(rng.NextUInt())) collisions++;
            }
            Assert.That(collisions, Is.LessThan(20),
                $"Unexpectedly many early collisions ({collisions}) — possible algorithm regression.");
        }

        [Test]
        public void NextInt_DistributionLooksUniform()
        {
            var rng = new RngService(SampleSeed);
            const int buckets = 10;
            const int samples = 100000;
            int[] counts = new int[buckets];
            for (int i = 0; i < samples; i++)
            {
                int v = rng.NextInt(0, buckets);
                counts[v]++;
            }

            double expected = samples / (double)buckets;
            for (int b = 0; b < buckets; b++)
            {
                double dev = Math.Abs(counts[b] - expected) / expected;
                Assert.That(dev, Is.LessThan(0.05),
                    $"Bucket {b} deviated by {dev:P2} from expected uniform (count={counts[b]}).");
            }
        }

        [Test]
        public void NextFloat_DistributionLooksUniform()
        {
            var rng = new RngService(SampleSeed);
            const int buckets = 10;
            const int samples = 100000;
            int[] counts = new int[buckets];
            for (int i = 0; i < samples; i++)
            {
                int b = (int)(rng.NextFloat() * buckets);
                if (b == buckets) b = buckets - 1; // guard exact 1.0 (shouldn't happen, defensive)
                counts[b]++;
            }

            double expected = samples / (double)buckets;
            for (int b = 0; b < buckets; b++)
            {
                double dev = Math.Abs(counts[b] - expected) / expected;
                Assert.That(dev, Is.LessThan(0.05),
                    $"Bucket {b} deviated by {dev:P2} from expected uniform (count={counts[b]}).");
            }
        }
    }
}
