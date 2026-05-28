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

namespace HexWorldStudio.Tests
{
    /// <summary>
    /// EditMode tests for <see cref="BiomeAssignmentStage"/> —
    /// determinism, full coverage (no null Biome left), water tile
    /// topology (Coast vs Ocean), envelope-based land assignment, and
    /// the modularity guarantee that the system works with any biome
    /// set the buyer ships (including a non-default 3-biome set).
    /// </summary>
    public class BiomeAssignmentStageTests
    {
        private const int Width = 20;
        private const int Height = 12;
        private const ulong SampleSeed = 0x55AA55AA55AA55AAUL;

        // ---- biome factories ----

        private static BiomeDefinition NewBiome(
            string name,
            bool isWater = false,
            bool isCoastalWater = false,
            float minT = 0f, float maxT = 1f,
            float minM = 0f, float maxM = 1f)
        {
            var b = ScriptableObject.CreateInstance<BiomeDefinition>();
            b.BiomeName = name;
            b.IsWater = isWater;
            b.IsCoastalWater = isCoastalWater;
            b.MinTemperature = minT;
            b.MaxTemperature = maxT;
            b.MinMoisture = minM;
            b.MaxMoisture = maxM;
            return b;
        }

        private static IReadOnlyList<BiomeDefinition> MinimalThreeBiomeSet(
            out BiomeDefinition ocean, out BiomeDefinition coast, out BiomeDefinition land)
        {
            ocean = NewBiome("Ocean", isWater: true, isCoastalWater: false);
            coast = NewBiome("Coast", isWater: true, isCoastalWater: true);
            land = NewBiome("Land", isWater: false);
            return new[] { ocean, coast, land };
        }

        private static GenerationContext NewContext(
            ulong seed, IReadOnlyList<BiomeDefinition> biomes)
        {
            var settings = new WorldGenerationSettings(
                Width, Height, seed,
                WorldGenerationSettings.DefaultLandRatio, biomes);
            var map = new WorldMap(Width, Height, seed);
            var rng = new RngService(seed);
            var ctx = new GenerationContext(map, settings, rng);
            new ContinentShapeStage().Execute(ctx);
            new ElevationFieldStage().Execute(ctx);
            new ElevationLevelQuantizationStage().Execute(ctx);
            new ClimateStage().Execute(ctx);
            return ctx;
        }

        // ---- tests ----

        [Test]
        public void Execute_SameSeed_ProducesSameBiomes()
        {
            IReadOnlyList<BiomeDefinition> biomes1 = MinimalThreeBiomeSet(out _, out _, out _);
            IReadOnlyList<BiomeDefinition> biomes2 = MinimalThreeBiomeSet(out _, out _, out _);
            // Fresh BiomeDefinition instances each call, but the
            // assignment depends only on which "role" each tile resolves
            // to. We compare biome NAMES per tile across two runs to
            // verify determinism without object-identity coupling.

            GenerationContext a = NewContext(SampleSeed, biomes1);
            GenerationContext b = NewContext(SampleSeed, biomes2);

            new BiomeAssignmentStage().Execute(a);
            new BiomeAssignmentStage().Execute(b);

            for (int i = 0; i < a.Map.Tiles.Count; i++)
            {
                Assert.That(a.Map.Tiles[i].Biome.BiomeName,
                    Is.EqualTo(b.Map.Tiles[i].Biome.BiomeName),
                    $"Biome diverged at tile {i}.");
            }
        }

        [Test]
        public void Execute_EveryTileHasNonNullBiome()
        {
            IReadOnlyList<BiomeDefinition> biomes = MinimalThreeBiomeSet(out _, out _, out _);
            GenerationContext ctx = NewContext(SampleSeed, biomes);
            new BiomeAssignmentStage().Execute(ctx);

            foreach (HexTile tile in ctx.Map.Tiles)
            {
                Assert.That(tile.Biome, Is.Not.Null,
                    $"Tile {tile.TileIndex} ({tile.Coord}) has null Biome after Execute.");
            }
        }

