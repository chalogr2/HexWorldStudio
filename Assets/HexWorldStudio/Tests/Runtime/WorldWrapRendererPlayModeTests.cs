using System.Collections;
using System.Collections.Generic;
using HexWorldStudio.Content;
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
    /// PlayMode tests for <see cref="WorldWrapRenderer"/> — copy
    /// per-tile colors match canonical, copies stay synced across
    /// re-renders, copy colliders are hittable by raycast.
    /// </summary>
    public class WorldWrapRendererPlayModeTests
    {
        private const int Width = 6;
        private const int Height = 4;
        private const ulong Seed = 0x33667799CCFF0000UL;

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

        private static IReadOnlyList<BiomeDefinition> MinimalBiomeSet(out BiomeDefinition[] held)
        {
            BiomeDefinition ocean = NewBiome("WrapPlayOcean", new Color(0.10f, 0.25f, 0.5f), isWater: true);
            BiomeDefinition coast = NewBiome("WrapPlayCoast", new Color(0.23f, 0.49f, 0.72f),
                isWater: true, isCoastalWater: true);
            BiomeDefinition land = NewBiome("WrapPlayLand",  new Color(0.44f, 0.66f, 0.36f));
            held = new[] { ocean, coast, land };
            return held;
        }

        private static (GameObject host, WorldRenderer renderer, WorldWrapRenderer wrap, WorldMap map, BiomeDefinition[] biomes)
            BuildScene()
        {
            var host = new GameObject("WrapPlayHost");
            var renderer = host.AddComponent<WorldRenderer>();
            renderer.ElevationStep = 0.15f;
            var wrap = host.AddComponent<WorldWrapRenderer>();
            wrap.Renderer = renderer;
            wrap.WorldTileWidth = Width;

            IReadOnlyList<BiomeDefinition> biomes = MinimalBiomeSet(out BiomeDefinition[] held);
            var settings = new WorldGenerationSettings(Width, Height, Seed,
                WorldGenerationSettings.DefaultLandRatio, biomes);
            WorldMap map = new HexWorldGenerator().Generate(settings);
            renderer.Render(map);
            wrap.Refresh();

            return (host, renderer, wrap, map, held);
        }

        private static void Teardown(GameObject host, BiomeDefinition[] biomes)
        {
            Object.DestroyImmediate(host);
            if (biomes != null)
            {
                foreach (BiomeDefinition b in biomes) Object.DestroyImmediate(b);
            }
        }

        [UnityTest]
        public IEnumerator Refresh_CopiesReceiveSameMaterialColorsAsCanonical()
        {
            var (host, _, _, map, biomes) = BuildScene();
            yield return null;
            try
            {
                Transform canonical = host.transform;
                Transform left = host.transform.Find("WrapCopyLeft");
                Assert.That(left, Is.Not.Null);

                var block = new MaterialPropertyBlock();
                int colorProp = Shader.PropertyToID("_BaseColor");

                // Compare property block colors for one tile of each
                // status by name. The left copy's child name matches
                // the canonical child's name.
                int compared = 0;
                for (int i = 0; i < canonical.childCount && compared < 5; i++)
                {
                    Transform canonChild = canonical.GetChild(i);
                    if (canonChild.name.StartsWith("Wrap")) continue; // skip the copy containers

                    Transform copyChild = left.Find(canonChild.name);
                    Assert.That(copyChild, Is.Not.Null,
                        $"Copy missing tile for {canonChild.name}.");

                    var canonMR = canonChild.GetComponent<MeshRenderer>();
                    var copyMR = copyChild.GetComponent<MeshRenderer>();
                    canonMR.GetPropertyBlock(block);
                    Color canonColor = block.GetColor(colorProp);
                    copyMR.GetPropertyBlock(block);
                    Color copyColor = block.GetColor(colorProp);

                    Assert.That(copyColor.r, Is.EqualTo(canonColor.r).Within(1e-3f));
                    Assert.That(copyColor.g, Is.EqualTo(canonColor.g).Within(1e-3f));
                    Assert.That(copyColor.b, Is.EqualTo(canonColor.b).Within(1e-3f));
                    compared++;
                }
                Assert.That(compared, Is.GreaterThan(0), "No tiles compared.");
            }
            finally
            {
                Teardown(host, biomes);
            }
        }

        [UnityTest]
        public IEnumerator Refresh_AfterUnderlyingRerender_CopiesStaySynced()
        {
            var (host, renderer, wrap, map, biomes) = BuildScene();
            yield return null;
            try
            {
                // Re-render with the same settings; counts should
                // still match across canonical + copies.
                renderer.Render(map);
                wrap.Refresh();
                yield return null;

                int canonical = renderer.TileCount;
                Transform left = host.transform.Find("WrapCopyLeft");
                Transform right = host.transform.Find("WrapCopyRight");
                Assert.That(left.childCount, Is.EqualTo(canonical));
                Assert.That(right.childCount, Is.EqualTo(canonical));
            }
            finally
            {
                Teardown(host, biomes);
            }
        }

        [UnityTest]
        public IEnumerator Refresh_CopyTileColliders_AreHittableByRaycast()
        {
            var (host, _, _, _, biomes) = BuildScene();
            yield return null;
            try
            {
                Transform right = host.transform.Find("WrapCopyRight");
                Transform anyCopyTile = null;
                for (int i = 0; i < right.childCount; i++)
                {
                    Transform child = right.GetChild(i);
                    var collider = child.GetComponent<MeshCollider>();
                    if (collider != null && collider.sharedMesh != null)
                    {
                        anyCopyTile = child;
                        break;
                    }
                }
                Assert.That(anyCopyTile, Is.Not.Null,
                    "Expected at least one copy tile with a MeshCollider whose sharedMesh is set.");

                // Force-sync the physics world so the just-instantiated
                // colliders are visible to Raycast queries in this same
                // frame (Unity does this automatically on FixedUpdate,
                // but PlayMode tests query before then).
                Physics.SyncTransforms();

                // Cast straight down onto the tile from above. Use a
                // slightly-forward offset (toward +X) so the ray hits a
                // ring vertex's interior rather than the exact center
                // vertex where flat-mesh raycast precision is weakest.
                Vector3 origin = anyCopyTile.position + new Vector3(0.2f, 5f, 0.2f);
                bool hit = Physics.Raycast(origin, Vector3.down, out RaycastHit info, 20f);

                Assert.That(hit, Is.True, "Raycast missed all copy tiles.");
                Assert.That(info.collider.gameObject, Is.SameAs(anyCopyTile.gameObject),
                    "Raycast hit a different GameObject than the targeted copy tile.");
            }
            finally
            {
                Teardown(host, biomes);
            }
        }
    }
}
