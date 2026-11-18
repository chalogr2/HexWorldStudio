using System.Collections.Generic;
using HexWorldStudio.Content;
using HexWorldStudio.Coordinates;
using HexWorldStudio.Data;
using HexWorldStudio.Generation;
using HexWorldStudio.Rendering;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

namespace HexWorldStudio.Tests
{
    /// <summary>
    /// Connected water shares one height, and raised tiles get cliffs
    /// down to their lowest neighbor.
    /// </summary>
    public class WaterLevelAndCliffTests
    {
        private const int Width = 48;
        private const int Height = 32;
        private const float OceanY = 0f;
        private const float LandRise = 0f;
        private const float Step = 0.15f;

        private static readonly ulong[] Seeds = { 1UL, 7UL, 42UL, 0xBEEFUL, 0xC0FFEEUL, 99991UL };

        private readonly List<Object> _created = new List<Object>();

        [TearDown]
        public void TearDown()
        {
            foreach (Object o in _created) if (o != null) Object.DestroyImmediate(o);
            _created.Clear();
        }

        private BiomeDefinition NewBiome(string name, bool isWater, bool isCoastal)
        {
            var b = ScriptableObject.CreateInstance<BiomeDefinition>();
            b.BiomeName = name;
            b.IsWater = isWater;
            b.IsCoastalWater = isCoastal;
            b.MinTemperature = 0f;
            b.MaxTemperature = 1f;
            b.MinMoisture = 0f;
            b.MaxMoisture = 1f;
            _created.Add(b);
            return b;
        }

        private WorldMap Generate(ulong seed)
        {
            var biomes = new[]
            {
                NewBiome("Ocean", true, false),
                NewBiome("Coast", true, true),
                NewBiome("Land", false, false),
            };
            return new HexWorldGenerator().Generate(
                new WorldGenerationSettings(Width, Height, seed, WorldGenerationSettings.DefaultLandRatio, biomes));
        }

        private static List<List<HexTile>> WaterBodies(WorldMap map)
        {
            var bodies = new List<List<HexTile>>();
            var seen = new bool[map.Tiles.Count];
            foreach (HexTile start in map.Tiles)
            {
                if (seen[start.TileIndex] || start.LandState != TileLandState.Water) continue;
                var body = new List<HexTile>();
                var stack = new Stack<HexTile>();
                seen[start.TileIndex] = true;
                stack.Push(start);
                while (stack.Count > 0)
                {
                    HexTile t = stack.Pop();
                    body.Add(t);
                    for (int d = 0; d < 6; d++)
                    {
                        if (!map.TryGetTile(t.Coord.Neighbor((HexDirection)d), out HexTile n)) continue;
                        if (n.LandState != TileLandState.Water || seen[n.TileIndex]) continue;
                        seen[n.TileIndex] = true;
                        stack.Push(n);
                    }
                }
                bodies.Add(body);
            }
            return bodies;
        }

        // ---- water ---------------------------------------------------

        [Test]
        public void EveryConnectedWaterBody_RendersAtOneHeight()
        {
            int lakeTiles = 0;
            foreach (ulong seed in Seeds)
            {
                WorldMap map = Generate(seed);
                foreach (List<HexTile> body in WaterBodies(map))
                {
                    float y0 = WorldRenderer.ComputeTileY(body[0], OceanY, LandRise, Step);
                    foreach (HexTile t in body)
                    {
                        if (t.IsLake) lakeTiles++;
                        Assert.That(WorldRenderer.ComputeTileY(t, OceanY, LandRise, Step), Is.EqualTo(y0).Within(1e-5f),
                            $"seed {seed}: water tile {t.Coord} sits at a different height from the rest of its body.");
                    }
                }
            }
            Assert.That(lakeTiles, Is.GreaterThan(0), "No lakes generated; the lake half of this test would be vacuous.");
        }

        [Test]
        public void LakeBodies_SitFlushWithTheirLowestShore()
        {
            foreach (ulong seed in Seeds)
            {
                WorldMap map = Generate(seed);
                foreach (List<HexTile> body in WaterBodies(map))
                {
                    if (!body[0].IsLake) continue;
                    int lowestShore = int.MaxValue;
                    foreach (HexTile t in body)
                    {
                        Assert.That(t.IsLake, Is.True, $"seed {seed}: lake body mixes lake and sea tiles at {t.Coord}.");
                        for (int d = 0; d < 6; d++)
                        {
                            if (map.TryGetTile(t.Coord.Neighbor((HexDirection)d), out HexTile n)
                                && n.LandState == TileLandState.Land
                                && n.ElevationLevel < lowestShore)
                            {
                                lowestShore = n.ElevationLevel;
                            }
                        }
                    }
                    Assert.That(body[0].ElevationLevel, Is.EqualTo(lowestShore),
                        $"seed {seed}: lake at {body[0].Coord} should sit at its lowest shore level.");
                }
            }
        }

        // ---- cliffs: pure helpers -------------------------------------

