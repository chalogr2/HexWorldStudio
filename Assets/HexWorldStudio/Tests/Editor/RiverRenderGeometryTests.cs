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
    /// EditMode tests for the geometry contracts the river renderer
    /// depends on (ADR-0025): that all three namings of a hex corner
    /// resolve to one world point — which is what lets adjacent river
    /// segments meet exactly — and that lakes are lifted to their
    /// terrace height instead of being drawn at sea level.
    /// </summary>
    public class RiverRenderGeometryTests
    {
        private const int MapWidth = 20;

        [Test]
        public void CornerPosition_AllThreeNamings_ResolveToTheSamePoint()
        {
            // If this drifts, ribbons built from different tiles' views
            // of the same corner would not meet, and every junction
            // would show a seam.
            for (int col = 3; col <= 7; col++)
            {
                for (int row = 3; row <= 7; row++)
                {
                    HexCoord tile = HexCoordExtensions.FromOffsetOddR(col, row);
                    for (int i = 0; i < HexCorner.CornersPerTile; i++)
                    {
                        Vector3 expected = HexMesh.CornerPosition(tile, i);

                        Vector3 viaA = HexMesh.CornerPosition(
                            tile.Neighbor(HexCorner.DirectionA(i)), i + 2);
                        Vector3 viaB = HexMesh.CornerPosition(
                            tile.Neighbor(HexCorner.DirectionB(i)), i + 4);

                        Assert.That(Vector3.Distance(viaA, expected), Is.LessThan(1e-4f),
                            $"Corner {i} of {tile} disagrees when named from its DirectionA neighbour.");
                        Assert.That(Vector3.Distance(viaB, expected), Is.LessThan(1e-4f),
                            $"Corner {i} of {tile} disagrees when named from its DirectionB neighbour.");
                    }
                }
            }
        }

        [Test]
        public void CornerPosition_AdjacentCorners_AreOneOuterRadiusApart()
        {
            HexCoord tile = HexCoordExtensions.FromOffsetOddR(5, 5);
            for (int i = 0; i < HexCorner.CornersPerTile; i++)
            {
                var corner = HexCorner.Canonicalize(tile, i, MapWidth);
                corner.GetAdjacentCorners(MapWidth, out HexCorner a, out HexCorner b, out HexCorner c);

                foreach (HexCorner neighbour in new[] { a, b, c })
                {
                    float d = Vector3.Distance(
                        HexMesh.CornerPosition(corner), HexMesh.CornerPosition(neighbour));
                    Assert.That(d, Is.EqualTo(HexMesh.OuterRadius).Within(1e-4f),
                        $"{corner} -> {neighbour} should span exactly one hex side.");
                }
            }
        }

        [Test]
        public void CornerPosition_MatchesTheTilesOwnRingVertices()
        {
            // Ties CornerPosition to the mesh the renderer actually
            // builds, so the two can never drift apart.
            HexCoord tile = HexCoordExtensions.FromOffsetOddR(4, 6);
            Vector3 center = HexMesh.WorldPosition(tile, 0, 0f);

            Mesh mesh = HexMesh.Build();
            try
            {
                Vector3[] ring = mesh.vertices;
                for (int i = 0; i < HexCorner.CornersPerTile; i++)
                {
                    Vector3 expected = center + ring[1 + i];
                    Vector3 actual = HexMesh.CornerPosition(tile, i);
                    Assert.That(Vector3.Distance(expected, actual), Is.LessThan(1e-4f),
                        $"CornerPosition({i}) does not match HexMesh ring vertex {i}.");
                }
            }
            finally
            {
                Object.DestroyImmediate(mesh);
            }
        }

        // ---- lake height ------------------------------------------

        private static HexTile NewTile(BiomeDefinition biome, int level, bool isLake)
        {
            var map = new WorldMap(4, 4, 1uL);
            map.TryGetTile(HexCoordExtensions.FromOffsetOddR(1, 1), out HexTile tile);
            tile.Biome = biome;
            tile.ElevationLevel = level;
            tile.IsLake = isLake;
            tile.LandState = biome != null && biome.IsWater ? TileLandState.Water : TileLandState.Land;
            return tile;
        }

        [Test]
        public void ComputeTileY_Lake_SitsAtItsTerraceHeightNotSeaLevel()
        {
            var water = ScriptableObject.CreateInstance<BiomeDefinition>();
            water.IsWater = true;
            water.IsCoastalWater = true;
            try
            {
                HexTile lake = NewTile(water, level: 4, isLake: true);
                float y = WorldRenderer.ComputeTileY(lake, oceanY: 0f,
                                                     landRise: 0.15f, elevationStep: 0.15f);

                Assert.That(y, Is.EqualTo(0f + 0.15f + 4 * 0.15f).Within(1e-5f),
                    "A lake must render at its surrounding terrace height — pinning it to sea "
                    + "level would punch a hole through the highlands.");
            }
            finally
            {
                Object.DestroyImmediate(water);
            }
        }

        [Test]
        public void ComputeTileY_SeaWater_IsUnaffectedByTheLakeRule()
        {
            var coast = ScriptableObject.CreateInstance<BiomeDefinition>();
            coast.IsWater = true;
            coast.IsCoastalWater = true;

            var ocean = ScriptableObject.CreateInstance<BiomeDefinition>();
            ocean.IsWater = true;
            ocean.IsCoastalWater = false;
            try
            {
                HexTile coastTile = NewTile(coast, level: 0, isLake: false);
                HexTile oceanTile = NewTile(ocean, level: 0, isLake: false);

                Assert.That(WorldRenderer.ComputeTileY(coastTile, 0f, 0.15f, 0.15f),
                    Is.EqualTo(0f).Within(1e-5f), "Coast must sit at sea level.");
                Assert.That(WorldRenderer.ComputeTileY(oceanTile, 0f, 0.15f, 0.15f),
                    Is.EqualTo(0f).Within(1e-5f), "Ocean height regressed.");
            }
            finally
            {
                Object.DestroyImmediate(coast);
                Object.DestroyImmediate(ocean);
            }
        }
    }
}
