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

        // ---- Termination at coast --------------------------------

        [Test]
        public void Execute_RiverEdges_AreAllBetweenLandTiles()
        {
            // Rivers must never occupy a land ↔ water edge; the walk
            // terminates before crossing into water and the last
            // recorded edge is a land ↔ land seam whose vertex
            // touches a water tile (river mouth). Civ V / VI
            // convention per ADR-0024.
            var (ctx, land, ocean, coast) = NewContext(riverDensity: 1f, minElevation: 1);
            try
            {
                SeedRampWorld(ctx.Map, land, ocean, coast);
                new RiverStage().Execute(ctx);

                Assert.That(ctx.Map.RiverEdges.Count, Is.GreaterThan(0),
                    "Expected at least one river on the ramp world at density=1.");

                foreach (RiverEdge e in ctx.Map.RiverEdges)
                {
                    ctx.Map.TryGetTile(e.Coord, out HexTile ownerTile);
                    ctx.Map.TryGetTile(e.Coord.Neighbor(e.Direction), out HexTile otherTile);
                    Assert.That(ownerTile, Is.Not.Null);
                    Assert.That(otherTile, Is.Not.Null);
                    Assert.That(ownerTile.LandState, Is.EqualTo(TileLandState.Land),
                        $"River edge {e} has a water endpoint (owner {ownerTile.Coord}).");
                    Assert.That(otherTile.LandState, Is.EqualTo(TileLandState.Land),
                        $"River edge {e} has a water endpoint (other {otherTile.Coord}).");
                }
            }
            finally
            {
                Object.DestroyImmediate(land);
                Object.DestroyImmediate(ocean);
                Object.DestroyImmediate(coast);
            }
        }

        [Test]
        public void Execute_AtLeastOneRiverReachesTheCoast()
        {
            // Companion to the "no water-edge" invariant: verify the
            // walk actually gets to the coast (not that every walk
            // fails to reach water at all — that would be a silent
            // regression). Some river edge must involve a land tile
            // that has a water neighbor.
            var (ctx, land, ocean, coast) = NewContext(riverDensity: 1f, minElevation: 1);
            try
            {
                SeedRampWorld(ctx.Map, land, ocean, coast);
                new RiverStage().Execute(ctx);

                bool coastalEdgeExists = false;
                foreach (RiverEdge e in ctx.Map.RiverEdges)
                {
                    ctx.Map.TryGetTile(e.Coord, out HexTile ownerTile);
                    ctx.Map.TryGetTile(e.Coord.Neighbor(e.Direction), out HexTile otherTile);
                    if (HasWaterNeighbor(ctx.Map, ownerTile) || HasWaterNeighbor(ctx.Map, otherTile))
                    {
                        coastalEdgeExists = true;
                        break;
                    }
                }
                Assert.That(coastalEdgeExists, Is.True,
                    "At least one river edge must involve a land tile adjacent to water — else no river reaches the coast.");
            }
            finally
            {
                Object.DestroyImmediate(land);
                Object.DestroyImmediate(ocean);
                Object.DestroyImmediate(coast);
            }
        }

        private static bool HasWaterNeighbor(WorldMap map, HexTile tile)
        {
            if (tile == null) return false;
            foreach (HexCoord n in tile.Coord.Neighbors())
            {
                if (map.TryGetTile(n, out HexTile neighbor)
                    && neighbor.LandState == TileLandState.Water)
                {
                    return true;
                }
            }
            return false;
        }

        // ---- Deferred lake flips --------------------------------

        // All-land bowl: elevation rises with distance from a single
        // interior tile, so that tile is the unique global minimum and
        // every walk converges on it. There is no water anywhere, so
        // the walk cannot terminate at a coast — it must terminate at
        // the minimum and queue it as a pending lake.
        //
        // The ramp world used by the other tests is strictly monotone
        // and therefore never produces a local minimum, which is why
        // the mid-walk flip corruption went unnoticed: no existing test
        // ever reached that branch.
        private static HexCoord SeedBowlWorld(WorldMap map, BiomeDefinition land)
        {
            int centerCol = Width / 2;
            int centerRow = Height / 2;
            HexCoord center = HexCoordExtensions.FromOffsetOddR(centerCol, centerRow);

            float maxDist = Mathf.Sqrt(centerCol * centerCol + centerRow * centerRow);
            foreach (HexTile t in map.Tiles)
            {
                (int col, int row) = t.Coord.ToOffsetOddR();
                float dc = col - centerCol;
                float dr = row - centerRow;
                float dist = Mathf.Sqrt(dc * dc + dr * dr);

                t.LandState = TileLandState.Land;
                t.Biome = land;
                t.Elevation = 0.05f + (dist / maxDist) * 0.95f;
                t.ElevationLevel = Mathf.Clamp(Mathf.RoundToInt(dist / maxDist * 5f), 0, 5);
                t.Moisture = 0.8f;
            }
            return center;
        }

        [Test]
        public void Execute_InlandLocalMinimum_BecomesLakeWithNoWaterEdges()
        {
            var (ctx, land, ocean, coast) = NewContext(riverDensity: 1f, minElevation: 1);
            try
            {
                HexCoord center = SeedBowlWorld(ctx.Map, land);
                new RiverStage().Execute(ctx);

                Assert.That(ctx.Map.TryGetTile(center, out HexTile centerTile), Is.True);
                Assert.That(centerTile.LandState, Is.EqualTo(TileLandState.Water),
                    "The unique global minimum must be flipped to a lake.");
                Assert.That(centerTile.Biome, Is.SameAs(coast),
                    "Lake tiles take the roster's coastal-water biome.");

                // The invariant the mid-walk flip used to break: the
                // edge that arrived at the minimum was recorded while
                // the tile was still land, and stayed in the set after
                // the flip made it a shoreline edge.
                foreach (RiverEdge e in ctx.Map.RiverEdges)
                {
                    ctx.Map.TryGetTile(e.Coord, out HexTile owner);
                    ctx.Map.TryGetTile(e.Coord.Neighbor(e.Direction), out HexTile other);
                    Assert.That(owner.LandState, Is.EqualTo(TileLandState.Land),
                        $"Edge {e} touches the lake at {owner.Coord}.");
                    Assert.That(other.LandState, Is.EqualTo(TileLandState.Land),
                        $"Edge {e} touches the lake at {other.Coord}.");
                }
            }
            finally
            {
                Object.DestroyImmediate(land);
                Object.DestroyImmediate(ocean);
                Object.DestroyImmediate(coast);
            }
        }

        [Test]
        public void Execute_LakeFlips_AreDeterministic()
        {
            var (ctxA, landA, oceanA, coastA) = NewContext(riverDensity: 0.6f, seed: 0xB0A7uL);
            var (ctxB, landB, oceanB, coastB) = NewContext(riverDensity: 0.6f, seed: 0xB0A7uL);
            try
            {
                SeedBowlWorld(ctxA.Map, landA);
                SeedBowlWorld(ctxB.Map, landB);

                new RiverStage().Execute(ctxA);
                new RiverStage().Execute(ctxB);

                for (int i = 0; i < ctxA.Map.Tiles.Count; i++)
                {
                    Assert.That(ctxA.Map.Tiles[i].LandState, Is.EqualTo(ctxB.Map.Tiles[i].LandState),
                        $"Lake flip diverged at tile index {i} — determinism contract broken.");
                }
            }
            finally
            {
                Object.DestroyImmediate(landA); Object.DestroyImmediate(oceanA); Object.DestroyImmediate(coastA);
                Object.DestroyImmediate(landB); Object.DestroyImmediate(oceanB); Object.DestroyImmediate(coastB);
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
