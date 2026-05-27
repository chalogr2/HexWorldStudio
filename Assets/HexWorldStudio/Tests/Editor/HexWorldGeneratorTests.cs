using System;
using HexWorldStudio.Data;
using HexWorldStudio.Generation;
using NUnit.Framework;

namespace HexWorldStudio.Tests
{
    /// <summary>
    /// EditMode tests for <see cref="HexWorldGenerator"/> — entry-point
    /// validation, dimension/seed round-trip, structural determinism.
    /// </summary>
    /// <remarks>
    /// Per-tile determinism (same seed produces the same per-tile
    /// elevation / biome / land state) is exercised in slices that add
    /// the corresponding generation stages. Slice 4's pipeline is
    /// empty, so the generator's only observable output is the
    /// dimensioned, NaN-defaulted <see cref="WorldMap"/> shell.
    /// </remarks>
    public class HexWorldGeneratorTests
    {
        private const int SampleWidth = 24;
        private const int SampleHeight = 16;
        private const ulong SampleSeed = 0x1122334455667788UL;

        [Test]
        public void Generate_NullSettings_Throws()
        {
            var gen = new HexWorldGenerator();
            Assert.Throws<ArgumentNullException>(() => gen.Generate(null));
        }

        [Test]
        public void Generate_ZeroWidth_Throws()
        {
            var gen = new HexWorldGenerator();
            var settings = new WorldGenerationSettings(0, SampleHeight, SampleSeed);
            Assert.Throws<ArgumentOutOfRangeException>(() => gen.Generate(settings));
        }

        [Test]
        public void Generate_NegativeWidth_Throws()
        {
            var gen = new HexWorldGenerator();
            var settings = new WorldGenerationSettings(-1, SampleHeight, SampleSeed);
            Assert.Throws<ArgumentOutOfRangeException>(() => gen.Generate(settings));
        }

        [Test]
        public void Generate_ZeroHeight_Throws()
        {
            var gen = new HexWorldGenerator();
            var settings = new WorldGenerationSettings(SampleWidth, 0, SampleSeed);
            Assert.Throws<ArgumentOutOfRangeException>(() => gen.Generate(settings));
        }

        [Test]
        public void Generate_NegativeHeight_Throws()
        {
            var gen = new HexWorldGenerator();
            var settings = new WorldGenerationSettings(SampleWidth, -1, SampleSeed);
            Assert.Throws<ArgumentOutOfRangeException>(() => gen.Generate(settings));
        }

