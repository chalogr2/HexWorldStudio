using System.Collections;
using System.Collections.Generic;
using HexWorldStudio.Content;
using HexWorldStudio.Coordinates;
using HexWorldStudio.Data;
using HexWorldStudio.Generation;
using HexWorldStudio.Rendering;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace HexWorldStudio.Tests.Runtime
{
    /// <summary>
    /// PlayMode tests for <see cref="WorldRenderer"/> — first PlayMode
    /// tests of the project. Verifies the end-to-end "WorldMap →
    /// scene-graph" path: one GameObject per tile, hex-layout
    /// positions, ElevationLevel-driven Y, biome color flow into the
    /// MaterialPropertyBlock.
    /// </summary>
    public class WorldRendererPlayModeTests
    {
        private const int Width = 8;
        private const int Height = 6;
        private const ulong Seed = 0xFACEFEEDFACEFEEDUL;

        // ---- biome helpers ----

        private static BiomeDefinition NewBiome(string name, Color color,
            bool isWater = false, bool isCoastalWater = false)
        {
            var b = ScriptableObject.CreateInstance<BiomeDefinition>();
            b.BiomeName = name;
            b.Color = color;
            b.IsWater = isWater;
            b.IsCoastalWater = isCoastalWater;
            b.MinTemperature = 0f;
            b.MaxTemperature = 1f;
            b.MinMoisture = 0f;
            b.MaxMoisture = 1f;
            return b;
        }

        private static IReadOnlyList<BiomeDefinition> MinimalBiomeSet(
            out BiomeDefinition ocean, out BiomeDefinition coast, out BiomeDefinition land)
        {
            ocean = NewBiome("PlayOcean", new Color(0.10f, 0.25f, 0.48f),
                isWater: true, isCoastalWater: false);
            coast = NewBiome("PlayCoast", new Color(0.23f, 0.49f, 0.72f),
                isWater: true, isCoastalWater: true);
            land = NewBiome("PlayLand", new Color(0.44f, 0.66f, 0.36f));
            return new[] { ocean, coast, land };
        }

        private static GameObject NewRendererHost(out WorldRenderer renderer)
        {
            var go = new GameObject("TestRenderer");
            renderer = go.AddComponent<WorldRenderer>();
            renderer.ElevationStep = 0.15f;
            renderer.TileWorldScale = 1f;
            return go;
        }

        // ---- tests ----

        [UnityTest]
        public IEnumerator Render_GeneratedMap_ProducesOneGameObjectPerTile()
        {
            BiomeDefinition[] biomes = (BiomeDefinition[])MinimalBiomeSet(out _, out _, out _);
            GameObject host = NewRendererHost(out WorldRenderer renderer);
            try
            {
                var settings = new WorldGenerationSettings(Width, Height, Seed,
                    WorldGenerationSettings.DefaultLandRatio, biomes);
                WorldMap map = new HexWorldGenerator().Generate(settings);

                renderer.Render(map);
                yield return null;

                Assert.That(renderer.TileCount, Is.EqualTo(Width * Height));
                Assert.That(host.transform.childCount, Is.EqualTo(Width * Height));
            }
            finally
            {
                Object.DestroyImmediate(host);
                foreach (BiomeDefinition b in biomes) Object.DestroyImmediate(b);
            }
        }

        [UnityTest]
        public IEnumerator Render_TileWorldPositions_MatchHexLayout()
        {
            BiomeDefinition[] biomes = (BiomeDefinition[])MinimalBiomeSet(out _, out _, out _);
            GameObject host = NewRendererHost(out WorldRenderer renderer);
            try
            {
                var settings = new WorldGenerationSettings(Width, Height, Seed,
                    WorldGenerationSettings.DefaultLandRatio, biomes);
                WorldMap map = new HexWorldGenerator().Generate(settings);
                renderer.Render(map);
                yield return null;

                // Spot-check three tiles' local positions against HexMesh
                // expectations.
                int[] cols = { 0, 5, 7 };
                int[] rows = { 0, 1, 4 };
                for (int i = 0; i < cols.Length; i++)
                {
                    int col = cols[i];
                    int row = rows[i];
                    HexCoord c = HexCoordExtensions.FromOffsetOddR(col, row);
                    map.TryGetTile(c, out HexTile tile);

                    Vector3 expected = HexMesh.WorldPosition(col, row, tile.ElevationLevel, 0.15f);
                    Transform child = FindChildByCoord(host.transform, col, row);
                    Assert.That(child, Is.Not.Null, $"Child for ({col}, {row}) not found.");
                    Assert.That(child.localPosition.x, Is.EqualTo(expected.x).Within(1e-4f));
                    Assert.That(child.localPosition.y, Is.EqualTo(expected.y).Within(1e-4f));
                    Assert.That(child.localPosition.z, Is.EqualTo(expected.z).Within(1e-4f));
                }
            }
            finally
            {
                Object.DestroyImmediate(host);
                foreach (BiomeDefinition b in biomes) Object.DestroyImmediate(b);
            }
        }

        [UnityTest]
        public IEnumerator Render_TileYReflectsElevationLevel()
        {
            BiomeDefinition[] biomes = (BiomeDefinition[])MinimalBiomeSet(out _, out _, out _);
            GameObject host = NewRendererHost(out WorldRenderer renderer);
            try
            {
                var settings = new WorldGenerationSettings(Width, Height, Seed,
                    WorldGenerationSettings.DefaultLandRatio, biomes);
                WorldMap map = new HexWorldGenerator().Generate(settings);
                renderer.Render(map);
                yield return null;

                foreach (HexTile tile in map.Tiles)
                {
                    (int col, int row) = tile.Coord.ToOffsetOddR();
                    Transform child = FindChildByCoord(host.transform, col, row);
                    float expectedY = tile.ElevationLevel * 0.15f;
                    Assert.That(child.localPosition.y, Is.EqualTo(expectedY).Within(1e-4f),
                        $"Tile ({col}, {row}) level={tile.ElevationLevel} produced Y={child.localPosition.y}.");
                }
            }
            finally
            {
                Object.DestroyImmediate(host);
                foreach (BiomeDefinition b in biomes) Object.DestroyImmediate(b);
            }
        }

        [UnityTest]
        public IEnumerator Render_TileColor_ReflectsBiomeColor()
        {
            BiomeDefinition[] biomes = (BiomeDefinition[])MinimalBiomeSet(
                out BiomeDefinition ocean, out BiomeDefinition coast, out BiomeDefinition land);
            GameObject host = NewRendererHost(out WorldRenderer renderer);
            try
            {
                var settings = new WorldGenerationSettings(Width, Height, Seed,
                    WorldGenerationSettings.DefaultLandRatio, biomes);
                WorldMap map = new HexWorldGenerator().Generate(settings);
                renderer.Render(map);
                yield return null;

                var block = new MaterialPropertyBlock();
                int propertyId = Shader.PropertyToID("_BaseColor");

                foreach (HexTile tile in map.Tiles)
                {
                    if (tile.Biome == null) continue;
                    (int col, int row) = tile.Coord.ToOffsetOddR();
                    Transform child = FindChildByCoord(host.transform, col, row);
                    var meshRenderer = child.GetComponent<MeshRenderer>();
                    Assert.That(meshRenderer, Is.Not.Null);
                    meshRenderer.GetPropertyBlock(block);
                    Color actual = block.GetColor(propertyId);

                    Assert.That(actual.r, Is.EqualTo(tile.Biome.Color.r).Within(1e-3f));
                    Assert.That(actual.g, Is.EqualTo(tile.Biome.Color.g).Within(1e-3f));
                    Assert.That(actual.b, Is.EqualTo(tile.Biome.Color.b).Within(1e-3f));
                    break; // one tile is enough for the color path; saves time
                }
            }
            finally
            {
                Object.DestroyImmediate(host);
                foreach (BiomeDefinition b in biomes) Object.DestroyImmediate(b);
            }
        }

        [UnityTest]
        public IEnumerator Render_CalledTwice_ReplacesPreviousTilesWithoutDoubling()
        {
            BiomeDefinition[] biomes = (BiomeDefinition[])MinimalBiomeSet(out _, out _, out _);
            GameObject host = NewRendererHost(out WorldRenderer renderer);
            try
            {
                var settings = new WorldGenerationSettings(Width, Height, Seed,
                    WorldGenerationSettings.DefaultLandRatio, biomes);
                WorldMap map = new HexWorldGenerator().Generate(settings);

                renderer.Render(map);
                yield return null;
                int firstCount = renderer.TileCount;

                renderer.Render(map);
                yield return null;
                int secondCount = renderer.TileCount;

                Assert.That(firstCount, Is.EqualTo(Width * Height));
                Assert.That(secondCount, Is.EqualTo(Width * Height),
                    "Second Render call did not replace the previous tiles; counts indicate doubling or leak.");
            }
            finally
            {
                Object.DestroyImmediate(host);
                foreach (BiomeDefinition b in biomes) Object.DestroyImmediate(b);
            }
        }

        [Test]
        public void Render_NullMap_Throws()
        {
            GameObject host = NewRendererHost(out WorldRenderer renderer);
            try
            {
                Assert.Throws<System.ArgumentNullException>(() => renderer.Render(null));
            }
            finally
            {
                Object.DestroyImmediate(host);
            }
        }

        [UnityTest]
        public IEnumerator Render_EveryTile_HasHexTileBindingWithMatchingCoord()
        {
            BiomeDefinition[] biomes = (BiomeDefinition[])MinimalBiomeSet(out _, out _, out _);
            GameObject host = NewRendererHost(out WorldRenderer renderer);
            try
            {
                var settings = new WorldGenerationSettings(Width, Height, Seed,
                    WorldGenerationSettings.DefaultLandRatio, biomes);
                WorldMap map = new HexWorldGenerator().Generate(settings);
                renderer.Render(map);
                yield return null;

                foreach (HexTile tile in map.Tiles)
                {
                    (int col, int row) = tile.Coord.ToOffsetOddR();
                    Transform child = FindChildByCoord(host.transform, col, row);
                    var binding = child.GetComponent<HexWorldStudio.Rendering.HexTileBinding>();
                    Assert.That(binding, Is.Not.Null,
                        $"Tile ({col},{row}) missing HexTileBinding.");
                    Assert.That(binding.Coord, Is.EqualTo(tile.Coord),
                        $"Tile ({col},{row}) HexTileBinding.Coord != HexTile.Coord.");
                }
            }
            finally
            {
                Object.DestroyImmediate(host);
                foreach (BiomeDefinition b in biomes) Object.DestroyImmediate(b);
            }
        }

        [UnityTest]
        public IEnumerator Render_TileColor_PrefersFeatureColorOverBiomeColor()
        {
            BiomeDefinition[] biomes = (BiomeDefinition[])MinimalBiomeSet(
                out _, out _, out BiomeDefinition land);

            // Author a feature with a deliberately distinct color
            // (hot pink, unlike the land biome color) so we can verify
            // the feature color wins through to the property block.
            var feature = ScriptableObject.CreateInstance<TileFeatureDefinition>();
            feature.FeatureName = "PlayFeature";
            feature.Color = new Color(0.95f, 0.10f, 0.85f, 1f);
            feature.PreferredElevationLevel = 0;
            feature.Weight = 1f;
            land.AllowedFeatures = new[] { feature };

            GameObject host = NewRendererHost(out WorldRenderer renderer);
            try
            {
                var settings = new WorldGenerationSettings(Width, Height, Seed,
                    WorldGenerationSettings.DefaultLandRatio, biomes);
                WorldMap map = new HexWorldGenerator().Generate(settings);
                renderer.Render(map);
                yield return null;

                var block = new MaterialPropertyBlock();
                int propertyId = Shader.PropertyToID("_BaseColor");

                foreach (HexTile tile in map.Tiles)
                {
                    if (tile.LandState != TileLandState.Land) continue;
                    Assert.That(tile.Feature, Is.SameAs(feature),
                        "Pre-condition: land tile should have the feature applied.");

                    (int col, int row) = tile.Coord.ToOffsetOddR();
                    Transform child = FindChildByCoord(host.transform, col, row);
                    child.GetComponent<MeshRenderer>().GetPropertyBlock(block);
                    Color actual = block.GetColor(propertyId);

                    Assert.That(actual.r, Is.EqualTo(feature.Color.r).Within(1e-3f));
                    Assert.That(actual.g, Is.EqualTo(feature.Color.g).Within(1e-3f));
                    Assert.That(actual.b, Is.EqualTo(feature.Color.b).Within(1e-3f));
                    break;
                }
            }
            finally
            {
                Object.DestroyImmediate(host);
                Object.DestroyImmediate(feature);
                foreach (BiomeDefinition b in biomes) Object.DestroyImmediate(b);
            }
        }

        // ---- helpers ----

        private static Transform FindChildByCoord(Transform parent, int col, int row)
        {
            string expected = $"Tile_{col}_{row}";
            for (int i = 0; i < parent.childCount; i++)
            {
                Transform child = parent.GetChild(i);
                if (child.name == expected) return child;
            }
            return null;
        }
    }
}