        [Test]
        public void CliffPieceCount_RoundsUpAndIgnoresTinyGaps()
        {
            Assert.That(WorldRenderer.CliffPieceCount(0f, 0.34f), Is.EqualTo(0));
            Assert.That(WorldRenderer.CliffPieceCount(0.0001f, 0.34f), Is.EqualTo(0));
            Assert.That(WorldRenderer.CliffPieceCount(0.15f, 0.34f), Is.EqualTo(1));
            Assert.That(WorldRenderer.CliffPieceCount(0.34f, 0.34f), Is.EqualTo(1));
            Assert.That(WorldRenderer.CliffPieceCount(0.35f, 0.34f), Is.EqualTo(2));
            Assert.That(WorldRenderer.CliffPieceCount(0.75f, 0.34f), Is.EqualTo(3));
            Assert.That(WorldRenderer.CliffPieceCount(1f, 0f), Is.EqualTo(0), "Zero-height piece must not loop forever.");
        }

        [Test]
        public void CliffFloor_IsLowestNeighbor_AndWaterNeverNeedsCliffs()
        {
            foreach (ulong seed in Seeds)
            {
                WorldMap map = Generate(seed);
                foreach (HexTile t in map.Tiles)
                {
                    float y = WorldRenderer.ComputeTileY(t, OceanY, LandRise, Step);
                    float floor = WorldRenderer.ComputeCliffFloorY(map, t, OceanY, LandRise, Step);
                    Assert.That(floor, Is.LessThanOrEqualTo(y + 1e-5f));
                    Assert.That(floor, Is.GreaterThanOrEqualTo(OceanY - 1e-5f));

                    if (t.LandState == TileLandState.Water)
                    {
                        Assert.That(floor, Is.EqualTo(y).Within(1e-5f),
                            $"seed {seed}: water tile {t.Coord} (lake: {t.IsLake}) would get a cliff under it.");
                    }
                }
            }
        }

        // ---- cliffs: built geometry -----------------------------------

        [Test]
        public void Render_WithCliffPrefab_StacksPiecesThatExactlyFillEachGap()
        {
            // A unit cube spans [-0.5, 0.5]: height 1, bottom offset -0.5,
            // so this also checks the bottom-offset correction.
            GameObject cliffPrefab = GameObject.CreatePrimitive(PrimitiveType.Cube);
            _created.Add(cliffPrefab);
            cliffPrefab.transform.position = new Vector3(0f, -5000f, 0f);

            var host = new GameObject("CliffHost");
            _created.Add(host);
            WorldRenderer renderer = host.AddComponent<WorldRenderer>();
            renderer.ElevationStep = 0.4f; // large steps so multi-piece stacks occur
            renderer.LandRiseAboveWater = 0.15f;
            renderer.CliffPrefab = cliffPrefab;

            WorldMap map = Generate(Seeds[0]);
            renderer.Render(map);

            int tilesWithCliffs = 0;
            int multiPieceStacks = 0;
            foreach (Transform tileRoot in host.transform)
            {
                HexTileBinding binding = tileRoot.GetComponent<HexTileBinding>();
                Assert.That(map.TryGetTile(binding.Coord, out HexTile tile), Is.True);

                float y = WorldRenderer.ComputeTileY(tile, renderer.OceanRenderHeight, renderer.LandRiseAboveWater, renderer.ElevationStep);
                float floor = WorldRenderer.ComputeCliffFloorY(map, tile, renderer.OceanRenderHeight, renderer.LandRiseAboveWater, renderer.ElevationStep);
                int expected = WorldRenderer.CliffPieceCount(y - floor, 1f);

                var pieces = new List<Transform>();
                foreach (Transform child in tileRoot) if (child.name == "Cliff") pieces.Add(child);
                Assert.That(pieces.Count, Is.EqualTo(expected), $"Wrong cliff count under {tile.Coord}.");
                if (expected == 0) continue;

                tilesWithCliffs++;
                if (expected > 1) multiPieceStacks++;

                float bottom = float.MaxValue, top = float.MinValue;
                foreach (Transform piece in pieces)
                {
                    Bounds b = piece.GetComponent<Renderer>().bounds;
                    bottom = Mathf.Min(bottom, b.min.y);
                    top = Mathf.Max(top, b.max.y);
                    Assert.That(piece.GetComponent<Collider>().enabled, Is.False, "Cliff colliders must be disabled.");
                }
                Assert.That(top, Is.EqualTo(y).Within(1e-3f), $"Cliff stack under {tile.Coord} must end at the tile.");
                Assert.That(bottom, Is.EqualTo(floor).Within(1e-3f), $"Cliff stack under {tile.Coord} must reach the lowest neighbor.");
            }

            Assert.That(tilesWithCliffs, Is.GreaterThan(0), "No cliffs built; test is vacuous.");
            Assert.That(multiPieceStacks, Is.GreaterThan(0), "No multi-piece stacks; stacking is untested.");
            renderer.Clear();
        }

        [Test]
        public void Render_WithoutCliffPrefab_BuildsNoCliffs()
        {
            var host = new GameObject("NoCliffHost");
            _created.Add(host);
            WorldRenderer renderer = host.AddComponent<WorldRenderer>();
            renderer.Render(Generate(Seeds[1]));

            foreach (Transform tileRoot in host.transform)
                foreach (Transform child in tileRoot)
                    Assert.That(child.name, Is.Not.EqualTo("Cliff"));
            renderer.Clear();
        }
    }
}
