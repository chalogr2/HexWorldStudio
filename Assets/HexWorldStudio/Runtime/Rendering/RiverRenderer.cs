using System;
using System.Collections.Generic;
using HexWorldStudio.Content;
using HexWorldStudio.Coordinates;
using HexWorldStudio.Data;
using UnityEngine;

namespace HexWorldStudio.Rendering
{
    /// <summary>
    /// Default river renderer per ADR-0024. Reads
    /// <see cref="WorldMap.RiverEdges"/> and emits a single
    /// procedural strip mesh — one quad per river edge — under this
    /// component's transform, textured by the assigned
    /// <see cref="RiverStyleAsset"/>. Buyers who want a completely
    /// custom river visual can disable this component and subscribe
    /// to <see cref="RiversRebuilt"/> to draw their own.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Each river edge becomes a flat quad sitting on the shared
    /// boundary between its two hex tiles. UVs are <b>U</b> across
    /// the river (0 to 1 bank-to-bank) and <b>V</b> along the flow
    /// (0 to 1 upstream-to-downstream), so any material whose
    /// shader scrolls V produces animated flow — the shipped
    /// <c>FlowingWater</c> shader is one such example, but any URP
    /// Shader Graph water material works transparently.
    /// </para>
    /// <para>
    /// Junctions: because every vertex on the strip is computed as
    /// a pure function of a hex-vertex world position, edges that
    /// share a hex vertex naturally meet at the same world point.
    /// Adjacent edges' widths diverge slightly at the shared point
    /// (they're perpendicular to different flow axes), producing a
    /// small visible seam at Y-junctions. A per-vertex-flow-map
    /// upgrade lives in the Slice 17.1 deferred list — for v17.0
    /// the seam reads as natural turbulence at most zoom levels.
    /// </para>
    /// <para>
    /// Cylindrical wrap: wrap-crossing edges emit geometry that
    /// extends past the canonical world's horizontal seam; the
    /// <see cref="WorldWrapRenderer"/> clones this renderer's
    /// GameObject along with everything else and stitches the two
    /// sides together in the standard way.
    /// </para>
    /// </remarks>
    public sealed class RiverRenderer : MonoBehaviour
    {
        [Tooltip("Visual style asset: material, width, surface offset. When null the renderer builds no geometry.")]
        [SerializeField] private RiverStyleAsset _style;

        [Tooltip("Optional: matched WorldRenderer used to align river Y with the Civ-style stepped tile baseline. When null the renderer uses the settings echoed on this component.")]
        [SerializeField] private WorldRenderer _worldRenderer;

        // Shader property id for the material's base color, matching
        // what URP/Lit and the shipped FlowingWater shader both use.
        private static readonly int BaseColorProperty = Shader.PropertyToID("_BaseColor");

        private Mesh _riverMesh;
        private GameObject _riverGameObject;

        /// <summary>The style asset controlling material, width, and surface offset.</summary>
        public RiverStyleAsset Style
        {
            get => _style;
            set => _style = value;
        }

        /// <summary>
        /// Fired after every successful <see cref="Render"/>. Passes
        /// the canonical river-edge collection so buyers can drive
        /// custom visualizations, gameplay overlays, or diagnostic
        /// tooling. Fires even when the renderer produces no
        /// geometry (empty roster, null style) so subscribers can
        /// clear cleanly.
        /// </summary>
        public event Action<IReadOnlyList<RiverEdge>> RiversRebuilt;

        /// <summary>
        /// Rebuilds the river geometry from the given map's
        /// <see cref="WorldMap.RiverEdges"/>. Destroys any previous
        /// geometry first so repeated calls are idempotent.
        /// </summary>
        /// <param name="map">The map to render rivers for. Must be non-null.</param>
        public void Render(WorldMap map)
        {
            if (map == null) throw new ArgumentNullException(nameof(map));

            Clear();

            IReadOnlyList<RiverEdge> edges = map.RiverEdges;
            if (_style == null || _style.Material == null || edges == null || edges.Count == 0)
            {
                RiversRebuilt?.Invoke(edges ?? Array.Empty<RiverEdge>());
                return;
            }

            BuildRiverMesh(map, edges);
            RiversRebuilt?.Invoke(edges);
        }

        /// <summary>
        /// Destroys the built river mesh and its holder GameObject.
        /// Called automatically from <see cref="Render"/> before a
        /// rebuild.
        /// </summary>
        public void Clear()
        {
            if (_riverGameObject != null)
            {
                DestroySafely(_riverGameObject);
                _riverGameObject = null;
            }
            if (_riverMesh != null)
            {
                DestroySafely(_riverMesh);
                _riverMesh = null;
            }
        }

