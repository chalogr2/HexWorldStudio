using HexWorldStudio.Content;
using HexWorldStudio.Coordinates;
using HexWorldStudio.Data;
using HexWorldStudio.Generation;
using HexWorldStudio.Generation.Pipeline;
using HexWorldStudio.Generation.Pipeline.Stages;
using HexWorldStudio.Generation.Rng;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

namespace HexWorldStudio.Tests
{
    /// <summary>
    /// EditMode tests for <see cref="FeatureCohesionStage"/> — no-op at
    /// strength 0, outlier smoothing, refusal to flip outside the
    /// tile's biome's <c>AllowedFeatures</c>, and water-tile
    /// preservation.
    /// </summary>
    public class FeatureCohesionStageTests
    {
        private const int Width = 12;
        private const int Height = 8;
        private const ulong SampleSeed = 0xFEA70CE5uL;

        private static TileFeatureDefinition NewFeature(string name)
        {
            var f = ScriptableObject.CreateInstance<TileFeatureDefinition>();
            f.FeatureName = name;
            return f;
        }

        private static BiomeDefinition NewBiome(string name, TileFeatureDefinition[] allowed, bool isWater = false, bool isCoastal = false)
        {
            var b = ScriptableObject.CreateInstance<BiomeDefinition>();
            b.BiomeName = name;
            b.IsWater = isWater;
            b.IsCoastalWater = isCoastal;
            b.AllowedFeatures = allowed;
            return b;
        }

        private static GenerationContext NewContext(BiomeDefinition[] biomes, float featureCohesion)
        {
            var settings = new WorldGenerationSettings(
                Width, Height, SampleSeed, WorldGenerationSettings.DefaultLandRatio,
                biomes, 0f, featureCohesion);
            var map = new WorldMap(Width, Height, SampleSeed);
            var rng = new RngService(SampleSeed);
            return new GenerationContext(map, settings, rng);
        }

        [Test]
        public void Execute_StrengthZero_LeavesFeaturesUnchanged()
        {
            TileFeatureDefinition grass = NewFeature("Grass");
            TileFeatureDefinition forest = NewFeature("Forest");
            BiomeDefinition land = NewBiome("Land", new[] { grass, forest });
            BiomeDefinition ocean = NewBiome("Ocean", null, isWater: true);
            BiomeDefinition coast = NewBiome("Coast", null, isWater: true, isCoastal: true);
            try
            {
                GenerationContext ctx = NewContext(new[] { ocean, coast, land }, featureCohesion: 0f);
                foreach (HexTile t in ctx.Map.Tiles)
                {
                    t.LandState = TileLandState.Land;
                    t.Biome = land;
                    t.Feature = grass;
                }
                const int outlier = 30;
                ctx.Map.Tiles[outlier].Feature = forest;

                new FeatureCohesionStage().Execute(ctx);

                Assert.That(ctx.Map.Tiles[outlier].Feature, Is.SameAs(forest),
                    "At strength 0 the feature cohesion stage must be a complete no-op.");
            }
            finally
            {
                Object.DestroyImmediate(grass);
                Object.DestroyImmediate(forest);
                Object.DestroyImmediate(land);
                Object.DestroyImmediate(ocean);
                Object.DestroyImmediate(coast);
            }
        }

        [Test]
        public void Execute_IsolatedOutlier_SmoothsTowardNeighborMajority()
        {
            TileFeatureDefinition grass = NewFeature("Grass");
            TileFeatureDefinition forest = NewFeature("Forest");
            BiomeDefinition land = NewBiome("Land", new[] { grass, forest });
            BiomeDefinition ocean = NewBiome("Ocean", null, isWater: true);
            BiomeDefinition coast = NewBiome("Coast", null, isWater: true, isCoastal: true);
            try
            {
                GenerationContext ctx = NewContext(new[] { ocean, coast, land }, featureCohesion: 1f);
                foreach (HexTile t in ctx.Map.Tiles)
                {
                    t.LandState = TileLandState.Land;
                    t.Biome = land;
                    t.Feature = grass;
                }
                const int outlier = 30;
                ctx.Map.Tiles[outlier].Feature = forest;

                new FeatureCohesionStage().Execute(ctx);

                Assert.That(ctx.Map.Tiles[outlier].Feature, Is.SameAs(grass),
                    "An isolated forest outlier surrounded by grassland must flip to grass under strong smoothing.");
            }
            finally
            {
                Object.DestroyImmediate(grass);
                Object.DestroyImmediate(forest);
                Object.DestroyImmediate(land);
                Object.DestroyImmediate(ocean);
                Object.DestroyImmediate(coast);
            }
        }

