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
    /// to this renderer. Each tile's visual is one of two paths:
    /// (a) when the tile's <see cref="BiomeDefinition.Prefab"/> or
    /// <see cref="TileFeatureDefinition.Prefab"/> is assigned, that
    /// prefab is instantiated as a child of the tile root (feature
    /// wins over biome); (b) otherwise the procedural <see cref="HexMesh"/>
    /// + per-biome <see cref="Material"/> + per-tile
    /// <see cref="MaterialPropertyBlock"/> path is used. The choice
    /// is per-tile and resolved by ADR-0017.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Procedural path (no prefab): per-tile color variation lands as a
    /// <see cref="MaterialPropertyBlock"/> on each tile's
    /// <see cref="MeshRenderer"/> so the SRP batcher can still group
    /// draw calls. Slice 7A sets the tile color from the biome; Slice
    /// 7B extends the property block to read a
    /// <c>TileFeatureDefinition.Color</c> override when present.
    /// </para>
    /// <para>
    /// Every tile root carries a <see cref="HexTileBinding"/> and a
    /// <see cref="MeshCollider"/> whose <c>sharedMesh</c> is the flat
    /// <see cref="HexMesh"/> shared mesh — regardless of which visual
    /// path is chosen. This guarantees Slice 9's hover / click picking
    /// works on buyer prefabs that may carry no colliders or
    /// arbitrarily complex collider geometry. Picking walks up the
    /// transform parent chain to find the binding.
    /// </para>
    /// <para>
    /// Tile Y uses a Civ-style stepped baseline: Ocean tiles pin to
    /// <see cref="OceanRenderHeight"/>, Coast tiles to
    /// <c>OceanRenderHeight + CoastRiseAboveOcean</c>, and land tiles
    /// to <c>OceanRenderHeight + LandRiseAboveWater + ElevationLevel * ElevationStep</c>.
    /// Water tiles get no per-tile noise variation — uniform water
    /// reads as calm / readable in the Civ aesthetic, and Coast vs
    /// Ocean provides the natural shoreline distinction. See
    /// <see cref="ComputeTileY"/>.
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

        [Header("Water level (Civ-style stepped baseline)")]

        [Tooltip("Y of open-ocean tiles. Sets the canonical sea level for the world.")]
        [SerializeField] private float _oceanRenderHeight = 0f;

        [Tooltip("World units Coast tiles rise above OceanRenderHeight. Distinguishes shallow coastal water from open ocean (continental-shelf look).")]
        [SerializeField] private float _coastRiseAboveOcean = 0.05f;

        [Tooltip("World units the lowest land tile (ElevationLevel 0) rises above OceanRenderHeight. Forms the visible coastline shelf between water and land. Higher values exaggerate the cliff between water and beach.")]
        [SerializeField] private float _landRiseAboveWater = 0.15f;

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

        /// <summary>Y of open-ocean tiles.</summary>
        public float OceanRenderHeight
        {
            get => _oceanRenderHeight;
            set => _oceanRenderHeight = value;
        }

        /// <summary>World units Coast tiles rise above <see cref="OceanRenderHeight"/>.</summary>
        public float CoastRiseAboveOcean
        {
            get => _coastRiseAboveOcean;
            set => _coastRiseAboveOcean = value;
        }

        /// <summary>World units the lowest land tile rises above <see cref="OceanRenderHeight"/>.</summary>
        public float LandRiseAboveWater
        {
            get => _landRiseAboveWater;
            set => _landRiseAboveWater = value;
        }

        private Mesh _sharedMesh;
        private BiomeMaterialCache _materialCache;
        private readonly List<GameObject> _tileObjects = new List<GameObject>();
        // MaterialPropertyBlock cannot be created as a field initializer
        // on a MonoBehaviour — Unity rejects calls into its internals from
        // a base ctor. Created lazily on first use.
        private MaterialPropertyBlock _propertyBlock;

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
            // Use HexMesh.WorldPosition only for the XZ layout; replace Y with the Civ-style stepped baseline.
            Vector3 worldPos = HexMesh.WorldPosition(col, row, 0, 0f);
            worldPos.y = ComputeTileY(tile, _oceanRenderHeight, _coastRiseAboveOcean, _landRiseAboveWater, _elevationStep);

            var go = new GameObject($"Tile_{col}_{row}");
            go.transform.SetParent(transform, worldPositionStays: false);
            go.transform.localPosition = worldPos;
            go.transform.localScale = Vector3.one * _tileWorldScale;

            // Always: canonical-coord binding + flat-hex picking collider
            // on the tile root, regardless of which visual path is
            // chosen (ADR-0016 + ADR-0017). Picking walks up the parent
            // chain to find the binding, so prefab children with their
            // own colliders still resolve to the right tile.
            var binding = go.AddComponent<HexTileBinding>();
            binding.Coord = tile.Coord;

            var collider = go.AddComponent<MeshCollider>();
            collider.sharedMesh = _sharedMesh;

            // Visual path: feature.Prefab → biome.Prefab → procedural HexMesh.
            GameObject visualPrefab = ResolveVisualPrefab(tile);
            if (visualPrefab != null)
            {
                var visual = Object.Instantiate(visualPrefab, go.transform);
                visual.transform.localPosition = Vector3.zero;
                visual.transform.localRotation = Quaternion.identity;
                // Intentionally do not overwrite localScale here — the
                // prefab brings its own authored scale and the tile
                // root's _tileWorldScale already propagates uniformly
                // via the parent transform.
            }
            else
            {
                // Procedural fallback (unchanged when both prefabs null).
                var filter = go.AddComponent<MeshFilter>();
                filter.sharedMesh = _sharedMesh;

                var meshRenderer = go.AddComponent<MeshRenderer>();
                BiomeDefinition biome = tile.Biome;
                if (biome != null)
                {
                    meshRenderer.sharedMaterial = _materialCache.Get(biome);
                    ApplyTileColor(meshRenderer, ResolveTileColor(tile));
                }
            }

            return go;
        }

        /// <summary>
        /// Computes the world-space Y for a tile under the Civ-style
        /// stepped-baseline model: Ocean and Coast pin to their own
        /// uniform Y (no per-tile noise — Civ water reads as calm /
        /// readable), and land tiles step up from
        /// <paramref name="oceanY"/> by
        /// <paramref name="landRise"/> + <c>ElevationLevel * elevationStep</c>.
        /// </summary>
        /// <remarks>
        /// Pure function so EditMode tests can pin the contract without
        /// instantiating a MonoBehaviour. A null <paramref name="tile"/>
        /// is treated as land at level 0 (lowest coastline).
        /// </remarks>
        internal static float ComputeTileY(HexTile tile, float oceanY, float coastRise, float landRise, float elevationStep)
        {
            if (tile != null && tile.Biome != null && tile.Biome.IsWater)
            {
                return tile.Biome.IsCoastalWater
                    ? oceanY + coastRise
                    : oceanY;
            }
            int level = (tile != null) ? tile.ElevationLevel : 0;
            return oceanY + landRise + level * elevationStep;
        }

        // ADR-0017 precedence: feature prefab overrides biome prefab;
        // when both are unset the renderer falls through to the
        // procedural HexMesh path.
        private static GameObject ResolveVisualPrefab(HexTile tile)
        {
            if (tile.Feature != null && tile.Feature.Prefab != null) return tile.Feature.Prefab;
            if (tile.Biome   != null && tile.Biome.Prefab   != null) return tile.Biome.Prefab;
            return null;
        }

        // Slice 7A: color comes from the biome. Slice 7B extends this
        // to read tile.Feature.Color when a feature is assigned.
        // Color resolution: feature color wins when assigned (so
        // forests, hills, mountains read distinctly), biome color is
        // the fallback (water tiles + un-featured land). Magenta is
        // the "nothing assigned" diagnostic color, intentionally
        // ugly so missing data is immediately visible.
        private static Color ResolveTileColor(HexTile tile)
        {
            if (tile.Feature != null) return tile.Feature.Color;
            if (tile.Biome != null)   return tile.Biome.Color;
            return Color.magenta;
        }

        private void ApplyTileColor(MeshRenderer meshRenderer, Color color)
        {
            if (_propertyBlock == null) _propertyBlock = new MaterialPropertyBlock();
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