        [Test]
        public void Execute_WaterTileWithLandNeighbor_BecomesCoast()
        {
            IReadOnlyList<BiomeDefinition> biomes = MinimalThreeBiomeSet(
                out BiomeDefinition ocean, out BiomeDefinition coast, out BiomeDefinition land);

            // Hand-craft a tiny map: one land tile in the middle of a sea.
            var settings = new WorldGenerationSettings(5, 5, 0UL,
                WorldGenerationSettings.DefaultLandRatio, biomes);
            var map = new WorldMap(5, 5, 0UL);
            foreach (HexTile t in map.Tiles)
            {
                t.LandState = TileLandState.Water;
                t.Elevation = 0f;
                t.Temperature = 0.5f;
                t.Moisture = 1f;
                t.ElevationLevel = 0;
            }
            map.TryGetTile(HexCoordExtensions.FromOffsetOddR(2, 2), out HexTile center);
            center.LandState = TileLandState.Land;

            var ctx = new GenerationContext(map, settings, new RngService(0UL));
            new BiomeAssignmentStage().Execute(ctx);

            // Every neighbor of the land tile must now be Coast.
            foreach (HexCoord nc in center.Coord.Neighbors())
            {
                if (!map.TryGetTile(nc, out HexTile neighbor)) continue;
                Assert.That(neighbor.Biome, Is.SameAs(coast),
                    $"Water tile {neighbor.Coord} adjacent to land was assigned {neighbor.Biome.BiomeName} instead of Coast.");
            }
            // The land tile itself is Land.
            Assert.That(center.Biome, Is.SameAs(land));
        }

        [Test]
        public void Execute_WaterTileWithNoLandNeighbors_BecomesOcean()
        {
            IReadOnlyList<BiomeDefinition> biomes = MinimalThreeBiomeSet(
                out BiomeDefinition ocean, out BiomeDefinition coast, out BiomeDefinition _);

            // All-water map. Every tile should be Ocean.
            var settings = new WorldGenerationSettings(5, 5, 0UL,
                WorldGenerationSettings.DefaultLandRatio, biomes);
            var map = new WorldMap(5, 5, 0UL);
            foreach (HexTile t in map.Tiles)
            {
                t.LandState = TileLandState.Water;
                t.Elevation = 0f;
                t.Temperature = 0.5f;
                t.Moisture = 1f;
                t.ElevationLevel = 0;
            }

            var ctx = new GenerationContext(map, settings, new RngService(0UL));
            new BiomeAssignmentStage().Execute(ctx);

            foreach (HexTile t in map.Tiles)
            {
                Assert.That(t.Biome, Is.SameAs(ocean),
                    $"Water tile in all-water map became {t.Biome.BiomeName} instead of Ocean.");
            }
        }

        [Test]
        public void Execute_LandTile_PicksFirstMatchingEnvelope()
        {
            BiomeDefinition ocean = NewBiome("Ocean", isWater: true, isCoastalWater: false);
            BiomeDefinition coast = NewBiome("Coast", isWater: true, isCoastalWater: true);
            BiomeDefinition cold  = NewBiome("Cold",  minT: 0f,   maxT: 0.5f, minM: 0f, maxM: 1f);
            BiomeDefinition warm  = NewBiome("Warm",  minT: 0.5f, maxT: 1f,   minM: 0f, maxM: 1f);
            IReadOnlyList<BiomeDefinition> biomes = new[] { ocean, coast, cold, warm };

            var settings = new WorldGenerationSettings(3, 3, 0UL,
                WorldGenerationSettings.DefaultLandRatio, biomes);
            var map = new WorldMap(3, 3, 0UL);
            foreach (HexTile t in map.Tiles)
            {
                t.LandState = TileLandState.Land;
                t.Elevation = 0f;
                t.ElevationLevel = 0;
                t.Moisture = 0.5f;
            }
            // Force three test cases by Temperature.
            map.TryGetTile(HexCoordExtensions.FromOffsetOddR(0, 0), out HexTile t0); t0.Temperature = 0.2f;
            map.TryGetTile(HexCoordExtensions.FromOffsetOddR(1, 1), out HexTile t1); t1.Temperature = 0.5f; // boundary
            map.TryGetTile(HexCoordExtensions.FromOffsetOddR(2, 2), out HexTile t2); t2.Temperature = 0.8f;

            var ctx = new GenerationContext(map, settings, new RngService(0UL));
            new BiomeAssignmentStage().Execute(ctx);

            Assert.That(t0.Biome, Is.SameAs(cold));
            // Boundary tie — Cold's upper bound is 0.5 inclusive,
            // Warm's lower bound is 0.5 inclusive, and first match wins.
            Assert.That(t1.Biome, Is.SameAs(cold));
            Assert.That(t2.Biome, Is.SameAs(warm));
        }

