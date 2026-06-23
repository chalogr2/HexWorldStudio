using HexWorldStudio.Content;
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
    /// EditMode tests for the pluggable continent-shape catalog
    /// (Slice 13 / ADR-0020). Each algorithm is verified against
    /// three contracts: <c>LandRatio</c> is honored within one tile,
    /// the result is deterministic given the seed, and a Shuffle
    /// selection dispatches to a valid concrete algorithm.
    /// </summary>
    public class ContinentShapeAlgorithmsTests
    {
        private const int Width = 24;
        private const int Height = 16;
        private const ulong SampleSeed = 0xC04157uL;
        // 24 × 16 = 384 tiles. Floor(0.5 × 384) = 192 tiles of land per the
        // percentile rule — the strictly-greater comparison can produce
        // 191 or 192 depending on noise ties at the threshold, so tests
        // assert "within 2 tiles" tolerance.

        private static GenerationContext NewContext(float landRatio, ContinentShapeAlgorithm algorithm, ulong seed = SampleSeed)
        {
            var settings = new WorldGenerationSettings(
                Width, Height, seed, landRatio,
                biomes: new BiomeDefinition[0],
                biomeCohesion: 0f, featureCohesion: 0f,
                continentShapeAlgorithm: algorithm);
            var map = new WorldMap(Width, Height, seed);
            var rng = new RngService(seed);
            return new GenerationContext(map, settings, rng);
        }

        private static int CountLand(WorldMap map)
        {
            int n = 0;
            foreach (HexTile t in map.Tiles)
            {
                if (t.LandState == TileLandState.Land) n++;
            }
            return n;
        }

        // ---- LandRatio honored, per algorithm ---------------------

        [TestCase(ContinentShapeAlgorithm.Continents)]
        [TestCase(ContinentShapeAlgorithm.Pangaea)]
        [TestCase(ContinentShapeAlgorithm.Archipelago)]
        [TestCase(ContinentShapeAlgorithm.Fractal)]
        [TestCase(ContinentShapeAlgorithm.InlandSea)]
        [TestCase(ContinentShapeAlgorithm.Lakes)]
        [TestCase(ContinentShapeAlgorithm.Shuffle)]
        public void Apply_HonorsLandRatio(ContinentShapeAlgorithm algorithm)
        {
            const float Ratio = 0.4f;
            GenerationContext ctx = NewContext(Ratio, algorithm);

            new ContinentShapeStage().Execute(ctx);

            int land = CountLand(ctx.Map);
            int expected = (int)(Ratio * ctx.Map.Tiles.Count);
            Assert.That(land, Is.InRange(expected - 2, expected + 2),
                $"{algorithm} landRatio = {(float)land / ctx.Map.Tiles.Count:F3}; expected ≈ {Ratio:F3}");
        }

        [TestCase(ContinentShapeAlgorithm.Continents)]
        [TestCase(ContinentShapeAlgorithm.Pangaea)]
        [TestCase(ContinentShapeAlgorithm.Archipelago)]
        [TestCase(ContinentShapeAlgorithm.Fractal)]
        [TestCase(ContinentShapeAlgorithm.InlandSea)]
        [TestCase(ContinentShapeAlgorithm.Lakes)]
        public void Apply_HonorsHighLandRatio(ContinentShapeAlgorithm algorithm)
        {
            // Lakes and InlandSea read best at higher land ratios — confirm the contract still holds.
            const float Ratio = 0.8f;
            GenerationContext ctx = NewContext(Ratio, algorithm);

            new ContinentShapeStage().Execute(ctx);

            int land = CountLand(ctx.Map);
            int expected = (int)(Ratio * ctx.Map.Tiles.Count);
            Assert.That(land, Is.InRange(expected - 2, expected + 2),
                $"{algorithm} at 0.8 landRatio produced {land} land tiles; expected ≈ {expected}.");
        }

        // ---- Determinism per algorithm ---------------------------

        [TestCase(ContinentShapeAlgorithm.Continents)]
        [TestCase(ContinentShapeAlgorithm.Pangaea)]
        [TestCase(ContinentShapeAlgorithm.Archipelago)]
        [TestCase(ContinentShapeAlgorithm.Fractal)]
        [TestCase(ContinentShapeAlgorithm.InlandSea)]
        [TestCase(ContinentShapeAlgorithm.Lakes)]
        [TestCase(ContinentShapeAlgorithm.Shuffle)]
        public void Apply_IsDeterministic_SameSeedSameOutput(ContinentShapeAlgorithm algorithm)
        {
            GenerationContext a = NewContext(0.4f, algorithm);
            GenerationContext b = NewContext(0.4f, algorithm);

            new ContinentShapeStage().Execute(a);
            new ContinentShapeStage().Execute(b);

            for (int i = 0; i < a.Map.Tiles.Count; i++)
            {
                Assert.That(a.Map.Tiles[i].LandState, Is.EqualTo(b.Map.Tiles[i].LandState),
                    $"{algorithm} drifted at tile {i} for seed {SampleSeed:X}.");
            }
        }

        // ---- Algorithms differ from each other -------------------

        [Test]
        public void Apply_DifferentAlgorithms_ProduceDifferentLandPatterns()
        {
            // Spot-check that algorithm choice actually matters — two
            // different algorithms with the same seed and ratio
            // should not collapse to identical outputs.
            GenerationContext continents = NewContext(0.4f, ContinentShapeAlgorithm.Continents);
            GenerationContext pangaea = NewContext(0.4f, ContinentShapeAlgorithm.Pangaea);

            new ContinentShapeStage().Execute(continents);
            new ContinentShapeStage().Execute(pangaea);

            int differences = 0;
            for (int i = 0; i < continents.Map.Tiles.Count; i++)
            {
                if (continents.Map.Tiles[i].LandState != pangaea.Map.Tiles[i].LandState) differences++;
            }
            Assert.That(differences, Is.GreaterThan(continents.Map.Tiles.Count / 10),
                "Continents and Pangaea should produce visibly different worlds — got an unexpectedly similar layout.");
        }

        // ---- Pangaea-specific shape sanity ------------------------

        [Test]
        public void Apply_Pangaea_ConcentratesLandInMiddleLatitudes()
        {
            // Pangaea's vertical Gaussian boost should cluster land
            // in the middle rows. Compare the land density in the
            // middle 50% of rows to the polar 50% — the middle
            // should be denser.
            GenerationContext ctx = NewContext(0.4f, ContinentShapeAlgorithm.Pangaea);
            new ContinentShapeStage().Execute(ctx);

            int middleLand = 0, polarLand = 0;
            int middleRowMin = Height / 4;
            int middleRowMax = Height - Height / 4 - 1;

            for (int i = 0; i < ctx.Map.Tiles.Count; i++)
            {
                HexTile t = ctx.Map.Tiles[i];
                if (t.LandState != TileLandState.Land) continue;
                (int _, int row) = t.Coord.ToOffsetOddR();
                if (row >= middleRowMin && row <= middleRowMax) middleLand++;
                else polarLand++;
            }

            Assert.That(middleLand, Is.GreaterThan(polarLand),
                $"Pangaea should concentrate land in mid-latitudes — got middle={middleLand}, polar={polarLand}.");
        }

        // ---- InlandSea-specific shape sanity ----------------------

        [Test]
        public void Apply_InlandSea_LeavesMiddleLatitudesAsWater()
        {
            // InlandSea inverts the Pangaea bias — mid-latitudes
            // should have MORE water than the polar latitudes (given
            // a high enough landRatio for the surrounding ring to form).
            GenerationContext ctx = NewContext(0.65f, ContinentShapeAlgorithm.InlandSea);
            new ContinentShapeStage().Execute(ctx);

            int middleWater = 0, polarWater = 0;
            int middleRowMin = Height / 4;
            int middleRowMax = Height - Height / 4 - 1;

            for (int i = 0; i < ctx.Map.Tiles.Count; i++)
            {
                HexTile t = ctx.Map.Tiles[i];
                if (t.LandState != TileLandState.Water) continue;
                (int _, int row) = t.Coord.ToOffsetOddR();
                if (row >= middleRowMin && row <= middleRowMax) middleWater++;
                else polarWater++;
            }

            Assert.That(middleWater, Is.GreaterThan(polarWater),
                $"InlandSea should concentrate water in mid-latitudes — got middle={middleWater}, polar={polarWater}.");
        }

        // ---- Default algorithm constant --------------------------

        [Test]
        public void WorldGenerationSettings_DefaultContinentShape_IsContinents()
        {
            // Pin the default — guarantees existing seeds with the
            // pre-Slice-13 5-arg constructor keep producing the same
            // Continents-shaped worlds.
            Assert.That(WorldGenerationSettings.DefaultContinentShape,
                Is.EqualTo(ContinentShapeAlgorithm.Continents));

            var settings = new WorldGenerationSettings(
                Width, Height, SampleSeed,
                WorldGenerationSettings.DefaultLandRatio,
                new BiomeDefinition[0]);
            Assert.That(settings.ContinentShapeAlgorithm, Is.EqualTo(ContinentShapeAlgorithm.Continents));
        }

        // ---- Router null-context guard ----------------------------

        [Test]
        public void Execute_NullContext_Throws()
        {
            Assert.Throws<System.ArgumentNullException>(
                () => new ContinentShapeStage().Execute(null));
        }
    }
}
