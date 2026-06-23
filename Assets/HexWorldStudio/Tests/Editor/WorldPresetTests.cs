using HexWorldStudio.Content;
using HexWorldStudio.Generation;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

namespace HexWorldStudio.Tests
{
    /// <summary>
    /// EditMode tests for <see cref="WorldPreset"/> — defaults match
    /// the <see cref="WorldGenerationSettings"/> constants, and
    /// <see cref="WorldPreset.ToSettings"/> round-trips every field
    /// faithfully along with the supplied seed.
    /// </summary>
    public class WorldPresetTests
    {
        [Test]
        public void Defaults_MatchSettingsConstants()
        {
            // A freshly created preset should land on the same default
            // values WorldGenerationSettings exposes, so the asset is
            // runnable as soon as a biome roster is wired in.
            WorldPreset preset = ScriptableObject.CreateInstance<WorldPreset>();
            try
            {
                Assert.That(preset.Width, Is.EqualTo(32), "Default Width.");
                Assert.That(preset.Height, Is.EqualTo(20), "Default Height.");
                Assert.That(preset.LandRatio, Is.EqualTo(WorldGenerationSettings.DefaultLandRatio));
                Assert.That(preset.ContinentShapeAlgorithm, Is.EqualTo(WorldGenerationSettings.DefaultContinentShape));
                Assert.That(preset.BiomeCohesion, Is.EqualTo(WorldGenerationSettings.DefaultBiomeCohesion));
                Assert.That(preset.FeatureCohesion, Is.EqualTo(WorldGenerationSettings.DefaultFeatureCohesion));
            }
            finally
            {
                Object.DestroyImmediate(preset);
            }
        }

        [Test]
        public void ToSettings_RoundTripsEveryField()
        {
            WorldPreset preset = ScriptableObject.CreateInstance<WorldPreset>();
            BiomeDefinition ocean = ScriptableObject.CreateInstance<BiomeDefinition>();
            BiomeDefinition coast = ScriptableObject.CreateInstance<BiomeDefinition>();
            BiomeDefinition land = ScriptableObject.CreateInstance<BiomeDefinition>();
            try
            {
                preset.Width = 64;
                preset.Height = 48;
                preset.LandRatio = 0.55f;
                preset.ContinentShapeAlgorithm = ContinentShapeAlgorithm.Pangaea;
                preset.BiomeCohesion = 0.8f;
                preset.FeatureCohesion = 0.15f;
                preset.Biomes = new[] { ocean, coast, land };

                const ulong Seed = 0x4242BADC0FFEEuL;
                WorldGenerationSettings settings = preset.ToSettings(Seed);

                Assert.That(settings.Width, Is.EqualTo(64));
                Assert.That(settings.Height, Is.EqualTo(48));
                Assert.That(settings.Seed, Is.EqualTo(Seed),
                    "The seed argument must pass through unchanged — the preset itself never carries a seed.");
                Assert.That(settings.LandRatio, Is.EqualTo(0.55f).Within(1e-6f));
                Assert.That(settings.ContinentShapeAlgorithm, Is.EqualTo(ContinentShapeAlgorithm.Pangaea));
                Assert.That(settings.BiomeCohesion, Is.EqualTo(0.8f).Within(1e-6f));
                Assert.That(settings.FeatureCohesion, Is.EqualTo(0.15f).Within(1e-6f));
                Assert.That(settings.Biomes, Is.EqualTo(preset.Biomes),
                    "Settings.Biomes must be the same reference the preset holds — buyers expect to mutate the biome roster live during authoring.");
            }
            finally
            {
                Object.DestroyImmediate(ocean);
                Object.DestroyImmediate(coast);
                Object.DestroyImmediate(land);
                Object.DestroyImmediate(preset);
            }
        }

        [Test]
        public void ToSettings_DifferentSeedsProduceIndependentSettings()
        {
            // The preset is a recipe; the seed picks the instance.
            // Two calls with different seeds must yield settings with
            // different Seed values but otherwise identical content.
            WorldPreset preset = ScriptableObject.CreateInstance<WorldPreset>();
            try
            {
                preset.LandRatio = 0.42f;
                preset.ContinentShapeAlgorithm = ContinentShapeAlgorithm.Archipelago;

                WorldGenerationSettings a = preset.ToSettings(seed: 1uL);
                WorldGenerationSettings b = preset.ToSettings(seed: 2uL);

                Assert.That(a.Seed, Is.Not.EqualTo(b.Seed));
                Assert.That(a.LandRatio, Is.EqualTo(b.LandRatio));
                Assert.That(a.ContinentShapeAlgorithm, Is.EqualTo(b.ContinentShapeAlgorithm));
            }
            finally
            {
                Object.DestroyImmediate(preset);
            }
        }
    }
}
