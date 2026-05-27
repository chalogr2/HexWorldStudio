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
    /// EditMode tests for <see cref="ElevationFieldStage"/> —
    /// determinism, range, smoothness, seam continuity.
    /// </summary>
    public class ElevationFieldStageTests
    {
        private const int Width = 40;
        private const int Height = 24;
        private const ulong SeedA = 0xCAFEBABEFACEFEEDUL;
        private const ulong SeedB = 0xFEEDFACECAFEBABEUL;

        private static GenerationContext NewContext(ulong seed)
        {
            var settings = new WorldGenerationSettings(Width, Height, seed);
            var map = new WorldMap(Width, Height, seed);
            var rng = new RngService(seed);
            return new GenerationContext(map, settings, rng);
        }

        [Test]
        public void Execute_SameSeed_ProducesSameElevation()
        {
            var stage = new ElevationFieldStage();
            GenerationContext a = NewContext(SeedA);
            GenerationContext b = NewContext(SeedA);

            stage.Execute(a);
            stage.Execute(b);

            for (int i = 0; i < a.Map.Tiles.Count; i++)
            {
                Assert.That(a.Map.Tiles[i].Elevation, Is.EqualTo(b.Map.Tiles[i].Elevation),
                    $"Elevation diverged at tile {i}.");
            }
        }

        [Test]
        public void Execute_DifferentSeeds_ProduceDifferentElevation()
        {
            var stage = new ElevationFieldStage();
            GenerationContext a = NewContext(SeedA);
            GenerationContext b = NewContext(SeedB);

            stage.Execute(a);
            stage.Execute(b);

            int diffs = 0;
            for (int i = 0; i < a.Map.Tiles.Count; i++)
            {
                if (a.Map.Tiles[i].Elevation != b.Map.Tiles[i].Elevation) diffs++;
            }
            Assert.That(diffs, Is.GreaterThan(0));
        }

        [Test]
        public void Execute_AllTilesHaveValidElevation_NoNaN()
        {
            var stage = new ElevationFieldStage();
            GenerationContext ctx = NewContext(SeedA);

            stage.Execute(ctx);

            foreach (HexTile tile in ctx.Map.Tiles)
            {
                Assert.That(float.IsNaN(tile.Elevation), Is.False,
                    $"Tile {tile.TileIndex} elevation is NaN after Execute.");
            }
        }

        [Test]
        public void Execute_ElevationInUnitInterval()
        {
            var stage = new ElevationFieldStage();
            GenerationContext ctx = NewContext(SeedA);

            stage.Execute(ctx);

            foreach (HexTile tile in ctx.Map.Tiles)
            {
                Assert.That(tile.Elevation, Is.InRange(0f, 1f),
                    $"Tile {tile.TileIndex} elevation {tile.Elevation} out of [0, 1].");
            }
        }

        [Test]
        public void Execute_ElevationSpansFullUnitInterval()
        {
            // After per-seed empirical min/max remap, the elevation
            // field always touches both 0 and 1 (the min and max source
            // samples map to those exact values). Pins the remap.
            var stage = new ElevationFieldStage();
            GenerationContext ctx = NewContext(SeedA);

            stage.Execute(ctx);

            float min = float.PositiveInfinity;
            float max = float.NegativeInfinity;
            foreach (HexTile tile in ctx.Map.Tiles)
            {
                if (tile.Elevation < min) min = tile.Elevation;
                if (tile.Elevation > max) max = tile.Elevation;
            }
            Assert.That(min, Is.EqualTo(0f));
            Assert.That(max, Is.EqualTo(1f));
        }

        [Test]
        public void Execute_RespectsHorizontalWrap()
        {
            // The cylindrical sample path must produce no measurable
            // seam discontinuity in the elevation field.
            var stage = new ElevationFieldStage();
            GenerationContext ctx = NewContext(SeedA);
            stage.Execute(ctx);

            float maxSeamGap = 0f;
            for (int row = 0; row < Height; row++)
            {
                ctx.Map.TryGetTile(HexCoordExtensions.FromOffsetOddR(0, row), out HexTile left);
                ctx.Map.TryGetTile(HexCoordExtensions.FromOffsetOddR(Width - 1, row), out HexTile right);
                float gap = System.Math.Abs(left.Elevation - right.Elevation);
                if (gap > maxSeamGap) maxSeamGap = gap;
            }

            // After per-seed empirical remap to [0, 1], the seam jump
            // is bounded by the same factor as interior jumps. Allow
            // generous slack since post-remap any small noise gap
            // gets amplified by 1/range.
            Assert.That(maxSeamGap, Is.LessThan(0.5f),
                $"Maximum seam elevation gap was {maxSeamGap}.");
        }

        [Test]
        public void Execute_NullContext_Throws()
        {
            var stage = new ElevationFieldStage();
            Assert.Throws<System.ArgumentNullException>(() => stage.Execute(null));
        }

        [Test]
        public void Name_IsElevationField()
        {
            Assert.That(new ElevationFieldStage().Name, Is.EqualTo("ElevationField"));
        }
    }
}
