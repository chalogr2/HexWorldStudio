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
    /// EditMode tests for <see cref="WorldWrapRenderer"/> — copy
    /// container lifecycle, copy positions, copy tile count,
    /// idempotency, WorldToCoord wrap math.
    /// </summary>
    public class WorldWrapRendererTests
    {
        private const int Width = 8;
        private const int Height = 6;
        private const ulong Seed = 0x77AABBCC11223344UL;

        // ---- biome helpers (copy of the minimal set used elsewhere) ----

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
            BiomeDefinition ocean = NewBiome("WrapOcean", new Color(0.1f, 0.25f, 0.5f), isWater: true);
            BiomeDefinition coast = NewBiome("WrapCoast", new Color(0.2f, 0.5f, 0.7f), isWater: true, isCoastalWater: true);
            BiomeDefinition land  = NewBiome("WrapLand",  new Color(0.4f, 0.6f, 0.3f));
            held = new[] { ocean, coast, land };
            return held;
        }

        private static (GameObject host, WorldRenderer renderer, WorldWrapRenderer wrap, WorldMap map, BiomeDefinition[] biomes)
            NewRenderedScene()
        {
            var host = new GameObject("WrapHost");
            var renderer = host.AddComponent<WorldRenderer>();
            var wrap = host.AddComponent<WorldWrapRenderer>();
            wrap.Renderer = renderer;
            wrap.WorldTileWidth = Width;

            IReadOnlyList<BiomeDefinition> biomes = MinimalBiomeSet(out BiomeDefinition[] held);
            var settings = new WorldGenerationSettings(Width, Height, Seed,
                WorldGenerationSettings.DefaultLandRatio, biomes);
            WorldMap map = new HexWorldGenerator().Generate(settings);

            renderer.Render(map);
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

        // ---- tests ----

        [Test]
        public void Refresh_CreatesTwoCopyContainers()
        {
            var (host, _, wrap, _, biomes) = NewRenderedScene();
            try
            {
                wrap.Refresh();

                Transform left = host.transform.Find("WrapCopyLeft");
                Transform right = host.transform.Find("WrapCopyRight");
                Assert.That(left, Is.Not.Null, "Expected WrapCopyLeft child.");
                Assert.That(right, Is.Not.Null, "Expected WrapCopyRight child.");
            }
            finally
            {
                Teardown(host, biomes);
            }
        }

        [Test]
        public void Refresh_LeftCopyPositionedAtMinusWorldWidth()
        {
            var (host, _, wrap, _, biomes) = NewRenderedScene();
            try
            {
                wrap.Refresh();
                Transform left = host.transform.Find("WrapCopyLeft");
                float expected = -Width * HexMesh.HorizontalSpacing;
                Assert.That(left.localPosition.x, Is.EqualTo(expected).Within(1e-4f));
            }
            finally
            {
                Teardown(host, biomes);
            }
        }

        [Test]
        public void Refresh_RightCopyPositionedAtPlusWorldWidth()
        {
            var (host, _, wrap, _, biomes) = NewRenderedScene();
            try
            {
                wrap.Refresh();
                Transform right = host.transform.Find("WrapCopyRight");
                float expected = +Width * HexMesh.HorizontalSpacing;
                Assert.That(right.localPosition.x, Is.EqualTo(expected).Within(1e-4f));
            }
            finally
            {
                Teardown(host, biomes);
            }
        }

        [Test]
        public void Refresh_CopyTileCountEqualsCanonicalTileCount()
        {
            var (host, renderer, wrap, _, biomes) = NewRenderedScene();
            try
            {
                wrap.Refresh();
                int canonical = renderer.TileCount;
                int leftCount = host.transform.Find("WrapCopyLeft").childCount;
                int rightCount = host.transform.Find("WrapCopyRight").childCount;

                Assert.That(leftCount, Is.EqualTo(canonical));
                Assert.That(rightCount, Is.EqualTo(canonical));
            }
            finally
            {
                Teardown(host, biomes);
            }
        }

        [Test]
        public void Refresh_CalledTwice_DoesNotDuplicateCopies()
        {
            var (host, _, wrap, _, biomes) = NewRenderedScene();
            try
            {
                wrap.Refresh();
                wrap.Refresh();

                int leftCount = 0, rightCount = 0;
                for (int i = 0; i < host.transform.childCount; i++)
                {
                    string name = host.transform.GetChild(i).name;
                    if (name == "WrapCopyLeft") leftCount++;
                    else if (name == "WrapCopyRight") rightCount++;
                }
                Assert.That(leftCount, Is.EqualTo(1), "Multiple WrapCopyLeft children — Refresh leaked.");
                Assert.That(rightCount, Is.EqualTo(1), "Multiple WrapCopyRight children — Refresh leaked.");
            }
            finally
            {
                Teardown(host, biomes);
            }
        }

        [Test]
        public void Clear_DestroysCopyContainers()
        {
            var (host, _, wrap, _, biomes) = NewRenderedScene();
            try
            {
                wrap.Refresh();
                wrap.Clear();

                Assert.That(host.transform.Find("WrapCopyLeft"), Is.Null);
                Assert.That(host.transform.Find("WrapCopyRight"), Is.Null);
            }
            finally
            {
                Teardown(host, biomes);
            }
        }

        [Test]
        public void WorldWidthInWorldUnits_EqualsTileWidthTimesHorizontalSpacing()
        {
            var (host, _, wrap, _, biomes) = NewRenderedScene();
            try
            {
                float expected = Width * HexMesh.HorizontalSpacing;
                Assert.That(wrap.WorldWidthInWorldUnits, Is.EqualTo(expected).Within(1e-5f));
            }
            finally
            {
                Teardown(host, biomes);
            }
        }

        [Test]
        public void WorldToCoord_AtCanonicalTileCenter_ReturnsThatTilesCoord()
        {
            var (host, _, wrap, map, biomes) = NewRenderedScene();
            try
            {
                // Pick a known tile away from edges to avoid boundary
                // rounding subtleties, compute its world position, and
                // verify WorldToCoord round-trips.
                HexCoord coord = HexCoordExtensions.FromOffsetOddR(3, 2);
                Vector3 worldPos = HexMesh.WorldPosition(3, 2, 0, 0.15f);

                HexCoord recovered = wrap.WorldToCoord(worldPos, map);
                Assert.That(recovered, Is.EqualTo(coord));
            }
            finally
            {
                Teardown(host, biomes);
            }
        }

        [Test]
        public void WorldToCoord_OnLeftCopy_NormalizesToCanonicalCoord()
        {
            var (host, _, wrap, map, biomes) = NewRenderedScene();
            try
            {
                // Take a canonical tile at (col=1, row=2) and shift its
                // X by -WorldWidthInWorldUnits — that's where the left
                // wrap copy would render it. WorldToCoord should still
                // resolve to the canonical (1, 2).
                Vector3 canonicalPos = HexMesh.WorldPosition(1, 2, 0, 0.15f);
                Vector3 onLeftCopy = canonicalPos + new Vector3(-wrap.WorldWidthInWorldUnits, 0, 0);

                HexCoord recovered = wrap.WorldToCoord(onLeftCopy, map);
                HexCoord expected = HexCoordExtensions.FromOffsetOddR(1, 2);
                Assert.That(recovered, Is.EqualTo(expected));
            }
            finally
            {
                Teardown(host, biomes);
            }
        }
    }
}
