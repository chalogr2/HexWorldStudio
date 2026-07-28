using System;
using System.Collections.Generic;
using HexWorldStudio.Content;
using HexWorldStudio.Data;
using UnityEngine;

namespace HexWorldStudio.Rendering
{
    /// <summary>
    /// Default river renderer per ADR-0025. Reads
    /// <see cref="WorldMap.RiverPaths"/> and emits one continuous
    /// mitered ribbon per river into a single shared mesh, textured by
    /// the assigned <see cref="RiverStyleAsset"/>. Buyers who want a
    /// completely custom river visual can disable this component and
    /// subscribe to <see cref="RiversRebuilt"/> to draw their own.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>One ribbon per river, not one quad per edge.</b> Because
    /// consecutive steps of a <see cref="RiverPath"/> share a corner,
    /// the ribbon's cross-sections are mitered at each joint and the
    /// segments meet exactly — no caps, no overlap tricks, no seams.
    /// </para>
    /// <para>
    /// <b>Height follows the terraces.</b> A channel step runs at the
    /// render height of its terrace level; a waterfall step descends
    /// from its <see cref="RiverStep.FromLevel"/> to its
    /// <see cref="RiverStep.ToLevel"/> as a flat lip, a near-vertical
    /// face, and a plunge pool. Because the generator guarantees
    /// <c>Steps[i].ToLevel == Steps[i + 1].FromLevel</c>, consecutive
    /// segments always agree on height at the corner they share.
    /// </para>
    /// <para>
    /// <b>UVs</b> are <b>U</b> across the river and <b>V</b> along the
    /// flow, accumulated monotonically from source to mouth — so a
    /// shader that scrolls V animates the whole river coherently
    /// downstream. The shipped <c>FlowingWater</c> shader is one such
    /// example; any URP water material works.
    /// </para>
    /// <para>
    /// <b>Width</b> tapers from <see cref="RiverStyleAsset.WidthRelativeToHex"/>
    /// at the source to a share of
    /// <see cref="RiverStyleAsset.MaxWidthRelativeToHex"/> at the
    /// mouth, set by the path's <see cref="RiverPath.StrahlerOrder"/>,
    /// so trunk rivers read larger than headwaters.
    /// </para>
    /// <para>
    /// <b>Cylindrical wrap:</b> corner positions are unwrapped
    /// relative to the previous corner, so a river crossing the seam
    /// emits one continuous ribbon running off the edge of the
    /// canonical world rather than a strip flung across its full
    /// width. <see cref="WorldWrapRenderer"/> shows the far side.
    /// </para>
    /// </remarks>
    public sealed class RiverRenderer : MonoBehaviour
    {
        [Tooltip("Visual style asset: material, widths, surface offset. When null the renderer builds no geometry.")]
        [SerializeField] private RiverStyleAsset _style;

        [Tooltip("Optional: matched WorldRenderer used to align river Y with the Civ-style stepped tile baseline. When null the renderer falls back to the stepped-baseline defaults.")]
        [SerializeField] private WorldRenderer _worldRenderer;

        // Shader property id for the material's base color, matching
        // what URP/Lit and the shipped FlowingWater shader both use.
        private static readonly int BaseColorProperty = Shader.PropertyToID("_BaseColor");

        private Mesh _riverMesh;
        private GameObject _riverGameObject;

        /// <summary>The style asset controlling material, widths, and surface offset.</summary>
        public RiverStyleAsset Style
        {
            get => _style;
            set => _style = value;
        }

        /// <summary>
        /// The root GameObject holding the built river mesh, or
        /// <c>null</c> when nothing is built. Exposed so
        /// <see cref="WorldWrapRenderer"/> can mirror river geometry
        /// even when this component lives on its own GameObject rather
        /// than beside the <see cref="WorldRenderer"/>.
        /// </summary>
        public GameObject BuiltGeometry => _riverGameObject;

        /// <summary>
        /// Fired after every successful <see cref="Render"/>. Passes
        /// the canonical river-edge collection so buyers can drive
        /// custom visualizations, gameplay overlays, or diagnostic
        /// tooling. Fires even when the renderer produces no geometry
        /// (empty roster, null style) so subscribers can clear cleanly.
        /// </summary>
        public event Action<IReadOnlyList<RiverEdge>> RiversRebuilt;

