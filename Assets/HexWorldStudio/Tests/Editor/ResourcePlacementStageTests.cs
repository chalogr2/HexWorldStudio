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
    /// EditMode tests for <see cref="ResourcePlacementStage"/> — the
    /// opt-in contract (empty roster / zero density = no-op),
    /// eligibility filtering, spacing enforcement, and determinism.
    /// </summary>
    public class ResourcePlacementStageTests
    {
        private const int Width = 16;
        private const int Height = 12;
        private const ulong SampleSeed = 0x12345_6789ABCuL;

        private static GenerationContext NewContext(
            IReadOnlyList<ResourceDefinition> resources,
            float density,
            ulong seed = SampleSeed)
        {
            var settings = new WorldGenerationSettings(
                Width, Height, seed,
                WorldGenerationSettings.DefaultLandRatio,
                biomes: new BiomeDefinition[0],
                biomeCohesion: 0f, featureCohesion: 0f,
                continentShapeAlgorithm: ContinentShapeAlgorithm.Continents,
                resources: resources,
                resourceDensity: density);
            var map = new WorldMap(Width, Height, seed);
            var rng = new RngService(seed);
            return new GenerationContext(map, settings, rng);
        }

        private static void FillBiome(WorldMap map, BiomeDefinition biome, TileLandState state = TileLandState.Land)
        {
            foreach (HexTile t in map.Tiles)
            {
                t.LandState = state;
                t.Biome = biome;
                t.ElevationLevel = 1;
            }
        }

        private static int CountResource(WorldMap map, ResourceDefinition r)
        {
            int n = 0;
            foreach (HexTile t in map.Tiles) if (t.Resource == r) n++;
            return n;
        }

        // ---- Opt-in contract -------------------------------------

        [Test]
        public void Execute_EmptyRoster_LeavesEveryTileResourceless()
        {
            GenerationContext ctx = NewContext(resources: new ResourceDefinition[0], density: 1f);
            BiomeDefinition land = ScriptableObject.CreateInstance<BiomeDefinition>();
            try
            {
                FillBiome(ctx.Map, land);

                new ResourcePlacementStage().Execute(ctx);

                foreach (HexTile t in ctx.Map.Tiles)
                {
                    Assert.That(t.Resource, Is.Null,
                        "Empty roster must produce zero placements regardless of density.");
                }
            }
            finally { Object.DestroyImmediate(land); }
        }

        [Test]
        public void Execute_NullRoster_LeavesEveryTileResourceless()
        {
            GenerationContext ctx = NewContext(resources: null, density: 1f);
            BiomeDefinition land = ScriptableObject.CreateInstance<BiomeDefinition>();
            try
            {
                FillBiome(ctx.Map, land);
                new ResourcePlacementStage().Execute(ctx);
                foreach (HexTile t in ctx.Map.Tiles) Assert.That(t.Resource, Is.Null);
            }
            finally { Object.DestroyImmediate(land); }
        }

        [Test]
        public void Execute_ZeroDensity_LeavesEveryTileResourceless()
        {
            ResourceDefinition wheat = ScriptableObject.CreateInstance<ResourceDefinition>();
            BiomeDefinition land = ScriptableObject.CreateInstance<BiomeDefinition>();
            try
            {
                wheat.ResourceName = "Wheat";
                GenerationContext ctx = NewContext(resources: new[] { wheat }, density: 0f);
                FillBiome(ctx.Map, land);

                new ResourcePlacementStage().Execute(ctx);

                foreach (HexTile t in ctx.Map.Tiles)
                {
                    Assert.That(t.Resource, Is.Null,
                        "Zero density must produce zero placements even with a populated roster.");
                }
            }
            finally
            {
                Object.DestroyImmediate(wheat);
                Object.DestroyImmediate(land);
            }
        }

        // ---- Eligibility filtering -------------------------------

        [Test]
        public void Execute_BiomeFilter_RespectsAllowedBiomes()
        {
            BiomeDefinition matching = ScriptableObject.CreateInstance<BiomeDefinition>();
            BiomeDefinition excluded = ScriptableObject.CreateInstance<BiomeDefinition>();
            ResourceDefinition wheat = ScriptableObject.CreateInstance<ResourceDefinition>();
            try
            {
                wheat.AllowedBiomes = new[] { matching };
                wheat.MinimumSpacing = 0; // disable spacing so all eligible tiles can place

                GenerationContext ctx = NewContext(resources: new[] { wheat }, density: 1f);

                // Half the map = matching biome, half = excluded biome.
                int total = ctx.Map.Tiles.Count;
                for (int i = 0; i < total; i++)
                {
                    HexTile t = ctx.Map.Tiles[i];
                    t.LandState = TileLandState.Land;
                    t.Biome = (i < total / 2) ? matching : excluded;
                    t.ElevationLevel = 1;
                }

                new ResourcePlacementStage().Execute(ctx);

                for (int i = 0; i < total; i++)
                {
                    HexTile t = ctx.Map.Tiles[i];
                    if (t.Biome == excluded)
                    {
                        Assert.That(t.Resource, Is.Null,
                            $"Tile {i} biome=excluded must never receive a resource with biome filter active.");
                    }
                }
                Assert.That(CountResource(ctx.Map, wheat), Is.GreaterThan(0),
                    "At density=1 with no spacing, eligible tiles must receive placements.");
            }
            finally
            {
                Object.DestroyImmediate(matching);
                Object.DestroyImmediate(excluded);
                Object.DestroyImmediate(wheat);
            }
        }

        // ---- Spacing ---------------------------------------------

        [Test]
        public void Execute_SpacingConstraint_RejectsTooCloseSameResource()
        {
            ResourceDefinition gold = ScriptableObject.CreateInstance<ResourceDefinition>();
            BiomeDefinition land = ScriptableObject.CreateInstance<BiomeDefinition>();
            try
            {
                gold.ResourceName = "Gold";
                gold.MinimumSpacing = 3;
                GenerationContext ctx = NewContext(resources: new[] { gold }, density: 1f);
                FillBiome(ctx.Map, land);

                new ResourcePlacementStage().Execute(ctx);

                // For every pair of placed gold tiles, distance >= 3.
                var placed = new List<HexCoord>();
                foreach (HexTile t in ctx.Map.Tiles)
                {
                    if (t.Resource == gold) placed.Add(t.Coord);
                }
                for (int i = 0; i < placed.Count; i++)
                {
                    for (int j = i + 1; j < placed.Count; j++)
                    {
                        int d = placed[i].Distance(placed[j]);
                        Assert.That(d, Is.GreaterThanOrEqualTo(3),
                            $"Gold pair at indices {i}/{j} sits {d} tiles apart — violates MinimumSpacing=3.");
                    }
                }
            }
            finally
            {
                Object.DestroyImmediate(gold);
                Object.DestroyImmediate(land);
            }
        }

        // ---- Determinism ----------------------------------------

        [Test]
        public void Execute_Deterministic_SameSeedSameOutput()
        {
            ResourceDefinition wheat = ScriptableObject.CreateInstance<ResourceDefinition>();
            ResourceDefinition cattle = ScriptableObject.CreateInstance<ResourceDefinition>();
            BiomeDefinition land = ScriptableObject.CreateInstance<BiomeDefinition>();
            try
            {
                GenerationContext a = NewContext(resources: new[] { wheat, cattle }, density: 0.5f);
                GenerationContext b = NewContext(resources: new[] { wheat, cattle }, density: 0.5f);
                FillBiome(a.Map, land);
                FillBiome(b.Map, land);

                new ResourcePlacementStage().Execute(a);
                new ResourcePlacementStage().Execute(b);

                for (int i = 0; i < a.Map.Tiles.Count; i++)
                {
                    Assert.That(a.Map.Tiles[i].Resource, Is.SameAs(b.Map.Tiles[i].Resource),
                        $"Resource drift at tile {i} — same seed must produce identical placements.");
                }
            }
            finally
            {
                Object.DestroyImmediate(wheat);
                Object.DestroyImmediate(cattle);
                Object.DestroyImmediate(land);
            }
        }

        // ---- Rarity weights / disabled resources ----------------

        [Test]
        public void Execute_RarityZeroResource_NeverPlaced()
        {
            ResourceDefinition disabled = ScriptableObject.CreateInstance<ResourceDefinition>();
            ResourceDefinition enabled = ScriptableObject.CreateInstance<ResourceDefinition>();
            BiomeDefinition land = ScriptableObject.CreateInstance<BiomeDefinition>();
            try
            {
                disabled.ResourceName = "Disabled";
                disabled.Rarity = 0f;
                enabled.ResourceName = "Enabled";
                enabled.Rarity = 1f;
                enabled.MinimumSpacing = 0;

                GenerationContext ctx = NewContext(resources: new[] { disabled, enabled }, density: 1f);
                FillBiome(ctx.Map, land);

                new ResourcePlacementStage().Execute(ctx);

                Assert.That(CountResource(ctx.Map, disabled), Is.EqualTo(0),
                    "Rarity=0 resources must never be picked.");
                Assert.That(CountResource(ctx.Map, enabled), Is.GreaterThan(0),
                    "The enabled sibling must absorb every placement opportunity.");
            }
            finally
            {
                Object.DestroyImmediate(disabled);
                Object.DestroyImmediate(enabled);
                Object.DestroyImmediate(land);
            }
        }

        // ---- Null-context guard ---------------------------------

        [Test]
        public void Execute_NullContext_Throws()
        {
            Assert.Throws<System.ArgumentNullException>(
                () => new ResourcePlacementStage().Execute(null));
        }
    }
}