        [Test]
        public void Generate_LandRatioZeroOrLess_Throws()
        {
            var gen = new HexWorldGenerator();
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                gen.Generate(new WorldGenerationSettings(SampleWidth, SampleHeight, SampleSeed, 0f)));
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                gen.Generate(new WorldGenerationSettings(SampleWidth, SampleHeight, SampleSeed, -0.1f)));
        }

        [Test]
        public void Generate_LandRatioOneOrMore_Throws()
        {
            var gen = new HexWorldGenerator();
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                gen.Generate(new WorldGenerationSettings(SampleWidth, SampleHeight, SampleSeed, 1f)));
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                gen.Generate(new WorldGenerationSettings(SampleWidth, SampleHeight, SampleSeed, 1.5f)));
        }

        [Test]
        public void Generate_LandRatioNaN_Throws()
        {
            var gen = new HexWorldGenerator();
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                gen.Generate(new WorldGenerationSettings(SampleWidth, SampleHeight, SampleSeed, float.NaN)));
        }

        [Test]
        public void Generate_ValidSettings_ReturnsNonNullMap()
        {
            var gen = new HexWorldGenerator();
            WorldMap map = gen.Generate(new WorldGenerationSettings(SampleWidth, SampleHeight, SampleSeed));
            Assert.That(map, Is.Not.Null);
        }

        [Test]
        public void Generate_MapDimensionsMatchSettings()
        {
            var gen = new HexWorldGenerator();
            WorldMap map = gen.Generate(new WorldGenerationSettings(SampleWidth, SampleHeight, SampleSeed));
            Assert.That(map.Width, Is.EqualTo(SampleWidth));
            Assert.That(map.Height, Is.EqualTo(SampleHeight));
        }

        [Test]
        public void Generate_MapSeedEchoesSettings()
        {
            var gen = new HexWorldGenerator();
            WorldMap map = gen.Generate(new WorldGenerationSettings(SampleWidth, SampleHeight, SampleSeed));
            Assert.That(map.Seed, Is.EqualTo(SampleSeed));
        }

        [Test]
        public void Generate_MapTileCountEqualsWidthTimesHeight()
        {
            var gen = new HexWorldGenerator();
            WorldMap map = gen.Generate(new WorldGenerationSettings(SampleWidth, SampleHeight, SampleSeed));
            Assert.That(map.Tiles.Count, Is.EqualTo(SampleWidth * SampleHeight));
        }

        [Test]
        public void Generate_TwoCallsSameSettings_ProduceStructurallyIdenticalMaps()
        {
            // With an empty pipeline the only observable output is
            // dimensions, seed, and tile count. Slice 5+ add per-tile
            // determinism tests as soon as stages exist.
            var gen = new HexWorldGenerator();
            var settings = new WorldGenerationSettings(SampleWidth, SampleHeight, SampleSeed);

            WorldMap a = gen.Generate(settings);
            WorldMap b = gen.Generate(settings);

            Assert.That(a.Width, Is.EqualTo(b.Width));
            Assert.That(a.Height, Is.EqualTo(b.Height));
            Assert.That(a.Seed, Is.EqualTo(b.Seed));
            Assert.That(a.Tiles.Count, Is.EqualTo(b.Tiles.Count));
        }

        [Test]
        public void Generate_FreshGeneratorInstancePerCall_ProducesEquivalentMaps()
        {
            // The class is intentionally cheap to instantiate; spinning
            // up a fresh HexWorldGenerator per call must not cause any
            // observable difference. Pins the "no hidden cross-call
            // state" contract.
            var settings = new WorldGenerationSettings(SampleWidth, SampleHeight, SampleSeed);
            WorldMap a = new HexWorldGenerator().Generate(settings);
            WorldMap b = new HexWorldGenerator().Generate(settings);

            Assert.That(a.Width, Is.EqualTo(b.Width));
            Assert.That(a.Height, Is.EqualTo(b.Height));
            Assert.That(a.Seed, Is.EqualTo(b.Seed));
        }

        [Test]
        public void Generate_DefaultPipeline_PopulatesAllPerTileFields()
        {
            var gen = new HexWorldGenerator();
            WorldMap map = gen.Generate(new WorldGenerationSettings(SampleWidth, SampleHeight, SampleSeed));

            foreach (HexTile tile in map.Tiles)
            {
                Assert.That(tile.LandState, Is.Not.EqualTo(TileLandState.Unset),
                    $"Tile {tile.TileIndex}: LandState remains Unset.");
                Assert.That(float.IsNaN(tile.Elevation), Is.False,
                    $"Tile {tile.TileIndex}: Elevation remains NaN.");
                Assert.That(tile.Elevation, Is.InRange(0f, 1f));
                Assert.That(tile.ElevationLevel, Is.GreaterThanOrEqualTo(0),
                    $"Tile {tile.TileIndex}: ElevationLevel still -1.");
                Assert.That(tile.ElevationLevel, Is.LessThan(6),
                    $"Tile {tile.TileIndex}: ElevationLevel {tile.ElevationLevel} exceeds default level count.");
            }
        }

        [Test]
        public void Generate_SameSeedTwice_ProducesByteIdenticalMaps_PerTile()
        {
            // The real per-tile determinism gate. Same settings must
            // produce identical LandState, Elevation, and ElevationLevel
            // on every single tile.
            var settings = new WorldGenerationSettings(SampleWidth, SampleHeight, SampleSeed);

            WorldMap a = new HexWorldGenerator().Generate(settings);
            WorldMap b = new HexWorldGenerator().Generate(settings);

            Assert.That(a.Tiles.Count, Is.EqualTo(b.Tiles.Count));
            for (int i = 0; i < a.Tiles.Count; i++)
            {
                HexTile ta = a.Tiles[i];
                HexTile tb = b.Tiles[i];
                Assert.That(ta.LandState, Is.EqualTo(tb.LandState),
                    $"Tile {i}: LandState mismatch.");
                Assert.That(ta.Elevation, Is.EqualTo(tb.Elevation),
                    $"Tile {i}: Elevation mismatch.");
                Assert.That(ta.ElevationLevel, Is.EqualTo(tb.ElevationLevel),
                    $"Tile {i}: ElevationLevel mismatch.");
            }
        }

        [Test]
        public void Generate_DifferentSeeds_ProduceDifferentMaps()
        {
            var s1 = new WorldGenerationSettings(SampleWidth, SampleHeight, 1UL);
            var s2 = new WorldGenerationSettings(SampleWidth, SampleHeight, 2UL);
            WorldMap a = new HexWorldGenerator().Generate(s1);
            WorldMap b = new HexWorldGenerator().Generate(s2);

            int landDiffs = 0;
            int elevationDiffs = 0;
            for (int i = 0; i < a.Tiles.Count; i++)
            {
                if (a.Tiles[i].LandState != b.Tiles[i].LandState) landDiffs++;
                if (a.Tiles[i].Elevation != b.Tiles[i].Elevation) elevationDiffs++;
            }
            Assert.That(landDiffs, Is.GreaterThan(0),
                "Two different seeds produced identical land masks.");
            Assert.That(elevationDiffs, Is.GreaterThan(0),
                "Two different seeds produced identical elevation fields.");
        }
    }
}
