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
    /// EditMode tests for <see cref="BiomeCohesionStage"/> — strength →
    /// (iterations, threshold) mapping, no-op at strength 0, isolated
    /// outlier smoothing, cluster preservation, water-tile preservation,
    /// and determinism.
    /// </summary>
    public class BiomeCohesionStageTests
    {
        private const int Width = 12;
        private const int Height = 8;
        private const ulong SampleSeed = 0xC047E510uL;

        // ---------- FromStrength mapping ----------

        [Test]
        public void FromStrength_Zero_ReturnsSkippedPair()
        {
            (int iters, int k) = BiomeCohesionStage.FromStrength(0f);
            Assert.That(iters, Is.EqualTo(0));
            Assert.That(k, Is.EqualTo(0));
        }

        [Test]
        public void FromStrength_Negative_ReturnsSkippedPair()
        {
            (int iters, int k) = BiomeCohesionStage.FromStrength(-0.25f);
            Assert.That(iters, Is.EqualTo(0));
            Assert.That(k, Is.EqualTo(0));
        }

        [Test]
        public void FromStrength_Half_ReturnsTwoIterationsTwoThreshold()
        {
            (int iters, int k) = BiomeCohesionStage.FromStrength(0.5f);
            Assert.That(iters, Is.EqualTo(2));
            Assert.That(k, Is.EqualTo(2));
        }

        [Test]
        public void FromStrength_One_ReturnsMaxIterations()
        {
            (int iters, int k) = BiomeCohesionStage.FromStrength(1f);
            Assert.That(iters, Is.EqualTo(4));
            Assert.That(k, Is.EqualTo(3));
        }

        [Test]
        public void FromStrength_AboveOne_ClampedToMax()
        {
            (int iters, int k) = BiomeCohesionStage.FromStrength(99f);
            Assert.That(iters, Is.EqualTo(4));
            Assert.That(k, Is.EqualTo(3));
        }

        [Test]
        public void FromStrength_NaN_ReturnsSkippedPair()
        {
            (int iters, int k) = BiomeCohesionStage.FromStrength(float.NaN);
            Assert.That(iters, Is.EqualTo(0));
            Assert.That(k, Is.EqualTo(0));
        }

        // ---------- Execute behavior ----------

        private static BiomeDefinition NewBiome(string name, bool isWater = false, bool isCoastal = false)
        {
            var b = ScriptableObject.CreateInstance<BiomeDefinition>();
            b.BiomeName = name;
            b.IsWater = isWater;
            b.IsCoastalWater = isCoastal;
            // Envelope values are unused at the cohesion stage — leave defaults.
            return b;
        }

        private static GenerationContext NewContext(IReadOnlyList<BiomeDefinition> biomes, float biomeCohesion)
        {
            var settings = new WorldGenerationSettings(
                Width, Height, SampleSeed, WorldGenerationSettings.DefaultLandRatio,
                biomes, biomeCohesion, 0f);
            var map = new WorldMap(Width, Height, SampleSeed);
            var rng = new RngService(SampleSeed);
            return new GenerationContext(map, settings, rng);
        }

        private static void FillBiome(WorldMap map, BiomeDefinition biome, TileLandState state = TileLandState.Land)
        {
            foreach (HexTile t in map.Tiles)
            {
                t.LandState = state;
                t.Biome = biome;
            }
        }

        [Test]
        public void Execute_StrengthZero_LeavesBiomesUnchanged()
        {
            BiomeDefinition land = NewBiome("Land");
            BiomeDefinition other = NewBiome("Other");
            BiomeDefinition ocean = NewBiome("Ocean", isWater: true);
            BiomeDefinition coast = NewBiome("Coast", isWater: true, isCoastal: true);
            try
            {
                GenerationContext ctx = NewContext(new[] { ocean, coast, land, other }, biomeCohesion: 0f);
                FillBiome(ctx.Map, land);
                // Plant a lone outlier of `other` in a sea of `land`.
                const int outlier = 30;
                ctx.Map.Tiles[outlier].Biome = other;

                new BiomeCohesionStage().Execute(ctx);

                Assert.That(ctx.Map.Tiles[outlier].Biome, Is.SameAs(other),
                    "At strength 0 the cohesion stage must be a complete no-op.");
            }
            finally
            {
                Object.DestroyImmediate(land);
                Object.DestroyImmediate(other);
                Object.DestroyImmediate(ocean);
                Object.DestroyImmediate(coast);
            }
        }

        [Test]
        public void Execute_IsolatedOutlier_SmoothsTowardNeighborMajority()
        {
            BiomeDefinition land = NewBiome("Land");
            BiomeDefinition other = NewBiome("Other");
            BiomeDefinition ocean = NewBiome("Ocean", isWater: true);
            BiomeDefinition coast = NewBiome("Coast", isWater: true, isCoastal: true);
            try
            {
                GenerationContext ctx = NewContext(new[] { ocean, coast, land, other }, biomeCohesion: 1f);
                FillBiome(ctx.Map, land);
                const int outlier = 30;
                ctx.Map.Tiles[outlier].Biome = other;

                new BiomeCohesionStage().Execute(ctx);

                Assert.That(ctx.Map.Tiles[outlier].Biome, Is.SameAs(land),
                    "An isolated single-tile outlier surrounded by 'land' must flip to 'land' under strong smoothing.");
            }
            finally
            {
                Object.DestroyImmediate(land);
                Object.DestroyImmediate(other);
                Object.DestroyImmediate(ocean);
                Object.DestroyImmediate(coast);
            }
        }

        [Test]
        public void Execute_StrongCluster_Preserved()
        {
            BiomeDefinition land = NewBiome("Land");
            BiomeDefinition other = NewBiome("Other");
            BiomeDefinition ocean = NewBiome("Ocean", isWater: true);
            BiomeDefinition coast = NewBiome("Coast", isWater: true, isCoastal: true);
            try
            {
                GenerationContext ctx = NewContext(new[] { ocean, coast, land, other }, biomeCohesion: 0.5f);
                FillBiome(ctx.Map, land);
                // Plant a 3x3 patch of 'other' — strong internal support.
                for (int col = 4; col <= 6; col++)
                {
                    for (int row = 3; row <= 5; row++)
                    {
                        HexCoord coord = HexCoordExtensions.FromOffsetOddR(col, row);
                        if (ctx.Map.TryGetIndex(coord, out int idx))
                        {
                            ctx.Map.Tiles[idx].Biome = other;
                        }
                    }
                }

                new BiomeCohesionStage().Execute(ctx);

                // Center tile of the cluster should survive — every neighbor is 'other'.
                HexCoord centerCoord = HexCoordExtensions.FromOffsetOddR(5, 4);
                Assert.That(ctx.Map.TryGetIndex(centerCoord, out int centerIdx), Is.True);
                Assert.That(ctx.Map.Tiles[centerIdx].Biome, Is.SameAs(other),
                    "A tile fully surrounded by same-biome neighbors must never be smoothed away.");
            }
            finally
            {
                Object.DestroyImmediate(land);
                Object.DestroyImmediate(other);
                Object.DestroyImmediate(ocean);
                Object.DestroyImmediate(coast);
            }
        }

        [Test]
        public void Execute_WaterTiles_NeverModified()
        {
            BiomeDefinition land = NewBiome("Land");
            BiomeDefinition ocean = NewBiome("Ocean", isWater: true);
            BiomeDefinition coast = NewBiome("Coast", isWater: true, isCoastal: true);
            try
            {
                GenerationContext ctx = NewContext(new[] { ocean, coast, land }, biomeCohesion: 1f);
                FillBiome(ctx.Map, ocean, TileLandState.Water);
                // Plant one land tile in the middle of a water sea.
                const int land1 = 30;
                ctx.Map.Tiles[land1].LandState = TileLandState.Land;
                ctx.Map.Tiles[land1].Biome = land;

                new BiomeCohesionStage().Execute(ctx);

                int waterUnchanged = 0;
                foreach (HexTile t in ctx.Map.Tiles)
                {
                    if (t.LandState == TileLandState.Water)
                    {
                        Assert.That(t.Biome, Is.SameAs(ocean),
                            "Water tile got rewritten despite the stage's water-preservation rule.");
                        waterUnchanged++;
                    }
                }
                Assert.That(waterUnchanged, Is.EqualTo(ctx.Map.Tiles.Count - 1));
            }
            finally
            {
                Object.DestroyImmediate(land);
                Object.DestroyImmediate(ocean);
                Object.DestroyImmediate(coast);
            }
        }

        [Test]
        public void Execute_Deterministic_TwoRunsProduceIdenticalOutput()
        {
            BiomeDefinition landA = NewBiome("LandA");
            BiomeDefinition landB = NewBiome("LandB");
            BiomeDefinition ocean = NewBiome("Ocean", isWater: true);
            BiomeDefinition coast = NewBiome("Coast", isWater: true, isCoastal: true);
            try
            {
                BiomeDefinition[] roster = { ocean, coast, landA, landB };

                GenerationContext a = NewContext(roster, biomeCohesion: 0.75f);
                GenerationContext b = NewContext(roster, biomeCohesion: 0.75f);

                FillBiome(a.Map, landA);
                FillBiome(b.Map, landA);
                for (int i = 0; i < 20; i++)
                {
                    a.Map.Tiles[i * 3].Biome = landB;
                    b.Map.Tiles[i * 3].Biome = landB;
                }

                new BiomeCohesionStage().Execute(a);
                new BiomeCohesionStage().Execute(b);

                for (int i = 0; i < a.Map.Tiles.Count; i++)
                {
                    Assert.That(a.Map.Tiles[i].Biome, Is.SameAs(b.Map.Tiles[i].Biome),
                        $"Cohesion drifted at tile {i} — identical input must produce identical output.");
                }
            }
            finally
            {
                Object.DestroyImmediate(landA);
                Object.DestroyImmediate(landB);
                Object.DestroyImmediate(ocean);
                Object.DestroyImmediate(coast);
            }
        }

        [Test]
        public void Execute_NullContext_Throws()
        {
            Assert.Throws<System.ArgumentNullException>(
                () => new BiomeCohesionStage().Execute(null));
        }
    }
}