        /// <summary>
        /// Rebuilds the river geometry from the given map's
        /// <see cref="WorldMap.RiverPaths"/>. Destroys any previous
        /// geometry first so repeated calls are idempotent.
        /// </summary>
        /// <param name="map">The map to render rivers for. Must be non-null.</param>
        public void Render(WorldMap map)
        {
            if (map == null) throw new ArgumentNullException(nameof(map));

            Clear();

            IReadOnlyList<RiverPath> paths = map.RiverPaths;
            if (_style == null || _style.Material == null || paths == null || paths.Count == 0)
            {
                RiversRebuilt?.Invoke(map.RiverEdges ?? Array.Empty<RiverEdge>());
                return;
            }

            BuildRiverMesh(map, paths);
            RiversRebuilt?.Invoke(map.RiverEdges);
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

        // One polyline sample: a point on the river's centre line, the
        // half-width there, and the accumulated distance for V.
        private struct Sample
        {
            public Vector3 Position;
            public float HalfWidth;
            public float Distance;
        }

        private void BuildRiverMesh(WorldMap map, IReadOnlyList<RiverPath> paths)
        {
            float oceanY = _worldRenderer != null ? _worldRenderer.OceanRenderHeight : 0f;
            float landRise = _worldRenderer != null ? _worldRenderer.LandRiseAboveWater : 0.15f;
            float stepY = _worldRenderer != null ? _worldRenderer.ElevationStep : 0.15f;
            float surfaceOffset = _style.SurfaceYOffset;

            float minHalf = 0.5f * _style.WidthRelativeToHex * HexMesh.WidthFlatToFlat;
            float maxHalf = 0.5f * Mathf.Max(_style.WidthRelativeToHex, _style.MaxWidthRelativeToHex)
                            * HexMesh.WidthFlatToFlat;
            float lipFraction = Mathf.Clamp(_style.WaterfallLipFraction, 0.05f, 0.45f);
            float worldWidth = map.Width * HexMesh.HorizontalSpacing;

            var vertices = new List<Vector3>();
            var uvs = new List<Vector2>();
            var normals = new List<Vector3>();
            var triangles = new List<int>();
            var samples = new List<Sample>();

            for (int p = 0; p < paths.Count; p++)
            {
                RiverPath path = paths[p];

                // Trunk rivers get wider. Order 1 stays at the minimum
                // for its whole length, so headwaters read as streams.
                float growth = Mathf.Clamp01((path.StrahlerOrder - 1) / 3f);
                float mouthHalf = Mathf.Lerp(minHalf, maxHalf, growth);

                BuildPathSamples(path, map, samples, oceanY, landRise, stepY, surfaceOffset,
                                 minHalf, mouthHalf, lipFraction, worldWidth);
                EmitRibbon(samples, vertices, uvs, normals, triangles);
            }

            if (triangles.Count == 0) return;

            _riverMesh = new Mesh { name = "HexWorldStudio.Rivers" };
            if (vertices.Count > 65535)
            {
                _riverMesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            }
            _riverMesh.SetVertices(vertices);
            _riverMesh.SetUVs(0, uvs);
            _riverMesh.SetNormals(normals);
            _riverMesh.SetTriangles(triangles, 0);
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

        // Turns a path into a centre-line polyline. Channel steps
        // contribute their end corner; waterfall steps contribute three
        // extra samples (lip, top of face, bottom of face) so the drop
        // renders as an actual fall rather than a gentle ramp.
        private static void BuildPathSamples(
            RiverPath path, WorldMap map, List<Sample> samples,
            float oceanY, float landRise, float stepY, float surfaceOffset,
            float minHalf, float mouthHalf, float lipFraction, float worldWidth)
        {
            samples.Clear();

            IReadOnlyList<RiverStep> steps = path.Steps;
            float distance = 0f;

            Vector3 previous = HexMesh.CornerPosition(steps[0].From);
            AddSample(samples, new Vector3(previous.x, LevelY(steps[0].FromLevel, oceanY, landRise, stepY) + surfaceOffset, previous.z),
                      minHalf, ref distance, first: true);

            for (int i = 0; i < steps.Count; i++)
            {
                RiverStep step = steps[i];

                // Width grows along the river toward the mouth.
                float tEnd = steps.Count == 1 ? 1f : (i + 1) / (float)steps.Count;
                float halfEnd = Mathf.Lerp(minHalf, mouthHalf, tEnd);

                Vector3 raw = HexMesh.CornerPosition(step.To);
                Vector3 end = Unwrap(previous, raw, worldWidth);

                float yFrom = LevelY(step.FromLevel, oceanY, landRise, stepY) + surfaceOffset;
                float yTo = LevelY(step.ToLevel, oceanY, landRise, stepY) + surfaceOffset;

                if (!step.IsWaterfall)
                {
                    AddSample(samples, new Vector3(end.x, yTo, end.z), halfEnd, ref distance, first: false);
                }
                else
                {
                    // Flat lip, near-vertical face, flat plunge pool.
                    Vector3 lip = Vector3.Lerp(previous, end, lipFraction);
                    Vector3 foot = Vector3.Lerp(previous, end, 1f - lipFraction);

                    AddSample(samples, new Vector3(lip.x, yFrom, lip.z), halfEnd, ref distance, first: false);
                    AddSample(samples, new Vector3(foot.x, yFrom, foot.z), halfEnd, ref distance, first: false);
                    AddSample(samples, new Vector3(foot.x, yTo, foot.z), halfEnd, ref distance, first: false);
                    AddSample(samples, new Vector3(end.x, yTo, end.z), halfEnd, ref distance, first: false);
                }

                previous = end;
            }
        }

        private static void AddSample(List<Sample> samples, Vector3 position, float halfWidth,
                                      ref float distance, bool first)
        {
            if (!first && samples.Count > 0)
            {
                distance += Vector3.Distance(samples[samples.Count - 1].Position, position);
            }
            samples.Add(new Sample { Position = position, HalfWidth = halfWidth, Distance = distance });
        }

        private static float LevelY(int level, float oceanY, float landRise, float stepY) =>
            oceanY + landRise + level * stepY;

        // Chooses the representation of `raw` closest to `reference`
        // along X. Corner coordinates are canonicalized to the
        // lex-smallest of their three tiles, so two adjacent corners
        // straddling the wrap seam can name tiles a full map apart;
        // without this the ribbon would stretch across the whole world.
        private static Vector3 Unwrap(Vector3 reference, Vector3 raw, float worldWidth)
        {
            if (worldWidth <= 0f) return raw;
            float half = worldWidth * 0.5f;
            float x = raw.x;
            while (x - reference.x > half) x -= worldWidth;
            while (reference.x - x > half) x += worldWidth;
            return new Vector3(x, raw.y, raw.z);
        }

        // Emits a mitered triangle strip through the sample polyline.
        private static void EmitRibbon(
            List<Sample> samples, List<Vector3> vertices, List<Vector2> uvs,
            List<Vector3> normals, List<int> triangles)
        {
            int count = samples.Count;
            if (count < 2) return;

            float totalLength = samples[count - 1].Distance;
            if (totalLength <= 0f) totalLength = 1f;

            int baseIndex = vertices.Count;

            for (int i = 0; i < count; i++)
            {
                Vector3 position = samples[i].Position;

                // Flat-plane tangents: the ribbon is mitered in XZ, so
                // a vertical waterfall face keeps its full width.
                Vector3 incoming = i > 0 ? Flat(position - samples[i - 1].Position) : Vector3.zero;
                Vector3 outgoing = i < count - 1 ? Flat(samples[i + 1].Position - position) : Vector3.zero;

                Vector3 direction;
                if (incoming.sqrMagnitude < 1e-10f) direction = outgoing;
                else if (outgoing.sqrMagnitude < 1e-10f) direction = incoming;
                else direction = (incoming.normalized + outgoing.normalized);

                if (direction.sqrMagnitude < 1e-10f)
                {
                    // A pure vertical drop sample, or a 180-degree
                    // reversal: reuse whichever tangent is available.
                    direction = incoming.sqrMagnitude > 1e-10f ? incoming : Vector3.forward;
                }
                direction.Normalize();

                Vector3 normal = new Vector3(-direction.z, 0f, direction.x);

                // Miter compensation: at a bend, the cross-section must
                // widen by 1 / cos(half-angle) for the outer edges of
                // adjacent segments to stay parallel. Clamped so a
                // hairpin cannot produce a spike.
                float miter = 1f;
                if (incoming.sqrMagnitude > 1e-10f && outgoing.sqrMagnitude > 1e-10f)
                {
                    Vector3 inNormal = new Vector3(-incoming.normalized.z, 0f, incoming.normalized.x);
                    float cosHalf = Vector3.Dot(normal, inNormal);
                    miter = 1f / Mathf.Max(0.4f, Mathf.Abs(cosHalf));
                }

                Vector3 offset = normal * (samples[i].HalfWidth * miter);
                float v = samples[i].Distance / totalLength;

                vertices.Add(position + offset);
                uvs.Add(new Vector2(1f, v));
                normals.Add(Vector3.up);

                vertices.Add(position - offset);
                uvs.Add(new Vector2(0f, v));
                normals.Add(Vector3.up);
            }

            for (int i = 0; i < count - 1; i++)
            {
                int a = baseIndex + i * 2;
                int b = a + 1;
                int c = a + 2;
                int d = a + 3;

                triangles.Add(a); triangles.Add(c); triangles.Add(b);
                triangles.Add(b); triangles.Add(c); triangles.Add(d);
            }
        }

        private static Vector3 Flat(Vector3 v) => new Vector3(v.x, 0f, v.z);

        private static void DestroySafely(UnityEngine.Object obj)
        {
            if (Application.isPlaying) Destroy(obj);
            else DestroyImmediate(obj);
        }
    }
}
