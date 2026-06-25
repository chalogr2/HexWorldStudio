using HexWorldStudio.Content;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

namespace HexWorldStudio.Tests
{
    /// <summary>
    /// EditMode tests for <see cref="ResourceDefinition"/> — defaults
    /// produce a permissive (any-biome / any-feature / any-elevation)
    /// resource, and <c>MatchesEnvironment</c> applies each filter
    /// independently.
    /// </summary>
    public class ResourceDefinitionTests
    {
        [Test]
        public void Defaults_AreFullyPermissive()
        {
            ResourceDefinition r = ScriptableObject.CreateInstance<ResourceDefinition>();
            try
            {
                Assert.That(r.AllowedBiomes, Is.Null.Or.Empty);
                Assert.That(r.AllowedFeatures, Is.Null.Or.Empty);
                Assert.That(r.MinElevationLevel, Is.EqualTo(0));
                Assert.That(r.MaxElevationLevel, Is.EqualTo(5));
                Assert.That(r.Rarity, Is.EqualTo(1f));
                Assert.That(r.MinimumSpacing, Is.EqualTo(2));

                // A default resource should match any tile.
                Assert.That(r.MatchesEnvironment(null, null, elevationLevel: 0), Is.True);
                Assert.That(r.MatchesEnvironment(null, null, elevationLevel: 5), Is.True);
            }
            finally { Object.DestroyImmediate(r); }
        }

        [Test]
        public void MatchesEnvironment_ElevationRange_Enforced()
        {
            ResourceDefinition r = ScriptableObject.CreateInstance<ResourceDefinition>();
            try
            {
                r.MinElevationLevel = 2;
                r.MaxElevationLevel = 4;

                Assert.That(r.MatchesEnvironment(null, null, 1), Is.False, "Below MinElevationLevel must fail.");
                Assert.That(r.MatchesEnvironment(null, null, 2), Is.True,  "At MinElevationLevel (inclusive) must pass.");
                Assert.That(r.MatchesEnvironment(null, null, 4), Is.True,  "At MaxElevationLevel (inclusive) must pass.");
                Assert.That(r.MatchesEnvironment(null, null, 5), Is.False, "Above MaxElevationLevel must fail.");
            }
            finally { Object.DestroyImmediate(r); }
        }

        [Test]
        public void MatchesEnvironment_BiomeFilter_OnlyAllowsListedBiomes()
        {
            ResourceDefinition r = ScriptableObject.CreateInstance<ResourceDefinition>();
            BiomeDefinition land = ScriptableObject.CreateInstance<BiomeDefinition>();
            BiomeDefinition other = ScriptableObject.CreateInstance<BiomeDefinition>();
            try
            {
                r.AllowedBiomes = new[] { land };

                Assert.That(r.MatchesEnvironment(land, null, 0), Is.True);
                Assert.That(r.MatchesEnvironment(other, null, 0), Is.False);
                Assert.That(r.MatchesEnvironment(null, null, 0), Is.False,
                    "A null biome can't match a non-empty AllowedBiomes filter.");
            }
            finally
            {
                Object.DestroyImmediate(r);
                Object.DestroyImmediate(land);
                Object.DestroyImmediate(other);
            }
        }

        [Test]
        public void MatchesEnvironment_FeatureFilter_OnlyAllowsListedFeatures()
        {
            ResourceDefinition r = ScriptableObject.CreateInstance<ResourceDefinition>();
            TileFeatureDefinition forest = ScriptableObject.CreateInstance<TileFeatureDefinition>();
            TileFeatureDefinition grass = ScriptableObject.CreateInstance<TileFeatureDefinition>();
            try
            {
                r.AllowedFeatures = new[] { forest };

                Assert.That(r.MatchesEnvironment(null, forest, 0), Is.True);
                Assert.That(r.MatchesEnvironment(null, grass, 0), Is.False);
                Assert.That(r.MatchesEnvironment(null, null, 0), Is.False,
                    "A null feature can't match a non-empty AllowedFeatures filter.");
            }
            finally
            {
                Object.DestroyImmediate(r);
                Object.DestroyImmediate(forest);
                Object.DestroyImmediate(grass);
            }
        }
    }
}
