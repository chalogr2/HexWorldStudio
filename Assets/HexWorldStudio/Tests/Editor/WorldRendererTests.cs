using HexWorldStudio.Content;
using HexWorldStudio.Coordinates;
using HexWorldStudio.Data;
using HexWorldStudio.Rendering;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

namespace HexWorldStudio.Tests
{
    /// <summary>
    /// EditMode tests for <see cref="WorldRenderer"/> pure helpers —
    /// today the Civ-style stepped-baseline Y computation. Renderer
    /// integration (GameObject construction, prefab swap, color flow)
    /// is exercised by the PlayMode <c>WorldRendererPlayModeTests</c>.
    /// </summary>
    public class WorldRendererTests
    {
        // Tunables matching the WorldRenderer inspector defaults.
        private const float OceanY = 0f;
        private const float CoastRise = 0.05f;
        private const float LandRise = 0.15f;
        private const float ElevationStep = 0.15f;

        private static HexTile NewTile(BiomeDefinition biome, int elevationLevel)
        {
            HexTile tile = new HexTile(new HexCoord(0, 0), tileIndex: 0)
            {
                Biome = biome,
                ElevationLevel = elevationLevel,
            };
            tile.LandState = (biome != null && biome.IsWater)
                ? TileLandState.Water
                : TileLandState.Land;
            return tile;
        }

        private static BiomeDefinition NewBiome(bool isWater, bool isCoastal)
        {
            var b = ScriptableObject.CreateInstance<BiomeDefinition>();
            b.IsWater = isWater;
            b.IsCoastalWater = isCoastal;
            return b;
        }

        [Test]
        public void ComputeTileY_OceanTile_SitsAtOceanRenderHeight()
        {
            BiomeDefinition ocean = NewBiome(isWater: true, isCoastal: false);
            try
            {
                HexTile tile = NewTile(ocean, elevationLevel: 0);
                float y = WorldRenderer.ComputeTileY(tile, OceanY, CoastRise, LandRise, ElevationStep);
                Assert.That(y, Is.EqualTo(OceanY).Within(1e-5f));
            }
            finally { Object.DestroyImmediate(ocean); }
        }

        [Test]
        public void ComputeTileY_CoastTile_SitsAtOceanPlusCoastRise()
        {
            BiomeDefinition coast = NewBiome(isWater: true, isCoastal: true);
            try
            {
                HexTile tile = NewTile(coast, elevationLevel: 0);
                float y = WorldRenderer.ComputeTileY(tile, OceanY, CoastRise, LandRise, ElevationStep);
                Assert.That(y, Is.EqualTo(OceanY + CoastRise).Within(1e-5f),
                    "Coast tiles must sit above Ocean by exactly CoastRiseAboveOcean for the continental-shelf look.");
            }
            finally { Object.DestroyImmediate(coast); }
        }

        [Test]
        public void ComputeTileY_LandTileAtLevelZero_SitsOnCoastlineShelf()
        {
            BiomeDefinition land = NewBiome(isWater: false, isCoastal: false);
            try
            {
                HexTile tile = NewTile(land, elevationLevel: 0);
                float y = WorldRenderer.ComputeTileY(tile, OceanY, CoastRise, LandRise, ElevationStep);
                Assert.That(y, Is.EqualTo(OceanY + LandRise).Within(1e-5f),
                    "Lowest land tile must sit at the visible coastline shelf, above Coast by (LandRiseAboveWater - CoastRiseAboveOcean).");
            }
            finally { Object.DestroyImmediate(land); }
        }

        [Test]
        public void ComputeTileY_LandTile_StepsUpByElevationStepPerLevel()
        {
            BiomeDefinition land = NewBiome(isWater: false, isCoastal: false);
            try
            {
                for (int level = 0; level <= 5; level++)
                {
                    HexTile tile = NewTile(land, elevationLevel: level);
                    float y = WorldRenderer.ComputeTileY(tile, OceanY, CoastRise, LandRise, ElevationStep);
                    Assert.That(y, Is.EqualTo(OceanY + LandRise + level * ElevationStep).Within(1e-5f),
                        $"Land at level {level} must step up linearly from the coastline shelf.");
                }
            }
            finally { Object.DestroyImmediate(land); }
        }

        [Test]
        public void ComputeTileY_LowestLand_AlwaysSitsAboveCoast()
        {
            // Pin the Slice-11.1 invariant under the new baseline:
            // even at level 0, land must sit visibly above coastal water.
            // Requires LandRise > CoastRise — defaults satisfy this and
            // this test pins it as a contract.
            BiomeDefinition land = NewBiome(isWater: false, isCoastal: false);
            BiomeDefinition coast = NewBiome(isWater: true, isCoastal: true);
            try
            {
                float landY = WorldRenderer.ComputeTileY(NewTile(land, 0), OceanY, CoastRise, LandRise, ElevationStep);
                float coastY = WorldRenderer.ComputeTileY(NewTile(coast, 0), OceanY, CoastRise, LandRise, ElevationStep);
                Assert.That(landY, Is.GreaterThan(coastY),
                    "Lowest land must render above Coast water — defaults guarantee this; inspector overrides may violate.");
            }
            finally
            {
                Object.DestroyImmediate(land);
                Object.DestroyImmediate(coast);
            }
        }

        [Test]
        public void ComputeTileY_CoastSitsAboveOcean()
        {
            BiomeDefinition coast = NewBiome(isWater: true, isCoastal: true);
            BiomeDefinition ocean = NewBiome(isWater: true, isCoastal: false);
            try
            {
                float coastY = WorldRenderer.ComputeTileY(NewTile(coast, 0), OceanY, CoastRise, LandRise, ElevationStep);
                float oceanY = WorldRenderer.ComputeTileY(NewTile(ocean, 0), OceanY, CoastRise, LandRise, ElevationStep);
                Assert.That(coastY, Is.GreaterThan(oceanY),
                    "Coast (shallow) must render above Ocean (deep) so the shoreline reads visibly even with no per-tile noise.");
            }
            finally
            {
                Object.DestroyImmediate(coast);
                Object.DestroyImmediate(ocean);
            }
        }

        [Test]
        public void ComputeTileY_NullTile_TreatsAsLowestLand()
        {
            // Defensive: null tile shouldn't NRE. The pure helper treats
            // it as land-at-level-0 (the safest visible default).
            float y = WorldRenderer.ComputeTileY(null, OceanY, CoastRise, LandRise, ElevationStep);
            Assert.That(y, Is.EqualTo(OceanY + LandRise).Within(1e-5f));
        }
    }
}
