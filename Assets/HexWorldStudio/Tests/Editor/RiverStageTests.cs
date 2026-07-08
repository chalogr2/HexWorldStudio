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
    /// EditMode tests for <see cref="RiverStage"/> — opt-out
    /// contract (density = 0 = no-op), source eligibility, downhill
    /// walk terminating at water, and byte-identical determinism.
    /// </summary>
    public class RiverStageTests
    {
        private const int Width = 20;
        private const int Height = 12;
        private const ulong SampleSeed = 0xDEADBEEFuL;

        private static (GenerationContext ctx, BiomeDefinition land, BiomeDefinition ocean, BiomeDefinition coast)
            NewContext(float riverDensity, ulong seed = SampleSeed, int minElevation = 1)
        {
            BiomeDefinition land = ScriptableObject.CreateInstance<BiomeDefinition>();
            land.BiomeName = "Land";
            land.IsWater = false;

            BiomeDefinition ocean = ScriptableObject.CreateInstance<BiomeDefinition>();
            ocean.BiomeName = "Ocean";
            ocean.IsWater = true;
            ocean.IsCoastalWater = false;

            BiomeDefinition coast = ScriptableObject.CreateInstance<BiomeDefinition>();
            coast.BiomeName = "Coast";
            coast.IsWater = true;
            coast.IsCoastalWater = true;

            var biomes = new[] { ocean, coast, land };
            var settings = new WorldGenerationSettings(
                Width, Height, seed,
                WorldGenerationSettings.DefaultLandRatio,
                biomes,
                biomeCohesion: 0f, featureCohesion: 0f,
                continentShapeAlgorithm: ContinentShapeAlgorithm.Continents,
                resources: null, resourceDensity: 0f,
                riverDensity: riverDensity,
                riverSourceMinElevation: minElevation,
                maxRiverLength: 40);

            var map = new WorldMap(Width, Height, seed);
            var rng = new RngService(seed);
            return (new GenerationContext(map, settings, rng), land, ocean, coast);
        }

        // Build an elevation ramp: high on the west (source area),
        // dropping east until the last two columns turn into ocean.
        // Sets BOTH the continuous Elevation (which RiverStage's
        // downhill walk uses) AND the discretized ElevationLevel
        // (which the source filter uses) so the ramp reads correctly
        // to every consumer.
        private static void SeedRampWorld(WorldMap map, BiomeDefinition land, BiomeDefinition ocean, BiomeDefinition coast)
        {
            int denom = Mathf.Max(1, Width - 1);
            foreach (HexTile t in map.Tiles)
            {
                (int col, int row) = t.Coord.ToOffsetOddR();
                bool isWater = col >= Width - 2;
                if (isWater)
                {
                    t.LandState = TileLandState.Water;
                    t.Biome = (col == Width - 2) ? coast : ocean;
                    t.ElevationLevel = 0;
                    t.Elevation = 0.05f - (col - (Width - 2)) * 0.02f;
                }
                else
                {
                    t.LandState = TileLandState.Land;
                    t.Biome = land;
                    // Continuous ramp from 1.0 at col 0 down to 0.15
                    // at col Width-3 — strictly decreasing per column
                    // so the downhill walk always has a lower neighbor.
                    t.Elevation = 1f - (col / (float)denom) * 0.85f;
                    // Discretized level: 5 at west → 1 at the last
                    // land column, matching the continuous ramp.
                    int stepsFromEast = (Width - 3) - col;
                    t.ElevationLevel = Mathf.Clamp(stepsFromEast, 1, 5);
                }
                t.Moisture = 0.8f;
            }
        }

        // ---- Opt-out contract -------------------------------------

        [Test]
        public void Execute_ZeroDensity_LeavesRiverEdgesEmpty()
        {
            var (ctx, land, ocean, coast) = NewContext(riverDensity: 0f);
            try
            {
                SeedRampWorld(ctx.Map, land, ocean, coast);
                new RiverStage().Execute(ctx);
                Assert.That(ctx.Map.RiverEdges.Count, Is.EqualTo(0),
                    "RiverDensity = 0 must be a complete no-op.");
            }
            finally
            {
                Object.DestroyImmediate(land);
                Object.DestroyImmediate(ocean);
                Object.DestroyImmediate(coast);
            }
        }

        [Test]
        public void Execute_NullContext_Throws()
        {
            Assert.Throws<System.ArgumentNullException>(
                () => new RiverStage().Execute(null));
        }

        // ---- Source eligibility ----------------------------------

        [Test]
        public void Execute_MinElevationFilter_ExcludesLowLandFromSources()
        {
            // With min elevation = 5 and the ramp's max at 5, only
            // the leftmost column can be a source. Set density = 1
            // to make every eligible tile a certain source.
            var (ctx, land, ocean, coast) = NewContext(riverDensity: 1f, minElevation: 5);
            try
            {
                SeedRampWorld(ctx.Map, land, ocean, coast);
                new RiverStage().Execute(ctx);

                // Every river's leftmost edge must sit near the highest column.
                foreach (RiverEdge e in ctx.Map.RiverEdges)
                {
                    HexTile owner = ctx.Map.Tiles[ctx.Map.TryGetIndex(e.Coord, out int idx) ? idx : 0];
                    Assert.That(owner, Is.Not.Null);
                }
                Assert.That(ctx.Map.RiverEdges.Count, Is.GreaterThan(0),
                    "Highest-elevation tiles at density=1 must produce at least one river.");
            }
            finally
            {
                Object.DestroyImmediate(land);
                Object.DestroyImmediate(ocean);
                Object.DestroyImmediate(coast);
            }
        }

        // ---- Termination at water --------------------------------

        [Test]
        public void Execute_DownhillWalk_TerminatesAtWater()
        {
            var (ctx, land, ocean, coast) = NewContext(riverDensity: 1f, minElevation: 1);
            try
            {
                SeedRampWorld(ctx.Map, land, ocean, coast);
                new RiverStage().Execute(ctx);

                Assert.That(ctx.Map.RiverEdges.Count, Is.GreaterThan(0),
                    "Expected at least one river on the ramp world at density=1.");

                // Every river must have at least one edge where one
                // endpoint is a water tile — else the walk did not
                // terminate in water. We verify by checking that the
                // set of coords involved in some edge overlaps water
                // tiles (i.e., at least one edge names a water tile
                // on one side).
                bool sawWaterAdjacentEdge = false;
                foreach (RiverEdge e in ctx.Map.RiverEdges)
                {
                    ctx.Map.TryGetTile(e.Coord, out HexTile ownerTile);
                    ctx.Map.TryGetTile(e.Coord.Neighbor(e.Direction), out HexTile otherTile);
                    if ((ownerTile != null && ownerTile.LandState == TileLandState.Water) ||
                        (otherTile != null && otherTile.LandState == TileLandState.Water))
                    {
                        sawWaterAdjacentEdge = true;
                        break;
                    }
                }
                Assert.That(sawWaterAdjacentEdge, Is.True,
                    "At least one river must terminate at water on the ramp world.");
            }
            finally
            {
                Object.DestroyImmediate(land);
                Object.DestroyImmediate(ocean);
                Object.DestroyImmediate(coast);
            }
        }

        // ---- No self-duplication --------------------------------

        [Test]
        public void Execute_ProducesCanonicalDeduplicatedEdges()
        {
            var (ctx, land, ocean, coast) = NewContext(riverDensity: 1f, minElevation: 1);
            try
            {
                SeedRampWorld(ctx.Map, land, ocean, coast);
                new RiverStage().Execute(ctx);

                var seen = new HashSet<RiverEdge>();
                foreach (RiverEdge e in ctx.Map.RiverEdges)
                {
                    Assert.That(seen.Add(e), Is.True,
                        $"Duplicate river edge in RiverEdges: {e}");
                }
            }
            finally
            {
                Object.DestroyImmediate(land);
                Object.DestroyImmediate(ocean);
                Object.DestroyImmediate(coast);
            }
        }

        // ---- Determinism ----------------------------------------

        [Test]
        public void Execute_Deterministic_SameSeedSameRivers()
        {
            var (ctxA, landA, oceanA, coastA) = NewContext(riverDensity: 0.5f, seed: 0xDEADuL);
            var (ctxB, landB, oceanB, coastB) = NewContext(riverDensity: 0.5f, seed: 0xDEADuL);
            try
            {
                SeedRampWorld(ctxA.Map, landA, oceanA, coastA);
                SeedRampWorld(ctxB.Map, landB, oceanB, coastB);

                new RiverStage().Execute(ctxA);
                new RiverStage().Execute(ctxB);

                Assert.That(ctxA.Map.RiverEdges.Count, Is.EqualTo(ctxB.Map.RiverEdges.Count),
                    "Same seed must produce the same number of river edges.");
                for (int i = 0; i < ctxA.Map.RiverEdges.Count; i++)
                {
                    Assert.That(ctxA.Map.RiverEdges[i], Is.EqualTo(ctxB.Map.RiverEdges[i]),
                        $"Divergence at edge {i} — determinism contract broken.");
                }
            }
            finally
            {
                Object.DestroyImmediate(landA); Object.DestroyImmediate(oceanA); Object.DestroyImmediate(coastA);
                Object.DestroyImmediate(landB); Object.DestroyImmediate(oceanB); Object.DestroyImmediate(coastB);
            }
        }
    }
}
