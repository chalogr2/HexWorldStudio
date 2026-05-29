using HexWorldStudio.Content;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

namespace HexWorldStudio.Tests
{
    /// <summary>
    /// EditMode tests for <see cref="TileFeatureDefinition"/> —
    /// inert default values, field round-trip, gameplay-metadata
    /// flags.
    /// </summary>
    public class TileFeatureDefinitionTests
    {
        [Test]
        public void Defaults_ProduceInertFeature()
        {
            var f = ScriptableObject.CreateInstance<TileFeatureDefinition>();
            try
            {
                Assert.That(f.FeatureName, Is.Null.Or.Empty);
                Assert.That(f.Color, Is.EqualTo(Color.magenta));
                Assert.That(f.PreferredElevationLevel, Is.EqualTo(1));
                Assert.That(f.Weight, Is.EqualTo(1f));
                Assert.That(f.IsImpassable, Is.False);
                Assert.That(f.MovementCostMultiplier, Is.EqualTo(1f));
            }
            finally
            {
                Object.DestroyImmediate(f);
            }
        }

        [Test]
        public void FieldsRoundTripUnderAssignment()
        {
            var f = ScriptableObject.CreateInstance<TileFeatureDefinition>();
            try
            {
                f.FeatureName = "TestForest";
                f.Color = new Color(0.2f, 0.6f, 0.3f, 1f);
                f.PreferredElevationLevel = 4;
                f.Weight = 2.5f;
                f.IsImpassable = true;
                f.MovementCostMultiplier = 2f;

                Assert.That(f.FeatureName, Is.EqualTo("TestForest"));
                Assert.That(f.Color.g, Is.EqualTo(0.6f).Within(1e-5f));
                Assert.That(f.PreferredElevationLevel, Is.EqualTo(4));
                Assert.That(f.Weight, Is.EqualTo(2.5f));
                Assert.That(f.IsImpassable, Is.True);
                Assert.That(f.MovementCostMultiplier, Is.EqualTo(2f));
            }
            finally
            {
                Object.DestroyImmediate(f);
            }
        }

        [Test]
        public void GameplayMetadataFlags_AreFreelySettable()
        {
            // Pins the contract that IsImpassable and
            // MovementCostMultiplier are buyer-facing metadata: the
            // generator never touches them, the SO never coerces them,
            // and any value is preserved.
            var f = ScriptableObject.CreateInstance<TileFeatureDefinition>();
            try
            {
                f.IsImpassable = true;
                f.MovementCostMultiplier = 0f; // pathological but allowed
                Assert.That(f.IsImpassable, Is.True);
                Assert.That(f.MovementCostMultiplier, Is.EqualTo(0f));

                f.IsImpassable = false;
                f.MovementCostMultiplier = 99f;
                Assert.That(f.IsImpassable, Is.False);
                Assert.That(f.MovementCostMultiplier, Is.EqualTo(99f));
            }
            finally
            {
                Object.DestroyImmediate(f);
            }
        }
    }
}