        [Test]
        public void Execute_NeverFlipsToFeatureNotInBiomeAllowList()
        {
            // 'mushroom' is plopped on a tile whose biome does NOT
            // include mushroom in AllowedFeatures. Neighbor tiles are
            // 'mushroom' (planted by a hostile test harness). The
            // cohesion stage must not flip a 'grass' tile to
            // 'mushroom' because mushroom isn't allowed in that biome
            // — even when the neighbor majority says otherwise.
            TileFeatureDefinition grass = NewFeature("Grass");
            TileFeatureDefinition mushroom = NewFeature("Mushroom");
            // Biome allows ONLY grass; mushroom is intentionally absent from allow-list.
            BiomeDefinition land = NewBiome("Land", new[] { grass });
            BiomeDefinition ocean = NewBiome("Ocean", null, isWater: true);
            BiomeDefinition coast = NewBiome("Coast", null, isWater: true, isCoastal: true);
            try
            {
                GenerationContext ctx = NewContext(new[] { ocean, coast, land }, featureCohesion: 1f);
                foreach (HexTile t in ctx.Map.Tiles)
                {
                    t.LandState = TileLandState.Land;
                    t.Biome = land;
                    t.Feature = mushroom; // hostile: every tile starts with a not-allowed feature
                }
                const int testTile = 30;
                ctx.Map.Tiles[testTile].Feature = grass; // the one tile with a permitted feature

                new FeatureCohesionStage().Execute(ctx);

                Assert.That(ctx.Map.Tiles[testTile].Feature, Is.SameAs(grass),
                    "Cohesion must not switch a tile to a feature outside its biome's AllowedFeatures list, even with overwhelming neighbor majority.");
            }
            finally
            {
                Object.DestroyImmediate(grass);
                Object.DestroyImmediate(mushroom);
                Object.DestroyImmediate(land);
                Object.DestroyImmediate(ocean);
                Object.DestroyImmediate(coast);
            }
        }

        [Test]
        public void Execute_WaterTiles_NeverModified()
        {
            TileFeatureDefinition grass = NewFeature("Grass");
            BiomeDefinition land = NewBiome("Land", new[] { grass });
            BiomeDefinition ocean = NewBiome("Ocean", null, isWater: true);
            BiomeDefinition coast = NewBiome("Coast", null, isWater: true, isCoastal: true);
            try
            {
                GenerationContext ctx = NewContext(new[] { ocean, coast, land }, featureCohesion: 1f);
                foreach (HexTile t in ctx.Map.Tiles)
                {
                    t.LandState = TileLandState.Water;
                    t.Biome = ocean;
                    t.Feature = null;
                }
                // Plant one land tile with a feature in the middle of a water sea.
                const int landIdx = 30;
                ctx.Map.Tiles[landIdx].LandState = TileLandState.Land;
                ctx.Map.Tiles[landIdx].Biome = land;
                ctx.Map.Tiles[landIdx].Feature = grass;

                new FeatureCohesionStage().Execute(ctx);

                foreach (HexTile t in ctx.Map.Tiles)
                {
                    if (t.LandState == TileLandState.Water)
                    {
                        Assert.That(t.Feature, Is.Null,
                            "Water tiles must never receive a feature from cohesion smoothing.");
                    }
                }
            }
            finally
            {
                Object.DestroyImmediate(grass);
                Object.DestroyImmediate(land);
                Object.DestroyImmediate(ocean);
                Object.DestroyImmediate(coast);
            }
        }

        [Test]
        public void Execute_NullContext_Throws()
        {
            Assert.Throws<System.ArgumentNullException>(
                () => new FeatureCohesionStage().Execute(null));
        }
    }
}
