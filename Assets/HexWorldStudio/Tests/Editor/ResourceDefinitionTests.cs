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

        // ---- ReplaceTile + GetReplacementPrefab (ADR-0023) -------

        [Test]
        public void GetReplacementPrefab_ReplaceTileOff_AlwaysReturnsNull()
        {
            ResourceDefinition r = ScriptableObject.CreateInstance<ResourceDefinition>();
            BiomeDefinition biome = ScriptableObject.CreateInstance<BiomeDefinition>();
            TileFeatureDefinition feature = ScriptableObject.CreateInstance<TileFeatureDefinition>();
            var prefab = new GameObject("DummyReplacement");
            try
            {
                r.ReplaceTile = false;
                r.ReplacementPrefabs = new[]
                {
                    new ResourceReplacementEntry { Biome = biome, Feature = feature, Prefab = prefab },
                };

                Assert.That(r.GetReplacementPrefab(biome, feature), Is.Null,
                    "Replace-tile lookup must return null when the ReplaceTile flag is off, even when entries exist.");
            }
            finally
            {
                Object.DestroyImmediate(prefab);
                Object.DestroyImmediate(r);
                Object.DestroyImmediate(biome);
                Object.DestroyImmediate(feature);
            }
        }

        [Test]
        public void GetReplacementPrefab_MatchingEntry_ReturnsItsPrefab()
        {
            ResourceDefinition r = ScriptableObject.CreateInstance<ResourceDefinition>();
            BiomeDefinition biomeA = ScriptableObject.CreateInstance<BiomeDefinition>();
            BiomeDefinition biomeB = ScriptableObject.CreateInstance<BiomeDefinition>();
            TileFeatureDefinition feature = ScriptableObject.CreateInstance<TileFeatureDefinition>();
            var prefabA = new GameObject("ReplaceA");
            var prefabB = new GameObject("ReplaceB");
            try
            {
                r.ReplaceTile = true;
                r.ReplacementPrefabs = new[]
                {
                    new ResourceReplacementEntry { Biome = biomeA, Feature = feature, Prefab = prefabA },
                    new ResourceReplacementEntry { Biome = biomeB, Feature = feature, Prefab = prefabB },
                };

                Assert.That(r.GetReplacementPrefab(biomeA, feature), Is.SameAs(prefabA));
                Assert.That(r.GetReplacementPrefab(biomeB, feature), Is.SameAs(prefabB));
            }
            finally
            {
                Object.DestroyImmediate(prefabA);
                Object.DestroyImmediate(prefabB);
                Object.DestroyImmediate(r);
                Object.DestroyImmediate(biomeA);
                Object.DestroyImmediate(biomeB);
                Object.DestroyImmediate(feature);
            }
        }

        [Test]
        public void GetReplacementPrefab_NoMatchingEntry_ReturnsNull()
        {
            ResourceDefinition r = ScriptableObject.CreateInstance<ResourceDefinition>();
            BiomeDefinition biome = ScriptableObject.CreateInstance<BiomeDefinition>();
            BiomeDefinition other = ScriptableObject.CreateInstance<BiomeDefinition>();
            TileFeatureDefinition feature = ScriptableObject.CreateInstance<TileFeatureDefinition>();
            var prefab = new GameObject("ReplaceOnlyForBiome");
            try
            {
                r.ReplaceTile = true;
                r.ReplacementPrefabs = new[]
                {
                    new ResourceReplacementEntry { Biome = biome, Feature = feature, Prefab = prefab },
                };

                Assert.That(r.GetReplacementPrefab(other, feature), Is.Null,
                    "Lookup with a (biome, feature) key not present in the entries must return null so the renderer falls back to the default add-on-top path.");
            }
            finally
            {
                Object.DestroyImmediate(prefab);
                Object.DestroyImmediate(r);
                Object.DestroyImmediate(biome);
                Object.DestroyImmediate(other);
                Object.DestroyImmediate(feature);
            }
        }

        [Test]
        public void GetReplacementPrefab_WildcardEntries_MatchNullKey()
        {
            // Wildcard biome / feature axes are represented by a null
            // reference in the entry key. The renderer queries with
            // tile.Biome / tile.Feature; if those happen to be null
            // (e.g., water tiles have null Feature), the wildcard
            // entry's prefab applies.
            ResourceDefinition r = ScriptableObject.CreateInstance<ResourceDefinition>();
            BiomeDefinition biome = ScriptableObject.CreateInstance<BiomeDefinition>();
            var prefab = new GameObject("Wildcard");
            try
            {
                r.ReplaceTile = true;
                r.ReplacementPrefabs = new[]
                {
                    new ResourceReplacementEntry { Biome = biome, Feature = null, Prefab = prefab },
                };

                Assert.That(r.GetReplacementPrefab(biome, null), Is.SameAs(prefab),
                    "An entry with Feature=null must match a tile whose Feature is null.");
            }
            finally
            {
                Object.DestroyImmediate(prefab);
                Object.DestroyImmediate(r);
                Object.DestroyImmediate(biome);
            }
        }
    }
}
