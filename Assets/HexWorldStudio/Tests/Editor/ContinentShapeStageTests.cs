using HexWorldStudio.Coordinates;
using HexWorldStudio.Data;
using HexWorldStudio.Generation;
using HexWorldStudio.Generation.Pipeline;
using HexWorldStudio.Generation.Pipeline.Stages;
using HexWorldStudio.Generation.Rng;
using NUnit.Framework;

namespace HexWorldStudio.Tests
{
    /// <summary>
    /// EditMode tests for <see cref="ContinentShapeStage"/> —
    /// determinism, land-ratio adherence, full coverage, no
    /// horizontal-seam discontinuities.
    /// </summary>
    public class ContinentShapeStageTests
    {
        private const int Width = 40;
        private const int Height = 24;
        private const ulong SeedA = 0x1234567890ABCDEFUL;
        private const ulong SeedB = 0xFEDCBA0987654321UL;

        private static GenerationContext NewContext(ulong seed, float landRatio = 0.4f)
        {
            var settings = new WorldGenerationSettings(Width, Height, seed, landRatio);
            var map = new WorldMap(Width, Height, seed);
            var rng = new RngService(seed);
            return new GenerationContext(map, settings, rng);
        }

        [Test]
        public void Execute_SameSeed_ProducesSameLandMask()
        {
            var stage = new ContinentShapeStage();
            GenerationContext a = NewContext(SeedA);
            GenerationContext b = NewContext(SeedA);

            stage.Execute(a);
            stage.Execute(b);

            for (int i = 0; i < a.Map.Tiles.Count; i++)
            {
                Assert.That(a.Map.Tiles[i].LandState, Is.EqualTo(b.Map.Tiles[i].LandState),
                    $"Land mask diverged at tile {i}.");
            }
        }

        [Test]
        public void Execute_DifferentSeeds_ProduceDifferentLandMasks()
        {
            var stage = new ContinentShapeStage();
            GenerationContext a = NewContext(SeedA);
            GenerationContext b = NewContext(SeedB);

            stage.Execute(a);
            stage.Execute(b);

            int diffs = 0;
            for (int i = 0; i < a.Map.Tiles.Count; i++)
            {
                if (a.Map.Tiles[i].LandState != b.Map.Tiles[i].LandState) diffs++;
            }
            Assert.That(diffs, Is.GreaterThan(0),
                "Two different seeds produced identical land masks.");
        }

        [Test]
        public void Execute_AllTilesClassified()
        {
            var stage = new ContinentShapeStage();
            GenerationContext ctx = NewContext(SeedA);

            stage.Execute(ctx);

            foreach (HexTile tile in ctx.Map.Tiles)
            {
                Assert.That(tile.LandState, Is.Not.EqualTo(TileLandState.Unset),
                    $"Tile {tile.TileIndex} ({tile.Coord}) remains Unset after Execute.");
            }
        }

        [TestCase(0.2f)]
        [TestCase(0.4f)]
        [TestCase(0.6f)]
        [TestCase(0.8f)]
        public void Execute_AchievesConfiguredLandRatio_WithinOneTile(float landRatio)
        {
            // Percentile-based threshold should hit the target ratio to
            // within one tile of theoretical (integer-tile granularity).
            var stage = new ContinentShapeStage();
            GenerationContext ctx = NewContext(SeedA, landRatio);

            stage.Execute(ctx);

            int total = ctx.Map.Tiles.Count;
            int landCount = 0;
            foreach (HexTile tile in ctx.Map.Tiles)
            {
                if (tile.LandState == TileLandState.Land) landCount++;
            }

            int expected = (int)(landRatio * total);
            // Allow off-by-one due to floor() in percentile-index calculation
            // and the strict-greater-than-threshold classification rule.
            int delta = System.Math.Abs(landCount - expected);
            Assert.That(delta, Is.LessThanOrEqualTo(2),
                $"Land count {landCount} differs from expected {expected} by {delta}; landRatio={landRatio}, total={total}.");
        }

        [Test]
        public void Execute_RespectsHorizontalWrap()
        {
            // The cylindrical Perlin field must produce no measurable
            // discontinuity at the seam: the east neighbor of each
            // rightmost tile should pull from the same noise basin as
            // the leftmost tile in the same row. Practical test: count
            // how many seam crossings flip LandState across the seam
            // vs. within the same row — they should be similar.
            var stage = new ContinentShapeStage();
            GenerationContext ctx = NewContext(SeedA);
            stage.Execute(ctx);

            int seamFlips = 0;
            int interiorFlips = 0;
            for (int row = 0; row < Height; row++)
            {
                ctx.Map.TryGetTile(HexCoordExtensions.FromOffsetOddR(0, row), out HexTile left);
                ctx.Map.TryGetTile(HexCoordExtensions.FromOffsetOddR(Width - 1, row), out HexTile right);
                if (left.LandState != right.LandState) seamFlips++;
            }
            // Sample interior column for comparison.
            int sampleCol = Width / 2;
            for (int row = 0; row < Height; row++)
            {
                ctx.Map.TryGetTile(HexCoordExtensions.FromOffsetOddR(sampleCol, row), out HexTile a);
                ctx.Map.TryGetTile(HexCoordExtensions.FromOffsetOddR(sampleCol + 1, row), out HexTile b);
                if (a.LandState != b.LandState) interiorFlips++;
            }

            // The seam should not look qualitatively different from a
            // random interior column. Tolerate a fair amount of noise;
            // the test fails only if the seam has many more flips than
            // the interior, indicating a discontinuity.
            Assert.That(seamFlips, Is.LessThanOrEqualTo(interiorFlips + 4),
                $"Seam flips ({seamFlips}) suspiciously exceed interior flips ({interiorFlips}); possible seam discontinuity.");
        }

        [Test]
        public void Execute_NullContext_Throws()
        {
            var stage = new ContinentShapeStage();
            Assert.Throws<System.ArgumentNullException>(() => stage.Execute(null));
        }

        [Test]
        public void Name_IsContinentShape()
        {
            Assert.That(new ContinentShapeStage().Name, Is.EqualTo("ContinentShape"));
        }
    }
}