        private void OnDestroy() => Clear();

        // Builds a triangulated strip per RiverEdge into a single
        // shared Mesh: a rectangle along the shared hex edge (from
        // one hex corner to the other) plus a small hexagon cap
        // centered at each hex-corner endpoint. Adjacent river edges
        // share hex corners (via the post-processing pass in
        // RiverStage), so their hexagon caps are centered on the
        // same point — the caps overlap perfectly and hide the
        // orientation mismatch between adjacent rectangle short
        // ends. UVs on the rectangle body follow U-across-V-along-
        // flow so shader-graph water materials scroll cleanly.
        private void BuildRiverMesh(WorldMap map, IReadOnlyList<RiverEdge> edges)
        {
            const int CapRingCount = 6;
            const int VertsPerCap = 1 + CapRingCount;
            const int VertsPerEdge = 4 + 2 * VertsPerCap;
            const int TrisPerCap = CapRingCount;
            const int IndicesPerEdge = 6 + 2 * TrisPerCap * 3;

            int quadCount = edges.Count;
            var vertices  = new Vector3[quadCount * VertsPerEdge];
            var uvs       = new Vector2[quadCount * VertsPerEdge];
            var normals   = new Vector3[quadCount * VertsPerEdge];
            var triangles = new int[quadCount * IndicesPerEdge];

            float halfWidth = 0.5f * _style.WidthRelativeToHex * HexMesh.WidthFlatToFlat;
            float surfaceOffset = _style.SurfaceYOffset;

            // Height model comes from the assigned WorldRenderer's
            // stepped-baseline knobs. River Y = pivot(higher tile) +
            // SurfaceYOffset. SurfaceYOffset represents the terrain-
            // surface offset above the pivot (defaults to 0.2 for
            // Kenney Hexagon Kit prefabs). Using the pivot instead
            // of the full visible-bounds top keeps rivers on the
            // terrain surface rather than on top of tree canopies
            // or mountain peaks inside a feature prefab.
            float oceanY   = _worldRenderer != null ? _worldRenderer.OceanRenderHeight : 0f;
            float coastR   = _worldRenderer != null ? _worldRenderer.CoastRiseAboveOcean : 0.05f;
            float landR    = _worldRenderer != null ? _worldRenderer.LandRiseAboveWater : 0.15f;
            float stepY    = _worldRenderer != null ? _worldRenderer.ElevationStep : 0.15f;

            for (int i = 0; i < quadCount; i++)
            {
                RiverEdge edge = edges[i];
                HexCoord a = edge.Coord;
                HexCoord bRaw = a.Neighbor(edge.Direction);

                map.TryGetTile(a, out HexTile tileA);
                map.TryGetTile(bRaw, out HexTile tileB);

                Vector3 aCenter = HexMesh.WorldPosition(a, 0, 0f);
                Vector3 bCenter = HexMesh.WorldPosition(bRaw, 0, 0f);

                float yA = WorldRenderer.ComputeTileY(tileA, oceanY, coastR, landR, stepY);
                float yB = WorldRenderer.ComputeTileY(tileB, oceanY, coastR, landR, stepY);
                float y  = Mathf.Max(yA, yB) + surfaceOffset;

                aCenter.y = 0f;
                bCenter.y = 0f;

                Vector3 mid       = (aCenter + bCenter) * 0.5f;
                Vector3 diff      = bCenter - aCenter;
                Vector3 flowDir   = new Vector3(-diff.z, 0f, diff.x).normalized;
                Vector3 acrossDir = diff.normalized;

                // Rectangle body: the strip runs along the shared
                // hex edge from one hex corner (pStart) to the other
                // (pEnd), with width extending halfWidth into each
                // neighboring tile. Straight-sided rectangle back
                // per the buyer request.
                Vector3 pStart    = mid + flowDir * (HexMesh.OuterRadius * 0.5f);
                Vector3 pEnd      = mid - flowDir * (HexMesh.OuterRadius * 0.5f);
                Vector3 halfCross = acrossDir * halfWidth;

                int v = i * VertsPerEdge;

                Vector3 q0 = new Vector3(pStart.x + halfCross.x, y, pStart.z + halfCross.z);
                Vector3 q1 = new Vector3(pStart.x - halfCross.x, y, pStart.z - halfCross.z);
                Vector3 q2 = new Vector3(pEnd.x   + halfCross.x, y, pEnd.z   + halfCross.z);
                Vector3 q3 = new Vector3(pEnd.x   - halfCross.x, y, pEnd.z   - halfCross.z);

                vertices[v + 0] = q0; uvs[v + 0] = new Vector2(1f, 0f);
                vertices[v + 1] = q1; uvs[v + 1] = new Vector2(0f, 0f);
                vertices[v + 2] = q2; uvs[v + 2] = new Vector2(1f, 1f);
                vertices[v + 3] = q3; uvs[v + 3] = new Vector2(0f, 1f);
                normals[v + 0] = Vector3.up;
                normals[v + 1] = Vector3.up;
                normals[v + 2] = Vector3.up;
                normals[v + 3] = Vector3.up;

                int t = i * IndicesPerEdge;
                triangles[t + 0] = v + 0;
                triangles[t + 1] = v + 2;
                triangles[t + 2] = v + 1;
                triangles[t + 3] = v + 1;
                triangles[t + 4] = v + 2;
                triangles[t + 5] = v + 3;
                t += 6;

                // Hexagon caps centered at each hex-corner endpoint
                // (pStart, pEnd). Radius = halfWidth so the cap
                // covers the rectangle's short-side width. Adjacent
                // river edges share hex corners (post-processing in
                // RiverStage guarantees corner-connected chains), so
                // their caps are centered on the same point and
                // overlap perfectly — smoothing over the width-axis
                // mismatch between adjacent rectangle short ends.
                int capStart = v + 4;
                t = BuildCornerCap(vertices, uvs, normals, triangles, capStart, t, pStart, y, halfWidth);
                int capEnd = capStart + VertsPerCap;
                t = BuildCornerCap(vertices, uvs, normals, triangles, capEnd, t, pEnd, y, halfWidth);
            }

            _riverMesh = new Mesh { name = "HexWorldStudio.Rivers" };
            if (vertices.Length > 65535) _riverMesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            _riverMesh.vertices  = vertices;
            _riverMesh.uv        = uvs;
            _riverMesh.normals   = normals;
            _riverMesh.triangles = triangles;
            _riverMesh.RecalculateBounds();

            _riverGameObject = new GameObject("Rivers");
            _riverGameObject.transform.SetParent(transform, worldPositionStays: false);
            _riverGameObject.transform.localPosition = Vector3.zero;

            var filter = _riverGameObject.AddComponent<MeshFilter>();
            filter.sharedMesh = _riverMesh;

            var renderer = _riverGameObject.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = _style.Material;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;

            if (_style.Material.HasProperty(BaseColorProperty))
            {
                var block = new MaterialPropertyBlock();
                renderer.GetPropertyBlock(block);
                block.SetColor(BaseColorProperty, _style.BaseColor);
                renderer.SetPropertyBlock(block);
            }
        }

