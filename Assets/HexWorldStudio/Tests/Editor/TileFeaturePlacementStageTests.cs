using System;
using System.Collections.Generic;
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
    /// EditMode tests for <see cref="TileFeaturePlacementStage"/> —
    /// every land tile gets a feature respecting its biome's allow
    /// list, water tiles stay null, elevation weighting favors the
    /// preferred-level feature, same seed produces same assignment.
    /// </summary>
    public class TileFeaturePlacementStageTests
    {
        private const int Width = 16;
        private const int Height = 10;
        private const ulong SampleSeed = 0x42424242C0FFEEABUL;

        // ---- factories ----

        private static TileFeatureDefinition NewFeature(string name, int preferredLevel, float weight)
        {
            var f = ScriptableObject.CreateInstance<TileFeatureDefinition>();
            f.FeatureName = name;
            f.PreferredElevationLevel = preferredLevel;
            f.Weight = weight;
            return f;
        }

        private static BiomeDefinition NewBiome(string name, TileFeatureDefinition[] allowed,
            bool isWater = false, bool isCoastalWater = false)
        {
            var b = ScriptableObject.CreateInstance<BiomeDefinition>();
            b.BiomeName = name;
            b.IsWater = isWater;
            b.IsCoastalWater = isCoastalWater;
            b.AllowedFeatures = allowed;
            b.MinTemperature = 0f;
            b.MaxTemperature = 1f;
            b.MinMoisture = 0f;
            b.MaxMoisture = 1f;
            return b;
        }

        private static GenerationContext NewSyntheticContext(
            BiomeDefinition landBiome, BiomeDefinition oceanBiome, BiomeDefinition coastBiome,
            int elevationLevel)
        {
            // Build a tiny synthetic context manually: every tile is
            // land, all assigned the same biome and ElevationLevel.
            var biomes = new[] { oceanBiome, coastBiome, landBiome };
            var settings = new WorldGenerationSettings(Width, Height, SampleSeed,
                WorldGenerationSettings.DefaultLandRatio, biomes);
            var map = new WorldMap(Width, Height, SampleSeed);
            foreach (HexTile t in map.Tiles)
            {
                t.LandState = TileLandState.Land;
                t.Elevation = elevationLevel / 5f;
                t.ElevationLevel = elevationLevel;
                t.Temperature = 0.5f;
                t.Moisture = 0.5f;
                t.Biome = landBiome;
            }
            var rng = new RngService(SampleSeed);
            return new GenerationContext(map, settings, rng);
        }

        private static List<TileFeatureDefinition> AllUsed;
        private static void DestroyAll(params Object[] objs)
        {
            foreach (Object o in objs)
            {
                if (o != null) Object.DestroyImmediate(o);
            }
        }

        // ---- tests ----

        [Test]
        public void Execute_NullContext_Throws()
        {
            Assert.Throws<ArgumentNullException>(
                () => new TileFeaturePlacementStage().Execute(null));
        }

        [Test]
        public void Name_IsTileFeaturePlacement()
        {
            Assert.That(new TileFeaturePlacementStage().Name,
                Is.EqualTo("TileFeaturePlacement"));
        }

        [Test]
        public void Execute_LandTiles_AllReceiveAFeatureFromAllowList()
        {
            TileFeatureDefinition flat = NewFeature("Flat", 1, 2f);
            TileFeatureDefinition hill = NewFeature("Hill", 3, 1f);
            TileFeatureDefinition mountain = NewFeature("Mountain", 5, 0.5f);
            BiomeDefinition land = NewBiome("Land", new[] { flat, hill, mountain });
            BiomeDefinition ocean = NewBiome("Ocean", null, isWater: true, isCoastalWater: false);
            BiomeDefinition coast = NewBiome("Coast", null, isWater: true, isCoastalWater: true);
            try
            {
                GenerationContext ctx = NewSyntheticContext(land, ocean, coast, elevationLevel: 1);
                new TileFeaturePlacementStage().Execute(ctx);

                var allowed = new HashSet<TileFeatureDefinition> { flat, hill, mountain };
                foreach (HexTile tile in ctx.Map.Tiles)
                {
                    Assert.That(tile.Feature, Is.Not.Null,
                        $"Tile {tile.TileIndex} got no feature.");
                    Assert.That(allowed.Contains(tile.Feature), Is.True,
                        $"Tile {tile.TileIndex} got feature outside its biome's allow-list: {tile.Feature.FeatureName}.");
                }
            }
            finally
            {
                DestroyAll(flat, hill, mountain, land, ocean, coast);
            }
        }

        [Test]
        public void Execute_WaterTiles_StayNull()
        {
            TileFeatureDefinition flat = NewFeature("Flat", 1, 2f);
            BiomeDefinition land = NewBiome("Land", new[] { flat });
            BiomeDefinition ocean = NewBiome("Ocean", null, isWater: true, isCoastalWater: false);
            BiomeDefinition coast = NewBiome("Coast", null, isWater: true, isCoastalWater: true);
            try
            {
                GenerationContext ctx = NewSyntheticContext(land, ocean, coast, elevationLevel: 0);
                // Override one tile to be water with ocean biome.
                ctx.Map.Tiles[0].LandState = TileLandState.Water;
                ctx.Map.Tiles[0].Biome = ocean;
                ctx.Map.Tiles[1].LandState = TileLandState.Water;
                ctx.Map.Tiles[1].Biome = coast;

                new TileFeaturePlacementStage().Execute(ctx);

                Assert.That(ctx.Map.Tiles[0].Feature, Is.Null,
                    "Ocean tile should remain null after feature placement.");
                Assert.That(ctx.Map.Tiles[1].Feature, Is.Null,
                    "Coast tile should remain null after feature placement.");
            }
            finally
            {
                DestroyAll(flat, land, ocean, coast);
            }
        }

        [Test]
        public void Execute_HighElevation_StronglyFavorsMountain()
        {
            // With ElevationLevel=5 and Mountain at PreferredLevel=5,
            // Mountain should dominate the distribution (>=60% of tiles)
            // even with a low base weight (0.5) because the falloff
            // crushes Hill (level 3, dist 2) and Flat (level 1, dist 4).
            TileFeatureDefinition flat = NewFeature("Flat", 1, 2f);
            TileFeatureDefinition hill = NewFeature("Hill", 3, 1f);
            TileFeatureDefinition mountain = NewFeature("Mountain", 5, 0.5f);
            BiomeDefinition land = NewBiome("Land", new[] { flat, hill, mountain });
            BiomeDefinition ocean = NewBiome("Ocean", null, isWater: true, isCoastalWater: false);
            BiomeDefinition coast = NewBiome("Coast", null, isWater: true, isCoastalWater: true);
            try
            {
                GenerationContext ctx = NewSyntheticContext(land, ocean, coast, elevationLevel: 5);
                new TileFeaturePlacementStage().Execute(ctx);

                int mountainCount = 0;
                foreach (HexTile tile in ctx.Map.Tiles)
                {
                    if (tile.Feature == mountain) mountainCount++;
                }
                int total = ctx.Map.Tiles.Count;
                double ratio = (double)mountainCount / total;
                Assert.That(ratio, Is.GreaterThan(0.6),
                    $"At ElevationLevel=5, Mountain should dominate; got {mountainCount}/{total} = {ratio:F2}.");
            }
            finally
            {
                DestroyAll(flat, hill, mountain, land, ocean, coast);
            }
        }

        [Test]
        public void Execute_LowElevation_StronglyFavorsFlat()
        {
            TileFeatureDefinition flat = NewFeature("Flat", 1, 2f);
            TileFeatureDefinition hill = NewFeature("Hill", 3, 1f);
            TileFeatureDefinition mountain = NewFeature("Mountain", 5, 0.5f);
            BiomeDefinition land = NewBiome("Land", new[] { flat, hill, mountain });
            BiomeDefinition ocean = NewBiome("Ocean", null, isWater: true, isCoastalWater: false);
            BiomeDefinition coast = NewBiome("Coast", null, isWater: true, isCoastalWater: true);
            try
            {
                GenerationContext ctx = NewSyntheticContext(land, ocean, coast, elevationLevel: 1);
                new TileFeaturePlacementStage().Execute(ctx);

                int flatCount = 0;
                foreach (HexTile tile in ctx.Map.Tiles)
                {
                    if (tile.Feature == flat) flatCount++;
                }
                int total = ctx.Map.Tiles.Count;
                double ratio = (double)flatCount / total;
                Assert.That(ratio, Is.GreaterThan(0.6),
                    $"At ElevationLevel=1, Flat should dominate; got {flatCount}/{total} = {ratio:F2}.");
            }
            finally
            {
                DestroyAll(flat, hill, mountain, land, ocean, coast);
            }
        }

        [Test]
        public void Execute_SameSeed_ProducesSameFeatureAssignment()
        {
            TileFeatureDefinition flat = NewFeature("Flat", 1, 2f);
            TileFeatureDefinition hill = NewFeature("Hill", 3, 1f);
            BiomeDefinition land = NewBiome("Land", new[] { flat, hill });
            BiomeDefinition ocean = NewBiome("Ocean", null, isWater: true, isCoastalWater: false);
            BiomeDefinition coast = NewBiome("Coast", null, isWater: true, isCoastalWater: true);
            try
            {
                GenerationContext a = NewSyntheticContext(land, ocean, coast, elevationLevel: 2);
                GenerationContext b = NewSyntheticContext(land, ocean, coast, elevationLevel: 2);

                new TileFeaturePlacementStage().Execute(a);
                new TileFeaturePlacementStage().Execute(b);

                for (int i = 0; i < a.Map.Tiles.Count; i++)
                {
                    Assert.That(a.Map.Tiles[i].Feature, Is.SameAs(b.Map.Tiles[i].Feature),
                        $"Feature assignment diverged at tile {i}.");
                }
            }
            finally
            {
                DestroyAll(flat, hill, land, ocean, coast);
            }
        }

        [Test]
        public void Execute_BiomeWithEmptyAllowedFeatures_LeavesFeatureNull()
        {
            // A buyer-authored biome with no allowed features should
            // not crash and should leave the tile feature null.
            BiomeDefinition emptyAllowList = NewBiome("EmptyAllowList", new TileFeatureDefinition[0]);
            BiomeDefinition ocean = NewBiome("Ocean", null, isWater: true, isCoastalWater: false);
            BiomeDefinition coast = NewBiome("Coast", null, isWater: true, isCoastalWater: true);
            try
            {
                GenerationContext ctx = NewSyntheticContext(emptyAllowList, ocean, coast, elevationLevel: 2);
                new TileFeaturePlacementStage().Execute(ctx);

                foreach (HexTile tile in ctx.Map.Tiles)
                {
                    Assert.That(tile.Feature, Is.Null,
                        "Land tile in a biome with empty AllowedFeatures should remain null.");
                }
            }
            finally
            {
                DestroyAll(emptyAllowList, ocean, coast);
            }
        }
    }
}
