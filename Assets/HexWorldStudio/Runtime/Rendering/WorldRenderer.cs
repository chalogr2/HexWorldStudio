using System.Collections.Generic;
using HexWorldStudio.Content;
using HexWorldStudio.Coordinates;
using HexWorldStudio.Data;
using UnityEngine;

namespace HexWorldStudio.Rendering
{
    /// <summary>
    /// MonoBehaviour that turns a <see cref="WorldMap"/> into scene
    /// geometry: one child <see cref="GameObject"/> per tile, parented
    /// to this renderer, with a shared <see cref="Mesh"/> and a
    /// per-biome <see cref="Material"/> from the
    /// <see cref="BiomeMaterialCache"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Per-tile color variation lands as a <see cref="MaterialPropertyBlock"/>
    /// on each tile's <see cref="MeshRenderer"/> so the SRP batcher
    /// can still group draw calls. Slice 7A sets the tile color from
    /// the biome; Slice 7B extends the property block to read a
    /// <c>TileFeatureDefinition.Color</c> override when present.
    /// </para>
    /// <para>
    /// One <see cref="MeshCollider"/> per tile is added at render time
    /// so Slice 9's hover / click raycasts hit them without requiring
    /// a renderer refactor.
    /// </para>
    /// <para>
    /// Buyers replace this renderer freely — the data layer never
    /// depends on it. Performance work (chunked rendering, GPU
    /// instancing) is tracked under "Performance &amp; scaling" in
    /// <c>FEATURES.md</c>.
    /// </para>
    /// </remarks>
    public sealed class WorldRenderer : MonoBehaviour
    {
        // URP base-color shader property id. Used by the property block
        // to override per-tile color without breaking SRP batching.
        private static readonly int BaseColorProperty = Shader.PropertyToID("_BaseColor");

        [Tooltip("World units added to a tile's Y per ElevationLevel step.")]
        [SerializeField] private float _elevationStep = 0.15f;

        [Tooltip("Multiplier applied to every tile's local scale. Defaults to 1 (uses HexMesh OuterRadius as-is).")]
        [SerializeField] private float _tileWorldScale = 1f;

        /// <summary>Y added per ElevationLevel step.</summary>
        public float ElevationStep
        {
            get => _elevationStep;
            set => _elevationStep = value;
        }

        /// <summary>Per-tile uniform scale multiplier.</summary>
        public float TileWorldScale
        {
            get => _tileWorldScale;
            set => _tileWorldScale = value;
        }

        private Mesh _sharedMesh;
        private BiomeMaterialCache _materialCache;
        private readonly List<GameObject> _tileObjects = new List<GameObject>();
        private readonly MaterialPropertyBlock _propertyBlock = new MaterialPropertyBlock();

        /// <summary>
        /// Number of tile GameObjects currently parented to this
        /// renderer. Equals <c>map.Tiles.Count</c> after a successful
        /// <see cref="Render"/>; <c>0</c> after <see cref="Clear"/>.
        /// </summary>
        public int TileCount => _tileObjects.Count;

        /// <summary>
        /// Rebuilds the scene-graph from <paramref name="map"/>.
        /// Destroys any previous tile GameObjects first so the renderer
        /// is idempotent across multiple calls.
        /// </summary>
        /// <param name="map">The map to render. Must be non-null.</param>
        /// <exception cref="System.ArgumentNullException">
        /// Thrown when <paramref name="map"/> is null.
        /// </exception>
        public void Render(WorldMap map)
        {
            if (map == null) throw new System.ArgumentNullException(nameof(map));

            Clear();
            EnsureSharedMesh();
            EnsureMaterialCache();

            for (int i = 0; i < map.Tiles.Count; i++)
            {
                HexTile tile = map.Tiles[i];
                _tileObjects.Add(BuildTileGameObject(tile));
            }
        }

        /// <summary>
        /// Destroys every previously-built tile GameObject and
        /// releases the shared mesh and material cache.
        /// </summary>
        public void Clear()
        {
            for (int i = 0; i < _tileObjects.Count; i++)
            {
                GameObject go = _tileObjects[i];
                if (go != null) DestroySafely(go);
            }
            _tileObjects.Clear();

            if (_sharedMesh != null)
            {
                DestroySafely(_sharedMesh);
                _sharedMesh = null;
            }

            _materialCache?.Dispose();
            _materialCache = null;
        }

        private void OnDestroy()
        {
            Clear();
        }

        private void EnsureSharedMesh()
        {
            if (_sharedMesh == null) _sharedMesh = HexMesh.Build();
        }

        private void EnsureMaterialCache()
        {
            if (_materialCache == null) _materialCache = new BiomeMaterialCache();
        }

        private GameObject BuildTileGameObject(HexTile tile)
        {
            (int col, int row) = tile.Coord.ToOffsetOddR();
            Vector3 worldPos = HexMesh.WorldPosition(col, row, tile.ElevationLevel, _elevationStep);

            var go = new GameObject($"Tile_{col}_{row}");
            go.transform.SetParent(transform, worldPositionStays: false);
            go.transform.localPosition = worldPos;
            go.transform.localScale = Vector3.one * _tileWorldScale;

            var filter = go.AddComponent<MeshFilter>();
            filter.sharedMesh = _sharedMesh;

            var meshRenderer = go.AddComponent<MeshRenderer>();
            BiomeDefinition biome = tile.Biome;
            if (biome != null)
            {
                meshRenderer.sharedMaterial = _materialCache.Get(biome);
                ApplyTileColor(meshRenderer, ResolveTileColor(tile));
            }

            // Mesh collider so Slice 9 raycasts hit tiles without a
            // renderer refactor.
            var collider = go.AddComponent<MeshCollider>();
            collider.sharedMesh = _sharedMesh;

            return go;
        }

        // Slice 7A: color comes from the biome. Slice 7B extends this
        // to read tile.Feature.Color when a feature is assigned.
        private static Color ResolveTileColor(HexTile tile)
        {
            return tile.Biome != null ? tile.Biome.Color : Color.magenta;
        }

        private void ApplyTileColor(MeshRenderer meshRenderer, Color color)
        {
            meshRenderer.GetPropertyBlock(_propertyBlock);
            _propertyBlock.SetColor(BaseColorProperty, color);
            meshRenderer.SetPropertyBlock(_propertyBlock);
        }

        // EditMode-safe destroy. Tests run in edit mode and need
        // DestroyImmediate to actually take effect; PlayMode uses
        // Destroy so we don't trip the "DestroyImmediate inside Play"
        // warning.
        private static void DestroySafely(UnityEngine.Object obj)
        {
            if (Application.isPlaying)
            {
                Destroy(obj);
            }
            else
            {
                DestroyImmediate(obj);
            }
        }
    }
}
