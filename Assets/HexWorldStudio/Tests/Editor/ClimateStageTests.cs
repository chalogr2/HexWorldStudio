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
    /// EditMode tests for <see cref="ClimateStage"/> — determinism,
    /// range correctness, latitude gradient on temperature, and
    /// prevailing-wind effects on moisture (windward vs leeward
    /// asymmetry, water tiles are saturated).
    /// </summary>
    public class ClimateStageTests
    {
        private const int Width = 32;
        private const int Height = 20;
        private const ulong SeedA = 0xABCDEF0123456789UL;
        private const ulong SeedB = 0x9876543210FEDCBAUL;

        private static GenerationContext NewContext(ulong seed)
        {
            var settings = new WorldGenerationSettings(Width, Height, seed);
            var map = new WorldMap(Width, Height, seed);
            var rng = new RngService(seed);
            // Run the upstream stages so Elevation / LandState exist.
            var ctx = new GenerationContext(map, settings, rng);
            new ContinentShapeStage().Execute(ctx);
            new ElevationFieldStage().Execute(ctx);
            new ElevationLevelQuantizationStage().Execute(ctx);
            return ctx;
        }

        [Test]
        public void Execute_SameSeed_ProducesSameClimate()
        {
            var stage = new ClimateStage();
            GenerationContext a = NewContext(SeedA);
            GenerationContext b = NewContext(SeedA);

            stage.Execute(a);
            stage.Execute(b);

            for (int i = 0; i < a.Map.Tiles.Count; i++)
            {
                Assert.That(a.Map.Tiles[i].Temperature, Is.EqualTo(b.Map.Tiles[i].Temperature),
                    $"Temperature diverged at tile {i}.");
                Assert.That(a.Map.Tiles[i].Moisture, Is.EqualTo(b.Map.Tiles[i].Moisture),
                    $"Moisture diverged at tile {i}.");
            }
        }

        [Test]
        public void Execute_AllTilesHaveNonNaNTemperatureAndMoisture()
        {
            var stage = new ClimateStage();
            GenerationContext ctx = NewContext(SeedA);
            stage.Execute(ctx);

            foreach (HexTile tile in ctx.Map.Tiles)
            {
                Assert.That(float.IsNaN(tile.Temperature), Is.False,
                    $"Tile {tile.TileIndex} Temperature is NaN.");
                Assert.That(float.IsNaN(tile.Moisture), Is.False,
                    $"Tile {tile.TileIndex} Moisture is NaN.");
            }
        }

        [Test]
        public void Execute_TemperatureAndMoisture_InUnitInterval()
        {
            var stage = new ClimateStage();
            GenerationContext ctx = NewContext(SeedA);
            stage.Execute(ctx);

            foreach (HexTile tile in ctx.Map.Tiles)
            {
                Assert.That(tile.Temperature, Is.InRange(0f, 1f),
                    $"Tile {tile.TileIndex} Temperature {tile.Temperature} out of [0, 1].");
                Assert.That(tile.Moisture, Is.InRange(0f, 1f),
                    $"Tile {tile.TileIndex} Moisture {tile.Moisture} out of [0, 1].");
            }
        }

        [Test]
        public void Execute_EquatorRowIsWarmerThanPolarRows()
        {
            var stage = new ClimateStage();
            GenerationContext ctx = NewContext(SeedA);
            stage.Execute(ctx);

            int equatorRow = (Height - 1) / 2;
            double equatorAvg = AverageTemperatureForRow(ctx.Map, equatorRow);
            double topPolarAvg = AverageTemperatureForRow(ctx.Map, 0);
            double bottomPolarAvg = AverageTemperatureForRow(ctx.Map, Height - 1);

            Assert.That(equatorAvg, Is.GreaterThan(topPolarAvg),
                $"Equator row avg ({equatorAvg:F3}) not warmer than top polar row ({topPolarAvg:F3}).");
            Assert.That(equatorAvg, Is.GreaterThan(bottomPolarAvg),
                $"Equator row avg ({equatorAvg:F3}) not warmer than bottom polar row ({bottomPolarAvg:F3}).");
        }

        [Test]
        public void Execute_WaterTiles_HaveMoistureOne()
        {
            var stage = new ClimateStage();
            GenerationContext ctx = NewContext(SeedA);
            stage.Execute(ctx);

            foreach (HexTile tile in ctx.Map.Tiles)
            {
                if (tile.LandState == TileLandState.Water)
                {
                    Assert.That(tile.Moisture, Is.EqualTo(1f),
                        $"Water tile {tile.TileIndex} has moisture {tile.Moisture} instead of 1.0.");
                }
            }
        }

        [Test]
        public void Execute_PrevailingWind_WindwardEastSideIsWetterThanLeewardWestSide()
        {
            // Synthetic setup: build a fresh map with a vertical "mountain
            // wall" spanning the middle column, water on both sides,
            // elevation gradient. After the wind pass (east → west), the
            // east half (windward) should average wetter than the west
            // half (leeward, in the mountain's rain shadow).
            var settings = new WorldGenerationSettings(Width, Height, SeedA);
            var map = new WorldMap(Width, Height, SeedA);
            var rng = new RngService(SeedA);
            var ctx = new GenerationContext(map, settings, rng);

            int wallCol = Width / 2;
            foreach (HexTile tile in map.Tiles)
            {
                (int col, int _row) = tile.Coord.ToOffsetOddR();
                tile.Elevation = (col == wallCol) ? 1f : 0.2f;

                if (col == 0 || col == Width - 1)
                {
                    tile.LandState = TileLandState.Water;
                }
                else
                {
                    tile.LandState = TileLandState.Land;
                }
            }

            new ClimateStage().Execute(ctx);

            // Compare moisture averages on land tiles east of and west of
            // the wall (exclude the wall column itself).
            double eastAvg = AverageLandMoistureInRange(map, wallCol + 1, Width - 2);
            double westAvg = AverageLandMoistureInRange(map, 1, wallCol - 1);

            Assert.That(eastAvg, Is.GreaterThan(westAvg),
                $"Windward (east) average moisture {eastAvg:F3} should exceed leeward (west) average moisture {westAvg:F3}.");
        }

        [Test]
        public void Execute_RespectsHorizontalWrap_NoMassiveSeamDiscontinuity()
        {
            var stage = new ClimateStage();
            GenerationContext ctx = NewContext(SeedA);
            stage.Execute(ctx);

            float maxTempSeamGap = 0f;
            for (int row = 0; row < Height; row++)
            {
                ctx.Map.TryGetTile(HexCoordExtensions.FromOffsetOddR(0, row), out HexTile left);
                ctx.Map.TryGetTile(HexCoordExtensions.FromOffsetOddR(Width - 1, row), out HexTile right);
                float gap = System.Math.Abs(left.Temperature - right.Temperature);
                if (gap > maxTempSeamGap) maxTempSeamGap = gap;
            }

            Assert.That(maxTempSeamGap, Is.LessThan(0.4f),
                $"Temperature seam gap was {maxTempSeamGap}; cylindrical sampling should keep it small.");
        }

        [Test]
        public void Execute_DifferentSeeds_ProduceDifferentClimate()
        {
            var stage = new ClimateStage();
            GenerationContext a = NewContext(SeedA);
            GenerationContext b = NewContext(SeedB);

            stage.Execute(a);
            stage.Execute(b);

            int diffs = 0;
            for (int i = 0; i < a.Map.Tiles.Count; i++)
            {
                if (a.Map.Tiles[i].Moisture != b.Map.Tiles[i].Moisture) diffs++;
            }
            Assert.That(diffs, Is.GreaterThan(0));
        }

        [Test]
        public void Execute_NullContext_Throws()
        {
            Assert.Throws<System.ArgumentNullException>(() => new ClimateStage().Execute(null));
        }

        [Test]
        public void Name_IsClimate()
        {
            Assert.That(new ClimateStage().Name, Is.EqualTo("Climate"));
        }

        // ---------- helpers ----------

        private static double AverageTemperatureForRow(WorldMap map, int row)
        {
            double sum = 0;
            int n = 0;
            for (int col = 0; col < map.Width; col++)
            {
                map.TryGetTile(HexCoordExtensions.FromOffsetOddR(col, row), out HexTile tile);
                sum += tile.Temperature;
                n++;
            }
            return n == 0 ? 0 : sum / n;
        }

        private static double AverageLandMoistureInRange(WorldMap map, int colMinInclusive, int colMaxInclusive)
        {
            double sum = 0;
            int n = 0;
            for (int row = 0; row < map.Height; row++)
            {
                for (int col = colMinInclusive; col <= colMaxInclusive; col++)
                {
                    map.TryGetTile(HexCoordExtensions.FromOffsetOddR(col, row), out HexTile tile);
                    if (tile.LandState == TileLandState.Land)
                    {
                        sum += tile.Moisture;
                        n++;
                    }
                }
            }
            return n == 0 ? 0 : sum / n;
        }
    }
}
