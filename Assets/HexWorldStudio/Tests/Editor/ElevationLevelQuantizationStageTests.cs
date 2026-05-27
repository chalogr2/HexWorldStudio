using HexWorldStudio.Data;
using HexWorldStudio.Generation;
using HexWorldStudio.Generation.Pipeline;
using HexWorldStudio.Generation.Pipeline.Stages;
using HexWorldStudio.Generation.Rng;
using NUnit.Framework;

namespace HexWorldStudio.Tests
{
    /// <summary>
    /// EditMode tests for <see cref="ElevationLevelQuantizationStage"/> —
    /// range, no-unset, deterministic given same input, smoothing
    /// behavior on synthetic outliers.
    /// </summary>
    public class ElevationLevelQuantizationStageTests
    {
        private const int Width = 12;
        private const int Height = 8;
        private const ulong SampleSeed = 0xFADE0FF0FADE0FF0UL;

        private static GenerationContext NewContext()
        {
            var settings = new WorldGenerationSettings(Width, Height, SampleSeed);
            var map = new WorldMap(Width, Height, SampleSeed);
            var rng = new RngService(SampleSeed);
            return new GenerationContext(map, settings, rng);
        }

        private static void FillElevation(WorldMap map, float value)
        {
            foreach (HexTile tile in map.Tiles)
            {
                tile.Elevation = value;
            }
        }

        [Test]
        public void Execute_LevelsInRange()
        {
            GenerationContext ctx = NewContext();
            new ElevationFieldStage().Execute(ctx);
            new ElevationLevelQuantizationStage().Execute(ctx);

            foreach (HexTile tile in ctx.Map.Tiles)
            {
                Assert.That(tile.ElevationLevel, Is.InRange(0, ElevationLevelQuantizationStage.DefaultLevelCount - 1),
                    $"Tile {tile.TileIndex} has out-of-range level {tile.ElevationLevel}.");
            }
        }

        [Test]
        public void Execute_NoUnsetRemains()
        {
            GenerationContext ctx = NewContext();
            new ElevationFieldStage().Execute(ctx);
            new ElevationLevelQuantizationStage().Execute(ctx);

            foreach (HexTile tile in ctx.Map.Tiles)
            {
                Assert.That(tile.ElevationLevel, Is.GreaterThanOrEqualTo(0),
                    $"Tile {tile.TileIndex} still carries the -1 unset sentinel.");
            }
        }

        [Test]
        public void Execute_DeterministicGivenSameElevation()
        {
            GenerationContext a = NewContext();
            GenerationContext b = NewContext();

            new ElevationFieldStage().Execute(a);
            new ElevationFieldStage().Execute(b);

            new ElevationLevelQuantizationStage().Execute(a);
            new ElevationLevelQuantizationStage().Execute(b);

            for (int i = 0; i < a.Map.Tiles.Count; i++)
            {
                Assert.That(a.Map.Tiles[i].ElevationLevel,
                    Is.EqualTo(b.Map.Tiles[i].ElevationLevel),
                    $"Quantization diverged at tile {i}.");
            }
        }

        [Test]
        public void Execute_UniformElevation_AllSameLevel()
        {
            GenerationContext ctx = NewContext();
            FillElevation(ctx.Map, 0.5f); // mid-band, should bucket to level 3 (floor(0.5*6))

            new ElevationLevelQuantizationStage().Execute(ctx);

            foreach (HexTile tile in ctx.Map.Tiles)
            {
                Assert.That(tile.ElevationLevel, Is.EqualTo(3));
            }
        }

        [Test]
        public void Execute_LowestElevation_BucketsToLevelZero()
        {
            GenerationContext ctx = NewContext();
            FillElevation(ctx.Map, 0f);

            new ElevationLevelQuantizationStage().Execute(ctx);

            foreach (HexTile tile in ctx.Map.Tiles)
            {
                Assert.That(tile.ElevationLevel, Is.EqualTo(0));
            }
        }