        [Test]
        public void Execute_LandTile_FallbackBiome_UsedWhenNoEnvelopeMatches()
        {
            // Author an envelope that deliberately excludes a region of
            // (T, M) space; verify the fallback (first non-water biome)
            // is assigned to a tile in the gap.
            BiomeDefinition ocean = NewBiome("Ocean", isWater: true, isCoastalWater: false);
            BiomeDefinition coast = NewBiome("Coast", isWater: true, isCoastalWater: true);
            BiomeDefinition narrow = NewBiome("Narrow", minT: 0f, maxT: 0.2f, minM: 0f, maxM: 0.2f);
            IReadOnlyList<BiomeDefinition> biomes = new[] { ocean, coast, narrow };

            var settings = new WorldGenerationSettings(3, 3, 0UL,
                WorldGenerationSettings.DefaultLandRatio, biomes);
            var map = new WorldMap(3, 3, 0UL);
            foreach (HexTile t in map.Tiles)
            {
                t.LandState = TileLandState.Land;
                t.Elevation = 0f;
                t.ElevationLevel = 0;
                t.Temperature = 0.9f;
                t.Moisture = 0.9f; // far outside Narrow envelope
            }

            var ctx = new GenerationContext(map, settings, new RngService(0UL));
            new BiomeAssignmentStage().Execute(ctx);

            foreach (HexTile tile in map.Tiles)
            {
                Assert.That(tile.Biome, Is.SameAs(narrow),
                    $"Tile {tile.Coord} fell out of Narrow's envelope but did not receive the fallback biome.");
            }
        }

        [Test]
        public void Generate_WithCustomThreeBiomeSet_WorksEndToEnd()
        {
            // The modularity guarantee from ADR-0011 / ADR-0007 — the
            // system must work with any buyer-authored biome set, not
            // just the 7-biome default. Construct a minimal 3-biome
            // set entirely from code (no .asset files) and run the
            // full default pipeline through HexWorldGenerator.
            IReadOnlyList<BiomeDefinition> biomes = MinimalThreeBiomeSet(
                out BiomeDefinition ocean, out BiomeDefinition coast, out BiomeDefinition land);

            var settings = new WorldGenerationSettings(
                Width, Height, SampleSeed,
                WorldGenerationSettings.DefaultLandRatio, biomes);

            WorldMap map = new HexWorldGenerator().Generate(settings);

            int oceanCount = 0, coastCount = 0, landCount = 0;
            foreach (HexTile tile in map.Tiles)
            {
                Assert.That(tile.Biome, Is.Not.Null,
                    $"Tile {tile.TileIndex} has null Biome after Generate.");
                if (tile.Biome == ocean) oceanCount++;
                else if (tile.Biome == coast) coastCount++;
                else if (tile.Biome == land) landCount++;
            }

            // All three roles should actually appear in any reasonable
            // map at this size — coastal water requires both ocean and
            // land somewhere in the world.
            Assert.That(oceanCount, Is.GreaterThan(0));
            Assert.That(coastCount, Is.GreaterThan(0));
            Assert.That(landCount, Is.GreaterThan(0));
        }

        [Test]
        public void Execute_NullContext_Throws()
        {
            Assert.Throws<ArgumentNullException>(() => new BiomeAssignmentStage().Execute(null));
        }

        [Test]
        public void Name_IsBiomeAssignment()
        {
            Assert.That(new BiomeAssignmentStage().Name, Is.EqualTo("BiomeAssignment"));
        }
    }
}
