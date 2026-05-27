using System;
using HexWorldStudio.Generation.Rng;
using NUnit.Framework;

namespace HexWorldStudio.Tests
{
    /// <summary>
    /// EditMode tests for <see cref="Perlin"/> — determinism, range
    /// sanity, cylindrical seam continuity, fractal octave behavior.
    /// </summary>
    public class NoiseSourcesTests
    {
        private const ulong SampleSeed = 0xA5A5A5A5A5A5A5A5UL;

        [Test]
        public void Sample2D_SameSeed_ProducesSameOutput()
        {
            var a = new Perlin(new RngService(SampleSeed));
            var b = new Perlin(new RngService(SampleSeed));
            for (int i = 0; i < 50; i++)
            {
                float x = i * 0.317f;
                float y = i * 0.421f;
                Assert.That(a.Sample2D(x, y), Is.EqualTo(b.Sample2D(x, y)),
                    $"Divergence at sample {i}.");
            }
        }

        [Test]
        public void Sample2D_DifferentSeeds_ProduceDifferentOutput()
        {
            var a = new Perlin(new RngService(1UL));
            var b = new Perlin(new RngService(2UL));
            int matches = 0;
            for (int i = 0; i < 20; i++)
            {
                float x = i * 0.71f + 0.13f;
                float y = i * 0.39f - 0.05f;
                if (Math.Abs(a.Sample2D(x, y) - b.Sample2D(x, y)) < 1e-6f) matches++;
            }
            Assert.That(matches, Is.LessThan(20),
                "Two different Perlin seeds produced identical noise on all 20 samples.");
        }

        [Test]
        public void Sample2D_OutputInExpectedBand()
        {
            var p = new Perlin(new RngService(SampleSeed));
            for (int i = 0; i < 10000; i++)
            {
                float x = (i % 97) * 0.234f;
                float y = (i % 73) * 0.187f;
                float v = p.Sample2D(x, y);
                // Theoretical bound for 2D classic Perlin is sqrt(2)/2 ≈ 0.707,
                // but allow generous slack for any vendored variation.
                Assert.That(v, Is.GreaterThan(-1.5f).And.LessThan(1.5f),
                    $"Out-of-band 2D sample {v} at index {i}.");
            }
        }

        [Test]
        public void Sample3D_SameSeed_ProducesSameOutput()
        {
            var a = new Perlin(new RngService(SampleSeed));
            var b = new Perlin(new RngService(SampleSeed));
            for (int i = 0; i < 50; i++)
            {
                float x = i * 0.111f;
                float y = i * 0.222f;
                float z = i * 0.333f;
                Assert.That(a.Sample3D(x, y, z), Is.EqualTo(b.Sample3D(x, y, z)));
            }
        }

        [Test]
        public void Sample3D_OutputInExpectedBand()
        {
            var p = new Perlin(new RngService(SampleSeed));
            for (int i = 0; i < 10000; i++)
            {
                float x = (i % 53) * 0.41f;
                float y = (i % 61) * 0.27f;
                float z = (i % 47) * 0.33f;
                float v = p.Sample3D(x, y, z);
                Assert.That(v, Is.GreaterThan(-1.5f).And.LessThan(1.5f));
            }
        }

        [Test]
        public void CylindricalSample_AcrossSeam_IsContinuous()
        {
            var p = new Perlin(new RngService(SampleSeed));
            const float cellsX = 4f;
            const float cellsY = 3f;
            const float seamEpsilon = 1e-4f;

            for (int rowI = 0; rowI < 10; rowI++)
            {
                float row = rowI * 0.1f;
                float nearStart = p.CylindricalSample(seamEpsilon, row, cellsX, cellsY);
                float nearEnd = p.CylindricalSample(1f - seamEpsilon, row, cellsX, cellsY);
                float jumpAcrossSeam = Math.Abs(nearStart - nearEnd);
                Assert.That(jumpAcrossSeam, Is.LessThan(0.01f),
                    $"Seam jump at row {row}: |{nearStart} - {nearEnd}| = {jumpAcrossSeam}.");
            }
        }

        [Test]
        public void CylindricalSample_AtIdenticalEnds_IsExactlyEqual()
        {
            // colNorm = 0 and colNorm = 1 map to the same point on the
            // circle, so the noise sample must be identical to floating-
            // point precision.
            var p = new Perlin(new RngService(SampleSeed));
            for (int rowI = 0; rowI < 10; rowI++)
            {
                float row = rowI * 0.1f;
                float a = p.CylindricalSample(0f, row, 5f, 4f);
                float b = p.CylindricalSample(1f, row, 5f, 4f);
                Assert.That(a, Is.EqualTo(b), $"colNorm=0 and colNorm=1 diverged at row {row}.");
            }
        }

        [Test]
        public void CylindricalFractalSample_RespectsDeterminism()
        {
            var a = new Perlin(new RngService(SampleSeed));
            var b = new Perlin(new RngService(SampleSeed));
            for (int i = 0; i < 50; i++)
            {
                float c = (i % 17) * (1f / 17f);
                float r = (i % 11) * 0.09f;
                Assert.That(
                    a.CylindricalFractalSample(c, r, 4f, 3f, 4, 0.5f, 2f),
                    Is.EqualTo(b.CylindricalFractalSample(c, r, 4f, 3f, 4, 0.5f, 2f)));
            }
        }

        [Test]
        public void CylindricalFractalSample_InvalidOctaves_Throws()
        {
            var p = new Perlin(new RngService(SampleSeed));
            Assert.Throws<ArgumentOutOfRangeException>(
                () => p.CylindricalFractalSample(0f, 0f, 4f, 3f, 0, 0.5f, 2f));
            Assert.Throws<ArgumentOutOfRangeException>(
                () => p.CylindricalFractalSample(0f, 0f, 4f, 3f, -1, 0.5f, 2f));
        }

        [Test]
        public void Constructor_NullRng_Throws()
        {
            Assert.Throws<ArgumentNullException>(() => new Perlin(null));
        }
    }
}
