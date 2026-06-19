using System.Collections.Generic;
using HexWorldStudio.Content;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

namespace HexWorldStudio.Tests
{
    /// <summary>
    /// EditMode tests for <see cref="BiomeEnvelopeCoverage"/> — the pure
    /// analysis helper backing the BiomeDefinition inspector chart's
    /// gap-fraction and overlap-pair diagnostics.
    /// </summary>
    public class BiomeEnvelopeCoverageTests
    {
        private static BiomeDefinition NewBiome(
            string name,
            float minT, float maxT, float minM, float maxM,
            bool isWater = false)
        {
            var b = ScriptableObject.CreateInstance<BiomeDefinition>();
            b.BiomeName = name;
            b.MinTemperature = minT;
            b.MaxTemperature = maxT;
            b.MinMoisture = minM;
            b.MaxMoisture = maxM;
            b.IsWater = isWater;
            return b;
        }

        private static void DestroyAll(IEnumerable<BiomeDefinition> biomes)
        {
            foreach (BiomeDefinition b in biomes)
            {
                if (b != null) Object.DestroyImmediate(b);
            }
        }

        [Test]
        public void Compute_NullList_ReturnsFullGap_NoOverlaps()
        {
            BiomeEnvelopeCoverage.Result r = BiomeEnvelopeCoverage.Compute(null);
            Assert.That(r.GapFraction, Is.EqualTo(1f).Within(1e-5f));
            Assert.That(r.OverlapPairs, Is.Empty);
        }

        [Test]
        public void Compute_EmptyList_ReturnsFullGap_NoOverlaps()
        {
            BiomeEnvelopeCoverage.Result r = BiomeEnvelopeCoverage.Compute(new BiomeDefinition[0]);
            Assert.That(r.GapFraction, Is.EqualTo(1f).Within(1e-5f));
            Assert.That(r.OverlapPairs, Is.Empty);
        }

        [Test]
        public void Compute_SingleBiomeCoveringFullSquare_NoGap_NoOverlap()
        {
            var biomes = new[] { NewBiome("All", 0f, 1f, 0f, 1f) };
            try
            {
                BiomeEnvelopeCoverage.Result r = BiomeEnvelopeCoverage.Compute(biomes);
                Assert.That(r.GapFraction, Is.EqualTo(0f).Within(1e-5f));
                Assert.That(r.OverlapPairs, Is.Empty);
            }
            finally { DestroyAll(biomes); }
        }

        [Test]
        public void Compute_SingleBiomeCoveringHalfSquare_HalfGap()
        {
            // Cover (T 0-1, M 0-0.5). Half the moisture range uncovered.
            var biomes = new[] { NewBiome("Half", 0f, 1f, 0f, 0.5f) };
            try
            {
                BiomeEnvelopeCoverage.Result r = BiomeEnvelopeCoverage.Compute(biomes);
                // The grid is 21x21 inclusive; the midline (M=0.5) is covered by Max=0.5 inclusive.
                // So uncovered columns are M = 0.55..1.0 → 10 of 21 columns ≈ 47.6%.
                Assert.That(r.GapFraction, Is.GreaterThan(0.40f));
                Assert.That(r.GapFraction, Is.LessThan(0.55f));
            }
            finally { DestroyAll(biomes); }
        }

        [Test]
        public void Compute_FullDefaultPartition_NoGap_NoOverlap()
        {
            // Mirror the v1 default partition (Snow/Tundra/Arid/Temperate/Tropical) — should fully tile [0,1]² with no overlap.
            var biomes = new[]
            {
                NewBiome("Snow",      0f,    0.15f, 0f,    1f),
                NewBiome("Tundra",    0.15f, 0.4f,  0f,    1f),
                NewBiome("Arid",      0.4f,  1f,    0f,    0.4f),
                NewBiome("Temperate", 0.4f,  0.7f,  0.4f,  1f),
                NewBiome("Tropical",  0.7f,  1f,    0.4f,  1f),
            };
            try
            {
                BiomeEnvelopeCoverage.Result r = BiomeEnvelopeCoverage.Compute(biomes);
                Assert.That(r.GapFraction, Is.EqualTo(0f).Within(1e-5f),
                    "The shipped default partition must fully tile the (T,M) unit square — no gaps.");
                // Adjacent biomes share boundary edges but don't overlap on interior (closed envelopes — adjacent edges count as overlap by Min/Max equality).
                // The default partition uses strict adjacency at T=0.15, 0.4, 0.7 and M=0.4 — these create edge-only overlaps with our half-open test (>).
                // Our overlap predicate uses strict >, so edge-only touches don't count as overlaps. Verify.
                Assert.That(r.OverlapPairs, Is.Empty,
                    "The shipped default partition uses edge-only adjacency, not overlapping regions.");
            }
            finally { DestroyAll(biomes); }
        }

        [Test]
        public void Compute_TwoBiomesOverlap_DetectedOnce()
        {
            var a = NewBiome("A", 0f, 0.5f, 0f, 1f);
            var b = NewBiome("B", 0.3f, 0.8f, 0f, 1f);
            var biomes = new[] { a, b };
            try
            {
                BiomeEnvelopeCoverage.Result r = BiomeEnvelopeCoverage.Compute(biomes);
                Assert.That(r.OverlapPairs.Count, Is.EqualTo(1),
                    "Each overlap is reported once (pairwise, j > i).");
                Assert.That(r.OverlapPairs[0].A, Is.SameAs(a));
                Assert.That(r.OverlapPairs[0].B, Is.SameAs(b));
            }
            finally { DestroyAll(biomes); }
        }

        [Test]
        public void Compute_WaterBiomesIgnored()
        {
            // Even though the water biome's envelope nominally covers everything, water biomes are skipped by Compute.
            var ocean = NewBiome("Ocean", 0f, 1f, 0f, 1f, isWater: true);
            var biomes = new[] { ocean };
            try
            {
                BiomeEnvelopeCoverage.Result r = BiomeEnvelopeCoverage.Compute(biomes);
                Assert.That(r.GapFraction, Is.EqualTo(1f).Within(1e-5f),
                    "Water biomes contribute no coverage — only land biome envelopes matter at runtime.");
                Assert.That(r.OverlapPairs, Is.Empty);
            }
            finally { DestroyAll(biomes); }
        }

        [Test]
        public void Compute_NullEntriesSkipped()
        {
            var a = NewBiome("A", 0f, 1f, 0f, 1f);
            var biomes = new BiomeDefinition[] { null, a, null };
            try
            {
                BiomeEnvelopeCoverage.Result r = BiomeEnvelopeCoverage.Compute(biomes);
                Assert.That(r.GapFraction, Is.EqualTo(0f).Within(1e-5f));
                Assert.That(r.OverlapPairs, Is.Empty);
            }
            finally { Object.DestroyImmediate(a); }
        }
    }
}
