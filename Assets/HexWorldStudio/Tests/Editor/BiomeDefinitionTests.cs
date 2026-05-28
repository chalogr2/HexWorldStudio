using HexWorldStudio.Content;
using NUnit.Framework;
using UnityEngine;

namespace HexWorldStudio.Tests
{
    /// <summary>
    /// EditMode tests for <see cref="BiomeDefinition"/> — climate
    /// envelope matching, water/coastal flag semantics.
    /// </summary>
    public class BiomeDefinitionTests
    {
        private static BiomeDefinition NewBiome(
            string name = "Test",
            bool isWater = false,
            bool isCoastalWater = false,
            float minT = 0f, float maxT = 1f,
            float minM = 0f, float maxM = 1f)
        {
            var b = ScriptableObject.CreateInstance<BiomeDefinition>();
            b.BiomeName = name;
            b.IsWater = isWater;
            b.IsCoastalWater = isCoastalWater;
            b.MinTemperature = minT;
            b.MaxTemperature = maxT;
            b.MinMoisture = minM;
            b.MaxMoisture = maxM;
            return b;
        }

        [Test]
        public void Matches_LandBiome_InsideEnvelope_ReturnsTrue()
        {
            BiomeDefinition b = NewBiome(minT: 0.3f, maxT: 0.7f, minM: 0.2f, maxM: 0.8f);
            Assert.That(b.Matches(0.5f, 0.5f), Is.True);
        }

        [Test]
        public void Matches_LandBiome_OutsideTemperature_ReturnsFalse()
        {
            BiomeDefinition b = NewBiome(minT: 0.3f, maxT: 0.7f, minM: 0.2f, maxM: 0.8f);
            Assert.That(b.Matches(0.1f, 0.5f), Is.False);
            Assert.That(b.Matches(0.9f, 0.5f), Is.False);
        }

        [Test]
        public void Matches_LandBiome_OutsideMoisture_ReturnsFalse()
        {
            BiomeDefinition b = NewBiome(minT: 0.3f, maxT: 0.7f, minM: 0.2f, maxM: 0.8f);
            Assert.That(b.Matches(0.5f, 0.1f), Is.False);
            Assert.That(b.Matches(0.5f, 0.9f), Is.False);
        }

        [Test]
        public void Matches_LandBiome_OnEnvelopeBoundary_ReturnsTrue()
        {
            // Closed-inclusive intervals: exact boundary values match.
            BiomeDefinition b = NewBiome(minT: 0.3f, maxT: 0.7f, minM: 0.2f, maxM: 0.8f);
            Assert.That(b.Matches(0.3f, 0.2f), Is.True);
            Assert.That(b.Matches(0.7f, 0.8f), Is.True);
        }

        [Test]
        public void Matches_WaterBiome_AlwaysReturnsFalse()
        {
            // Water biomes are resolved by topology, not by envelope —
            // Matches() must reject every climate query for them.
            BiomeDefinition ocean = NewBiome(
                name: "Ocean", isWater: true, isCoastalWater: false);
            BiomeDefinition coast = NewBiome(
                name: "Coast", isWater: true, isCoastalWater: true);
            Assert.That(ocean.Matches(0.5f, 0.5f), Is.False);
            Assert.That(coast.Matches(0.5f, 0.5f), Is.False);
        }

        [Test]
        public void Flags_OceanVsCoast_Distinguishable()
        {
            BiomeDefinition ocean = NewBiome(isWater: true, isCoastalWater: false);
            BiomeDefinition coast = NewBiome(isWater: true, isCoastalWater: true);
            BiomeDefinition land = NewBiome(isWater: false, isCoastalWater: false);

            Assert.That(ocean.IsWater && !ocean.IsCoastalWater, Is.True);
            Assert.That(coast.IsWater && coast.IsCoastalWater, Is.True);
            Assert.That(land.IsWater, Is.False);
        }
    }
}
