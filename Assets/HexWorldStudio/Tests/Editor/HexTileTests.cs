using HexWorldStudio.Coordinates;
using HexWorldStudio.Data;
using NUnit.Framework;

namespace HexWorldStudio.Tests
{
    /// <summary>
    /// EditMode tests for <see cref="HexTile"/> — construction, NaN
    /// defaults, and internal-setter mutation paths.
    /// </summary>
    public class HexTileTests
    {
        [Test]
        public void Constructor_StoresCoordAndIndex()
        {
            var coord = new HexCoord(3, 4);
            var tile = new HexTile(coord, 42);
            Assert.That(tile.Coord, Is.EqualTo(coord));
            Assert.That(tile.TileIndex, Is.EqualTo(42));
        }

        [Test]
        public void Elevation_DefaultIsNaN()
        {
            var tile = new HexTile(HexCoord.Zero, 0);
            Assert.That(float.IsNaN(tile.Elevation), Is.True);
        }

        [Test]
        public void Temperature_DefaultIsNaN()
        {
            var tile = new HexTile(HexCoord.Zero, 0);
            Assert.That(float.IsNaN(tile.Temperature), Is.True);
        }

        [Test]
        public void Moisture_DefaultIsNaN()
        {
            var tile = new HexTile(HexCoord.Zero, 0);
            Assert.That(float.IsNaN(tile.Moisture), Is.True);
        }

        [Test]
        public void LandState_DefaultIsUnset()
        {
            var tile = new HexTile(HexCoord.Zero, 0);
            Assert.That(tile.LandState, Is.EqualTo(TileLandState.Unset));
        }

        [Test]
        public void Elevation_CanBeSetViaInternalSetter()
        {
            var tile = new HexTile(HexCoord.Zero, 0);
            tile.Elevation = 0.5f;
            Assert.That(tile.Elevation, Is.EqualTo(0.5f));
        }

        [Test]
        public void Temperature_CanBeSetViaInternalSetter()
        {
            var tile = new HexTile(HexCoord.Zero, 0);
            tile.Temperature = 0.25f;
            Assert.That(tile.Temperature, Is.EqualTo(0.25f));
        }

        [Test]
        public void Moisture_CanBeSetViaInternalSetter()
        {
            var tile = new HexTile(HexCoord.Zero, 0);
            tile.Moisture = 0.75f;
            Assert.That(tile.Moisture, Is.EqualTo(0.75f));
        }

        [Test]
        public void LandState_CanBeSetViaInternalSetter()
        {
            var tile = new HexTile(HexCoord.Zero, 0);
            tile.LandState = TileLandState.Land;
            Assert.That(tile.LandState, Is.EqualTo(TileLandState.Land));
            tile.LandState = TileLandState.Water;
            Assert.That(tile.LandState, Is.EqualTo(TileLandState.Water));
        }
    }
}
