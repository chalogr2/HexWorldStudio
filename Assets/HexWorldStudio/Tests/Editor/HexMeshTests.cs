using HexWorldStudio.Coordinates;
using HexWorldStudio.Rendering;
using NUnit.Framework;
using UnityEngine;

namespace HexWorldStudio.Tests
{
    /// <summary>
    /// EditMode tests for <see cref="HexMesh"/> — vertex / triangle
    /// counts, planar orientation, layout constants, world-position
    /// math.
    /// </summary>
    public class HexMeshTests
    {
        [Test]
        public void Build_HasSevenVertices()
        {
            Mesh mesh = HexMesh.Build();
            try
            {
                Assert.That(mesh.vertexCount, Is.EqualTo(7));
            }
            finally
            {
                Object.DestroyImmediate(mesh);
            }
        }

        [Test]
        public void Build_HasSixTriangles()
        {
            Mesh mesh = HexMesh.Build();
            try
            {
                Assert.That(mesh.triangles.Length, Is.EqualTo(6 * 3));
            }
            finally
            {
                Object.DestroyImmediate(mesh);
            }
        }

        [Test]
        public void Build_AllNormalsPointUp()
        {
            Mesh mesh = HexMesh.Build();
            try
            {
                Vector3[] normals = mesh.normals;
                Assert.That(normals.Length, Is.EqualTo(7));
                foreach (Vector3 n in normals)
                {
                    Assert.That(Vector3.Dot(n, Vector3.up), Is.EqualTo(1f).Within(1e-5f));
                }
            }
            finally
            {
                Object.DestroyImmediate(mesh);
            }
        }

        [Test]
        public void Build_CenterVertexIsAtOrigin()
        {
            Mesh mesh = HexMesh.Build();
            try
            {
                Assert.That(mesh.vertices[0], Is.EqualTo(Vector3.zero));
            }
            finally
            {
                Object.DestroyImmediate(mesh);
            }
        }

        [Test]
        public void Build_RingVertices_AreAtOuterRadiusFromCenter()
        {
            Mesh mesh = HexMesh.Build();
            try
            {
                for (int i = 1; i < mesh.vertices.Length; i++)
                {
                    float dist = mesh.vertices[i].magnitude;
                    Assert.That(dist, Is.EqualTo(HexMesh.OuterRadius).Within(1e-5f),
                        $"Ring vertex {i} sits at distance {dist}, expected OuterRadius={HexMesh.OuterRadius}.");
                }
            }
            finally
            {
                Object.DestroyImmediate(mesh);
            }
        }

        [Test]
        public void Build_TopVertexIsAtPlusZ()
        {
            // Pointy-top: the first ring vertex (vertex index 1) is at
            // the top of the hex, i.e., positive Z (forward in world).
            Mesh mesh = HexMesh.Build();
            try
            {
                Vector3 top = mesh.vertices[1];
                Assert.That(top.z, Is.GreaterThan(0f));
                Assert.That(top.x, Is.EqualTo(0f).Within(1e-5f));
            }
            finally
            {
                Object.DestroyImmediate(mesh);
            }
        }

        [Test]
        public void OuterRadius_DefaultIsOne()
        {
            Assert.That(HexMesh.OuterRadius, Is.EqualTo(1f));
        }

        [Test]
        public void InnerRadius_EqualsOuterRadiusTimesSqrt3Over2()
        {
            float expected = HexMesh.OuterRadius * (Mathf.Sqrt(3f) / 2f);
            Assert.That(HexMesh.InnerRadius, Is.EqualTo(expected).Within(1e-5f));
        }

        [Test]
        public void HorizontalSpacing_EqualsTwiceInnerRadius()
        {
            Assert.That(HexMesh.HorizontalSpacing,
                Is.EqualTo(2f * HexMesh.InnerRadius).Within(1e-5f));
        }

        [Test]
        public void VerticalSpacing_EqualsOnePointFiveOuterRadius()
        {
            Assert.That(HexMesh.VerticalSpacing,
                Is.EqualTo(1.5f * HexMesh.OuterRadius).Within(1e-5f));
        }

        [Test]
        public void WorldPosition_RowZero_Col0_IsAtOriginPlusElevation()
        {
            Vector3 pos = HexMesh.WorldPosition(0, 0, 0, 0.15f);
            Assert.That(pos, Is.EqualTo(Vector3.zero));
        }

        [Test]
        public void WorldPosition_EvenRow_NoOddRowShift()
        {
            Vector3 pos = HexMesh.WorldPosition(0, 2, 0, 0.15f);
            Assert.That(pos.x, Is.EqualTo(0f).Within(1e-5f));
        }

        [Test]
        public void WorldPosition_OddRow_ShiftsRightByOddRowOffset()
        {
            Vector3 pos = HexMesh.WorldPosition(0, 1, 0, 0.15f);
            Assert.That(pos.x, Is.EqualTo(HexMesh.OddRowOffset).Within(1e-5f));
        }

        [Test]
        public void WorldPosition_AdjacentColumnsSeparatedByHorizontalSpacing()
        {
            Vector3 a = HexMesh.WorldPosition(0, 0, 0, 0.15f);
            Vector3 b = HexMesh.WorldPosition(1, 0, 0, 0.15f);
            Assert.That(b.x - a.x, Is.EqualTo(HexMesh.HorizontalSpacing).Within(1e-5f));
        }

        [Test]
        public void WorldPosition_AdjacentRowsSeparatedByVerticalSpacing()
        {
            Vector3 a = HexMesh.WorldPosition(0, 0, 0, 0.15f);
            Vector3 b = HexMesh.WorldPosition(0, 1, 0, 0.15f);
            Assert.That(b.z - a.z, Is.EqualTo(HexMesh.VerticalSpacing).Within(1e-5f));
        }

        [Test]
        public void WorldPosition_ElevationLevel_MultipliedByStep_AddedToY()
        {
            Vector3 pos = HexMesh.WorldPosition(0, 0, 4, 0.25f);
            Assert.That(pos.y, Is.EqualTo(1.0f).Within(1e-5f));
        }

        [Test]
        public void WorldPosition_AxialCoordOverload_AgreesWithOffsetOverload()
        {
            // Pick a coord with known offset; verify both overloads
            // yield the same world position.
            HexCoord c = HexCoordExtensions.FromOffsetOddR(3, 5);
            Vector3 fromAxial = HexMesh.WorldPosition(c, 2, 0.15f);
            Vector3 fromOffset = HexMesh.WorldPosition(3, 5, 2, 0.15f);
            Assert.That(fromAxial, Is.EqualTo(fromOffset));
        }
    }
}
