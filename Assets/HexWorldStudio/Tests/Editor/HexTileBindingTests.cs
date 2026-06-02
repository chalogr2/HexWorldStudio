using HexWorldStudio.Coordinates;
using HexWorldStudio.Rendering;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

namespace HexWorldStudio.Tests
{
    /// <summary>
    /// EditMode tests for <see cref="HexTileBinding"/> — default Coord
    /// value and field round-trip.
    /// </summary>
    public class HexTileBindingTests
    {
        [Test]
        public void Coord_DefaultsToZero()
        {
            var go = new GameObject("BindingHost");
            try
            {
                var binding = go.AddComponent<HexTileBinding>();
                Assert.That(binding.Coord, Is.EqualTo(HexCoord.Zero));
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void Coord_RoundTripsThroughAssignment()
        {
            var go = new GameObject("BindingHost");
            try
            {
                var binding = go.AddComponent<HexTileBinding>();
                binding.Coord = new HexCoord(5, -3);
                Assert.That(binding.Coord, Is.EqualTo(new HexCoord(5, -3)));
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }
    }
}