        // Emits a hexagonal disc centered at `center` with radius
        // `radius` into the vertex / index buffers, starting at
        // `vertexStart` and `indexCursor`. Returns the new index
        // cursor. Six triangles fan from the center vertex around
        // six ring vertices at 60° increments.
        private static int BuildCornerCap(
            Vector3[] vertices, Vector2[] uvs, Vector3[] normals, int[] triangles,
            int vertexStart, int indexCursor, Vector3 center, float y, float radius)
        {
            const int Ring = 6;
            vertices[vertexStart]  = new Vector3(center.x, y, center.z);
            uvs[vertexStart]       = new Vector2(0.5f, 0.5f);
            normals[vertexStart]   = Vector3.up;

            for (int r = 0; r < Ring; r++)
            {
                float angle = r * (Mathf.PI * 2f / Ring);
                float cx = center.x + Mathf.Cos(angle) * radius;
                float cz = center.z + Mathf.Sin(angle) * radius;
                vertices[vertexStart + 1 + r] = new Vector3(cx, y, cz);
                uvs[vertexStart + 1 + r]      = new Vector2(0.5f + Mathf.Cos(angle) * 0.5f,
                                                            0.5f + Mathf.Sin(angle) * 0.5f);
                normals[vertexStart + 1 + r]  = Vector3.up;
            }

            for (int r = 0; r < Ring; r++)
            {
                int next = (r + 1) % Ring;
                triangles[indexCursor++] = vertexStart;
                triangles[indexCursor++] = vertexStart + 1 + r;
                triangles[indexCursor++] = vertexStart + 1 + next;
            }
            return indexCursor;
        }

        private static void DestroySafely(UnityEngine.Object obj)
        {
            if (Application.isPlaying) Destroy(obj);
            else DestroyImmediate(obj);
        }
    }
}