        [Test]
        public void Execute_HighestElevation_BucketsToMaxLevel()
        {
            GenerationContext ctx = NewContext();
            FillElevation(ctx.Map, 1f);

            new ElevationLevelQuantizationStage().Execute(ctx);

            int maxLevel = ElevationLevelQuantizationStage.DefaultLevelCount - 1;
            foreach (HexTile tile in ctx.Map.Tiles)
            {
                Assert.That(tile.ElevationLevel, Is.EqualTo(maxLevel));
            }
        }

        [Test]
        public void Execute_IsolatedOutlier_SmoothsTowardNeighbors()
        {
            // Construct a synthetic field where one tile sits at the
            // top of the range while every neighbor sits at the
            // bottom. After quantization + smoothing, the outlier
            // should have been pulled one step toward its neighbors.
            GenerationContext ctx = NewContext();
            FillElevation(ctx.Map, 0f);

            // Pick a tile away from the seam so its neighbors are
            // all in-bounds without wrap subtlety.
            const int outlierIndex = 30;
            ctx.Map.Tiles[outlierIndex].Elevation = 1f;

            new ElevationLevelQuantizationStage().Execute(ctx);

            int outlierLevel = ctx.Map.Tiles[outlierIndex].ElevationLevel;
            int maxLevel = ElevationLevelQuantizationStage.DefaultLevelCount - 1;
            Assert.That(outlierLevel, Is.EqualTo(maxLevel - 1),
                $"Expected outlier to be pulled down to {maxLevel - 1}; got {outlierLevel}.");
        }

        [Test]
        public void Execute_GentleGradient_PreservesMonotonicity()
        {
            // A monotonically increasing elevation field should produce
            // a monotonically non-decreasing level field after
            // quantization with smoothing.
            GenerationContext ctx = NewContext();
            for (int i = 0; i < ctx.Map.Tiles.Count; i++)
            {
                ctx.Map.Tiles[i].Elevation = (float)i / (ctx.Map.Tiles.Count - 1);
            }

            new ElevationLevelQuantizationStage().Execute(ctx);

            // Strict monotonicity is too strong (smoothing may flip a
            // few). Check overall ordering: the first half should not
            // have higher max level than the second half.
            int firstHalfMax = 0;
            int secondHalfMin = int.MaxValue;
            int half = ctx.Map.Tiles.Count / 2;
            for (int i = 0; i < half; i++)
            {
                if (ctx.Map.Tiles[i].ElevationLevel > firstHalfMax)
                    firstHalfMax = ctx.Map.Tiles[i].ElevationLevel;
            }
            for (int i = half; i < ctx.Map.Tiles.Count; i++)
            {
                if (ctx.Map.Tiles[i].ElevationLevel < secondHalfMin)
                    secondHalfMin = ctx.Map.Tiles[i].ElevationLevel;
            }
            Assert.That(firstHalfMax, Is.LessThanOrEqualTo(secondHalfMin + 1),
                $"Monotonic gradient broken: first half max {firstHalfMax} > second half min {secondHalfMin}.");
        }

        [Test]
        public void Constructor_LevelCountBelowTwo_Throws()
        {
            Assert.Throws<System.ArgumentOutOfRangeException>(
                () => new ElevationLevelQuantizationStage(1));
            Assert.Throws<System.ArgumentOutOfRangeException>(
                () => new ElevationLevelQuantizationStage(0));
            Assert.Throws<System.ArgumentOutOfRangeException>(
                () => new ElevationLevelQuantizationStage(-3));
        }

        [Test]
        public void Execute_NullContext_Throws()
        {
            Assert.Throws<System.ArgumentNullException>(
                () => new ElevationLevelQuantizationStage().Execute(null));
        }

        [Test]
        public void Name_IsElevationLevelQuantization()
        {
            Assert.That(new ElevationLevelQuantizationStage().Name,
                Is.EqualTo("ElevationLevelQuantization"));
        }
    }
}
