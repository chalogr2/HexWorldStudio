using System.Collections;
using HexWorldStudio.Content;
using HexWorldStudio.Generation;
using HexWorldStudio.Rendering;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace HexWorldStudio.Tests.Runtime
{
    /// <summary>
    /// PlayMode tests for the runtime-control surface on
    /// <see cref="WorldGeneratorBootstrap"/> that the QuickStart
    /// regenerate overlay drives: <c>Regenerate(seed)</c>, <c>Seed</c>,
    /// <c>ContinentShapeAlgorithm</c>, and <c>LandRatio</c>.
    /// </summary>
    public class WorldGeneratorBootstrapPlayModeTests
    {
        private const int Width = 12;
        private const int Height = 8;

        private GameObject _host;
        private WorldGeneratorBootstrap _bootstrap;
        private BiomeDefinition[] _biomes;

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

        private static void WireField(MonoBehaviour target, string field, object value)
        {
            var fi = target.GetType().GetField(field,
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            Assert.That(fi, Is.Not.Null, $"Field {field} not found on {target.GetType().Name}.");
            fi.SetValue(target, value);
        }

        [SetUp]
        public void SetUp()
        {
            _biomes = new[]
            {
                NewBiome("BOcean", isWater: true),
                NewBiome("BCoast", isWater: true, isCoastalWater: true),
                NewBiome("BLand"),
            };

            _host = new GameObject("BootstrapHost");
            _host.SetActive(false); // keep Start from generating before wiring
            WorldRenderer renderer = _host.AddComponent<WorldRenderer>();
            _bootstrap = _host.AddComponent<WorldGeneratorBootstrap>();
            WireField(_bootstrap, "_width", Width);
            WireField(_bootstrap, "_height", Height);
            WireField(_bootstrap, "_biomes", _biomes);
            WireField(_bootstrap, "_renderer", renderer);
            WireField(_bootstrap, "_generateOnStart", false);
            WireField(_bootstrap, "_riverDensity", 0f);
            _host.SetActive(true);
        }

        [TearDown]
        public void TearDown()
        {
            if (_host != null) Object.DestroyImmediate(_host);
            foreach (BiomeDefinition b in _biomes) Object.DestroyImmediate(b);
        }

        [UnityTest]
        public IEnumerator RegenerateWithSeed_UsesThatSeed_EvenWhenRandomizeIsOn()
        {
            WireField(_bootstrap, "_randomizeSeedOnRegenerate", true);

            _bootstrap.Regenerate(424242UL);
            yield return null;

            Assert.That(_bootstrap.Seed, Is.EqualTo(424242UL));
            Assert.That(_bootstrap.LatestMap, Is.Not.Null);
            Assert.That(_bootstrap.LatestMap.Seed, Is.EqualTo(424242UL));
        }

        [UnityTest]
        public IEnumerator Regenerate_WithRandomizeOn_ReportsTheSeedItUsed()
        {
            WireField(_bootstrap, "_randomizeSeedOnRegenerate", true);

            _bootstrap.Regenerate();
            yield return null;

            Assert.That(_bootstrap.LatestMap.Seed, Is.EqualTo(_bootstrap.Seed),
                "Seed must reflect the world on screen so it can be written down and rebuilt.");
        }

        [Test]
        public void LandRatio_IsClampedToInspectorRange()
        {
            _bootstrap.LandRatio = 2f;
            Assert.That(_bootstrap.LandRatio, Is.EqualTo(0.95f).Within(1e-6f));
            _bootstrap.LandRatio = -1f;
            Assert.That(_bootstrap.LandRatio, Is.EqualTo(0.05f).Within(1e-6f));
        }

        [UnityTest]
        public IEnumerator ContinentShapeAlgorithm_AppliesOnNextRegenerate()
        {
            _bootstrap.ContinentShapeAlgorithm = ContinentShapeAlgorithm.Pangaea;
            _bootstrap.Regenerate(7UL);
            yield return null;

            // Same inputs straight through the generator must give the
            // same land layout the bootstrap produced.
            var expected = new HexWorldGenerator().Generate(new WorldGenerationSettings(
                Width, Height, 7UL, WorldGenerationSettings.DefaultLandRatio, _biomes,
                WorldGenerationSettings.DefaultBiomeCohesion, WorldGenerationSettings.DefaultFeatureCohesion,
                ContinentShapeAlgorithm.Pangaea,
                null, WorldGenerationSettings.DefaultResourceDensity,
                0f, WorldGenerationSettings.DefaultRiverSourceMinElevation, WorldGenerationSettings.DefaultMaxRiverLength,
                WorldGenerationSettings.DefaultMinRiverLength, WorldGenerationSettings.DefaultMaxWaterfallDrop,
                WorldGenerationSettings.DefaultLakeDepthThreshold));

            for (int i = 0; i < expected.Tiles.Count; i++)
            {
                Assert.That(_bootstrap.LatestMap.Tiles[i].LandState, Is.EqualTo(expected.Tiles[i].LandState),
                    $"Tile {i} differs from a direct Pangaea generation.");
            }
        }
    }
}
