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

        // Surface-Y cache keyed by source prefab reference. Computed
        // once per unique biome / feature prefab via the renderer-
        // bounds of the first instance, then reused for every other
        // tile that shares the same source prefab. Lets resource
        // visuals sit on the visible top of the underlying tile mesh
        // instead of clipping through it (ADR-0023). Cleared in Clear().
        private readonly Dictionary<GameObject, float> _surfaceYCache = new Dictionary<GameObject, float>();

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

            // Drop the surface-Y cache so a hot-edited prefab
            // re-measures its visible top on the next Render().
            _surfaceYCache.Clear();
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

            // Decide the main tile visual. When the assigned resource
            // is in replace-tile mode (ADR-0023) AND a matching
            // (biome, feature) replacement prefab is authored, that
            // prefab BECOMES the tile visual — no biome / feature
            // prefab is instantiated alongside it. Otherwise the
            // regular feature.Prefab → biome.Prefab → procedural
            // HexMesh chain runs (ADR-0017).
            GameObject mainPrefab = null;
            bool replacedByResource = false;

            if (tile.Resource != null && tile.Resource.ReplaceTile)
            {
                GameObject replacement = tile.Resource.GetReplacementPrefab(tile.Biome, tile.Feature);
                if (replacement != null)
                {
                    mainPrefab = replacement;
                    replacedByResource = true;
                }
                // No matching replacement → fall through to regular
                // biome / feature path and let the resource render
                // as the add-on-top default below.
            }

            if (mainPrefab == null)
            {
                mainPrefab = ResolveVisualPrefab(tile);
            }

            GameObject mainInstance = null;
            if (mainPrefab != null)
            {
                mainInstance = Object.Instantiate(mainPrefab, go.transform);
                mainInstance.transform.localPosition = Vector3.zero;
                mainInstance.transform.localRotation = Quaternion.identity;
                // Intentionally do not overwrite localScale here — the
                // prefab brings its own authored scale and the tile
                // root's _tileWorldScale already propagates uniformly
                // via the parent transform.
            }
            else
            {
                // Procedural fallback (unchanged when no prefab path matches).
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

            // Resource visual rotation snaps to one of the hex's six
            // 60° sextants based on a stable per-coord hash, so the
            // same world re-rendered produces the same orientation on
            // every resource tile (no per-frame jitter, no RNG fork
            // needed in the render path).
            //
            // Applies to BOTH render modes: the replacement prefab (in
            // replace-tile mode) and the default add-on-top prefab.
            // 60° steps preserve hex-grid alignment — every rotation
            // still maps the prefab to a valid hex side.
            if (replacedByResource && mainInstance != null)
            {
                mainInstance.transform.localRotation = Quaternion.Euler(0f, GetResourceRotationY(tile.Coord), 0f);
            }

            // Optional resource visual layered on top of biome / feature
            // visuals (ADR-0022). Skipped entirely when the resource
            // already replaced the tile (ADR-0023); the replacement
            // prefab IS the resource visual in that case.
            //
            // Placed at the visible top of the main visual (cached per
            // source prefab via GetSurfaceY) so the resource model
            // sits on the surface instead of clipping into the tile.
            if (!replacedByResource
                && tile.Resource != null
                && tile.Resource.Prefab != null)
            {
                var resourceVisual = Object.Instantiate(tile.Resource.Prefab, go.transform);
                float surfaceY = (mainInstance != null && mainPrefab != null)
                    ? GetSurfaceY(mainPrefab, mainInstance, go.transform)
                    : 0f;
                resourceVisual.transform.localPosition = new Vector3(0f, surfaceY, 0f);
                resourceVisual.transform.localRotation = Quaternion.Euler(0f, GetResourceRotationY(tile.Coord), 0f);
            }

            return go;
        }

        /// <summary>
        /// Returns the deterministic resource-visual Y rotation (in
        /// degrees) for a tile at the given coordinate. The result is
        /// always one of <c>{0, 60, 120, 180, 240, 300}</c> so the
        /// prefab faces one of the hex's six sides — hex-grid
        /// alignment is preserved at every rotation.
        /// </summary>
        /// <remarks>
        /// Pure function of <see cref="HexCoord.Q"/> and
        /// <see cref="HexCoord.R"/>: same coord → same rotation across
        /// re-renders and across machines (no Unity-side RNG involved).
        /// Internal-visible so the EditMode test pin can assert the
        /// snap-to-60 invariant.
        /// </remarks>
        internal static float GetResourceRotationY(HexCoord coord)
        {
            // Stable integer hash combining Q and R with large odd
            // primes (Wang-style). The same primes used in many hex /
            // grid hashing schemes; collisions inside the small map
            // sizes the asset targets are not a concern because the
            // value is reduced mod 6 anyway.
            int hash = (coord.Q * 73856093) ^ (coord.R * 19349663);

            // C# % preserves the sign of the dividend, which would
            // produce negative sextants for negative hashes. Snap to
            // [0, 5] with a defensive double-mod.
            int sextant = ((hash % 6) + 6) % 6;
            return sextant * 60f;
        }

        /// <summary>
        /// World-space Y of the visible top of the tile at the given
        /// index — the top of whichever geometry the tile actually
        /// renders (procedural HexMesh, biome prefab, feature prefab,
        /// or replaced resource prefab). Falls back to the tile
        /// root's Y when the tile carries no renderers.
        /// </summary>
        /// <remarks>
        /// Used by <see cref="RiverRenderer"/> so river strips sit
        /// on the actual surface of buyer prefabs (e.g., Kenney
        /// Hexagon Kit tiles whose visible geometry rises above the
        /// pivot). Cheap: walks the tile's Renderer components once
        /// per river-edge endpoint; total cost scales with river
        /// count × 2, dwarfed by mesh construction.
        /// </remarks>
        internal float GetTileTopY(int tileIndex)
        {
            if (tileIndex < 0 || tileIndex >= _tileObjects.Count) return 0f;
            GameObject go = _tileObjects[tileIndex];
            if (go == null) return 0f;
            Renderer[] rs = go.GetComponentsInChildren<Renderer>(includeInactive: true);
            if (rs == null || rs.Length == 0) return go.transform.position.y;
            Bounds b = rs[0].bounds;
            for (int i = 1; i < rs.Length; i++) b.Encapsulate(rs[i].bounds);
            return b.max.y;
        }

        // Returns the tile-root-local Y of the visible top of the
        // given main visual. Computed once per source prefab via the
        // first instance's combined Renderer bounds and cached for
        // every subsequent tile that shares the same source. Falls
        // back to 0 when the prefab has no renderers (flat / empty).
        //
        // Cache validity assumes prefab geometry doesn't change at
        // runtime, which is the standard Asset Store contract for
        // prefab content. Cleared in Clear() so a hot-edited prefab
        // re-measures on the next Render() call.
        private float GetSurfaceY(GameObject sourcePrefab, GameObject instance, Transform tileRoot)
        {
            if (_surfaceYCache.TryGetValue(sourcePrefab, out float cached)) return cached;

            Renderer[] renderers = instance.GetComponentsInChildren<Renderer>(includeInactive: true);
            float localTop = 0f;
            if (renderers != null && renderers.Length > 0)
            {
                Bounds combined = renderers[0].bounds;
                for (int i = 1; i < renderers.Length; i++) combined.Encapsulate(renderers[i].bounds);
                // Convert world-space top Y to a tile-root-local offset.
                localTop = combined.max.y - tileRoot.position.y;
                if (localTop < 0f) localTop = 0f; // defensive: never below the tile root
            }

            _surfaceYCache[sourcePrefab] = localTop;
            return localTop;
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
            // Lakes are water, but inland water: a mountain tarn sits in
            // its basin at the surrounding terrace height, not at sea
            // level. Pinning it to oceanY would punch a hole straight
            // through the highlands, so lakes take the land formula and
            // keep the ElevationLevel the hydrology pass left on them
            // (ADR-0025).
            bool isSeaWater = tile != null && tile.Biome != null && tile.Biome.IsWater && !tile.IsLake;
            if (isSeaWater)
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
                // Destroy is deferred to the end of the frame. Until then
                // the object is still a child here, so anything that walks
                // children in the same frame (WorldWrapRenderer.Refresh
                // right after a regenerate) would see the old world too and
                // clone it. Hide and detach it now so it is gone at once.
                if (obj is GameObject go)
                {
                    go.SetActive(false);
                    go.transform.SetParent(null, worldPositionStays: false);
                }
                Destroy(obj);
            }
            else
            {
                DestroyImmediate(obj);
            }
        }
    }
}
