using System;
using HexWorldStudio.Coordinates;
using HexWorldStudio.Data;
using UnityEngine;

namespace HexWorldStudio.Rendering
{
    /// <summary>
    /// Procedural pointy-top hex mesh builder (ADR-0013).
    /// </summary>
    /// <remarks>
    /// <para>
    /// One mesh per call to <see cref="Build"/>: a flat hex with a
    /// center vertex and six ring vertices, normals pointing up
    /// (<c>+Y</c>). Vertices are wound counter-clockwise around the
    /// ring starting at the top ("12 o'clock") so the fan triangulation
    /// produces outward-facing front faces.
    /// </para>
    /// <para>
    /// Layout constants are exposed as <c>public const</c> /
    /// <c>public static readonly</c> so buyer scripts can use the same
    /// spacing math the default <c>WorldRenderer</c> uses to place
    /// tiles in world space.
    /// </para>
    /// </remarks>
    public static class HexMesh
    {
        /// <summary>Distance from the hex center to a vertex.</summary>
        public const float OuterRadius = 1f;

        /// <summary>
        /// Distance from the hex center to an edge midpoint.
        /// Equals <c>OuterRadius * sqrt(3) / 2</c>.
        /// </summary>
        public static readonly float InnerRadius = OuterRadius * 0.8660254037844386f;

        /// <summary>Flat-edge to flat-edge width. Equals <c>2 * InnerRadius</c>.</summary>
        public static readonly float WidthFlatToFlat = 2f * InnerRadius;

        /// <summary>Vertex-to-vertex height. Equals <c>2 * OuterRadius</c>.</summary>
        public const float HeightPointToPoint = 2f * OuterRadius;

        /// <summary>Column-to-column world distance within a row.</summary>
        public static readonly float HorizontalSpacing = WidthFlatToFlat;

        /// <summary>Row-to-row world distance.</summary>
        public const float VerticalSpacing = 1.5f * OuterRadius;

        /// <summary>Horizontal offset applied to odd rows (odd-r layout).</summary>
        public static readonly float OddRowOffset = InnerRadius;

        /// <summary>
        /// Builds a fresh flat pointy-top hex mesh: 7 vertices, 6
        /// triangles fanned from the center, normals <c>+Y</c>.
        /// Caller owns the returned <see cref="Mesh"/> instance and is
        /// responsible for destroying it (typically through the
        /// renderer's lifecycle).
        /// </summary>
        public static Mesh Build()
        {
            var mesh = new Mesh
            {
                name = "HexMesh.PointyTop",
            };

            // Vertex 0 = center; vertices 1..6 = ring, starting at the top
            // and going counter-clockwise.
            Vector3[] vertices = new Vector3[7];
            vertices[0] = Vector3.zero;
            for (int i = 0; i < 6; i++)
            {
                // Pointy-top: top vertex sits at angle 90° from +X.
                // Going CCW: 90°, 150°, 210°, 270°, 330°, 30°.
                float angleDeg = 90f + i * 60f;
                float rad = angleDeg * Mathf.Deg2Rad;
                vertices[i + 1] = new Vector3(
                    OuterRadius * Mathf.Cos(rad),
                    0f,
                    OuterRadius * Mathf.Sin(rad));
            }

            // Triangle fan from center.
            int[] triangles = new int[6 * 3];
            for (int i = 0; i < 6; i++)
            {
                int next = (i + 1) % 6;
                triangles[i * 3 + 0] = 0;
                triangles[i * 3 + 1] = 1 + i;
                triangles[i * 3 + 2] = 1 + next;
            }

            Vector3[] normals = new Vector3[7];
            for (int i = 0; i < normals.Length; i++) normals[i] = Vector3.up;

            // Simple planar UV: map (x, z) of each vertex onto [0, 1]
            // by dividing by the bounding diameter and offsetting.
            Vector2[] uv = new Vector2[7];
            float diameter = HeightPointToPoint;
            for (int i = 0; i < vertices.Length; i++)
            {
                uv[i] = new Vector2(
                    (vertices[i].x + OuterRadius) / diameter,
                    (vertices[i].z + OuterRadius) / diameter);
            }

            mesh.vertices = vertices;
            mesh.triangles = triangles;
            mesh.normals = normals;
            mesh.uv = uv;
            mesh.RecalculateBounds();
            return mesh;
        }

        /// <summary>
        /// World-space position for a tile at the given odd-r offset
        /// column / row and elevation level.
        /// </summary>
        /// <param name="col">Offset column (odd-r).</param>
        /// <param name="row">Offset row.</param>
        /// <param name="elevationLevel">
        /// Discrete elevation level. Multiplied by
        /// <paramref name="elevationStep"/> to compute world Y.
        /// </param>
        /// <param name="elevationStep">
        /// World units added to Y per <paramref name="elevationLevel"/>
        /// step. Buyers commonly use the renderer's
        /// <c>ElevationStep</c> default of <c>0.15</c>.
        /// </param>
        public static Vector3 WorldPosition(int col, int row, int elevationLevel, float elevationStep)
        {
            float x = col * HorizontalSpacing + ((row & 1) == 1 ? OddRowOffset : 0f);
            float y = elevationLevel * elevationStep;
            float z = row * VerticalSpacing;
            return new Vector3(x, y, z);
        }

        /// <summary>
        /// Convenience overload: computes world position from an axial
        /// <see cref="HexCoord"/> using odd-r conversion.
        /// </summary>
        public static Vector3 WorldPosition(HexCoord coord, int elevationLevel, float elevationStep)
        {
            (int col, int row) = coord.ToOffsetOddR();
            return WorldPosition(col, row, elevationLevel, elevationStep);
        }

        /// <summary>
        /// World-space position of one of a tile's six corners, on the
        /// <c>Y = 0</c> plane. Corner <c>index</c> matches the ring
        /// vertex order <see cref="Build"/> produces: index <c>0</c> at
        /// 90°, increasing counter-clockwise in 60° steps.
        /// </summary>
        /// <param name="coord">The owning tile.</param>
        /// <param name="cornerIndex">Corner index; wrapped into <c>[0, 6)</c>.</param>
        /// <remarks>
        /// The three tiles meeting at a corner all resolve to the same
        /// point through this method, which is what lets edge-based
        /// geometry (rivers, borders, outlines) meet exactly.
        /// </remarks>
        public static Vector3 CornerPosition(HexCoord coord, int cornerIndex)
        {
            Vector3 center = WorldPosition(coord, 0, 0f);
            int i = ((cornerIndex % 6) + 6) % 6;
            float rad = (90f + i * 60f) * Mathf.Deg2Rad;
            return new Vector3(
                center.x + OuterRadius * Mathf.Cos(rad),
                0f,
                center.z + OuterRadius * Mathf.Sin(rad));
        }

        /// <summary>
        /// World-space position of a <see cref="HexCorner"/>, on the
        /// <c>Y = 0</c> plane. Convenience wrapper over
        /// <see cref="CornerPosition(HexCoord, int)"/>.
        /// </summary>
        /// <param name="corner">The corner, in any naming.</param>
        public static Vector3 CornerPosition(HexCorner corner) =>
            CornerPosition(corner.Coord, corner.Index);
    }
}
