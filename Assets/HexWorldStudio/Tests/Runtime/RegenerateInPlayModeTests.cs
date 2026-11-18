using System.Collections;
using HexWorldStudio.Content;
using HexWorldStudio.Rendering;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace HexWorldStudio.Tests.Runtime
{
    /// <summary>
    /// Regression tests that must run in real Play mode. The rest of the
    /// suite runs in Edit mode, where renderers use DestroyImmediate, so
    /// it cannot see bugs caused by Destroy being deferred to the end of
    /// the frame.
    /// </summary>
    public class RegenerateInPlayModeTests
    {
        private const int Width = 10;
        private const int Height = 6;

        private static void WireField(MonoBehaviour target, string field, object value)
        {
            var fi = target.GetType().GetField(field,
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            Assert.That(fi, Is.Not.Null, $"Field {field} not found on {target.GetType().Name}.");
            fi.SetValue(target, value);
        }

        private static BiomeDefinition NewBiome(string name, bool isWater = false, bool isCoastalWater = false)
        {
            var b = ScriptableObject.CreateInstance<BiomeDefinition>();
            b.BiomeName = name;
            b.IsWater = isWater;
            b.IsCoastalWater = isCoastalWater;
            b.MinTemperature = 0f;
            b.MaxTemperature = 1f;
            b.MinMoisture = 0f;
            b.MaxMoisture = 1f;
            return b;
        }

        private static int ActiveTileCount(GameObject host) =>
            host.GetComponentsInChildren<HexTileBinding>(includeInactive: false).Length;

        [UnityTest]
        public IEnumerator RegenerateTwiceInOneFrame_WithWrap_LeavesExactlyOneWorld()
        {
            yield return new EnterPlayMode();

            var biomes = new[]
            {
                NewBiome("POcean", isWater: true),
                NewBiome("PCoast", isWater: true, isCoastalWater: true),
                NewBiome("PLand"),
            };

            var host = new GameObject("PlayModeRegenerateHost");
            host.SetActive(false);
            WorldRenderer renderer = host.AddComponent<WorldRenderer>();
            WorldWrapRenderer wrap = host.AddComponent<WorldWrapRenderer>();
            wrap.Renderer = renderer;
            WorldGeneratorBootstrap bootstrap = host.AddComponent<WorldGeneratorBootstrap>();
            WireField(bootstrap, "_width", Width);
            WireField(bootstrap, "_height", Height);
            WireField(bootstrap, "_biomes", biomes);
            WireField(bootstrap, "_renderer", renderer);
            WireField(bootstrap, "_wrapRenderer", wrap);
            WireField(bootstrap, "_riverDensity", 0f);
            WireField(bootstrap, "_generateOnStart", false);
            host.SetActive(true);

            try
            {
                Assert.That(Application.isPlaying, Is.True);

                // Canonical world plus the two wrap copies.
                int expected = Width * Height * 3;

                bootstrap.Regenerate(1UL);
                yield return null;
                Assert.That(ActiveTileCount(host), Is.EqualTo(expected), "After the first generate.");

                // Two regenerates in the same frame, so the first one's
                // Destroy calls have not been processed yet when the
                // second one (and its wrap refresh) runs.
                bootstrap.Regenerate(2UL);
                bootstrap.Regenerate(3UL);
                Assert.That(ActiveTileCount(host), Is.EqualTo(expected), "Same frame as the regenerates.");

                yield return null;
                Assert.That(ActiveTileCount(host), Is.EqualTo(expected), "One frame later.");
                Assert.That(host.transform.childCount, Is.EqualTo(Width * Height + 2),
                    "Host should hold the tiles plus WrapCopyLeft and WrapCopyRight, nothing else.");
            }
            finally
            {
                Object.Destroy(host);
                foreach (BiomeDefinition b in biomes) Object.Destroy(b);
            }

            yield return new ExitPlayMode();
        }
    }
}
