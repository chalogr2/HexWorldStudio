using System.Collections.Generic;
using HexWorldStudio.Coordinates;
using HexWorldStudio.Data;
using UnityEngine;

namespace HexWorldStudio.Rendering
{
    /// <summary>
    /// Renders two shifted visual copies of <see cref="WorldRenderer"/>'s
    /// canonical output at <c>±WorldWidthInWorldUnits</c> along X so the
    /// cylindrical map appears continuous as the camera scrolls
    /// horizontally (ADR-0015).
    /// </summary>
    /// <remarks>
    /// <para>
    /// Copies are produced by <c>Object.Instantiate</c> of the canonical
    /// tile GameObjects (shared meshes and materials via Unity's standard
    /// reference semantics) followed by a per-tile
    /// <see cref="MaterialPropertyBlock"/> copy so per-tile colors
    /// applied by <see cref="WorldRenderer"/> flow into the copies
    /// automatically.
    /// </para>
    /// <para>
    /// Each copy tile carries its own <see cref="MeshCollider"/> so
    /// raycasts hit any visible tile regardless of which copy it
    /// belongs to. Slice 9 will use the <see cref="WorldToCoord"/>
    /// helper to map a raycast hit point on a copy back to the
    /// canonical <see cref="HexCoord"/> via <see cref="WorldMap.Normalize"/>.
    /// </para>
    /// </remarks>
    public sealed class WorldWrapRenderer : MonoBehaviour
    {
        [Tooltip("The canonical world renderer this wrap layer mirrors. Required.")]
        [SerializeField] private WorldRenderer _renderer;

        [Tooltip("Map width in tiles, used to compute the wrap offset in world units. Auto-set by Bootstrap on each Render.")]
        [SerializeField] private int _worldTileWidth;

        private GameObject _leftCopy;
        private GameObject _rightCopy;

        /// <summary>Tile-width of the canonical world (mirrors <c>WorldMap.Width</c>).</summary>
        public int WorldTileWidth
        {
            get => _worldTileWidth;
            set => _worldTileWidth = value;
        }

        /// <summary>The canonical world renderer this wrap layer mirrors.</summary>
        public WorldRenderer Renderer
        {
            get => _renderer;
            set => _renderer = value;
        }

        /// <summary>World-space distance from the canonical world to a wrap copy.</summary>
        public float WorldWidthInWorldUnits => _worldTileWidth * HexMesh.HorizontalSpacing;

        /// <summary>
        /// Rebuilds both wrap copies from the canonical renderer's current
        /// children. Idempotent — destroys any previous copies first.
        /// </summary>
        public void Refresh()
        {
            Clear();
            if (_renderer == null || _worldTileWidth <= 0) return;

            float dx = WorldWidthInWorldUnits;
            _leftCopy  = BuildCopyContainer("WrapCopyLeft",  new Vector3(-dx, 0f, 0f));
            _rightCopy = BuildCopyContainer("WrapCopyRight", new Vector3(+dx, 0f, 0f));
        }

        /// <summary>Destroys both wrap copy containers and their children.</summary>
        public void Clear()
        {
            DestroyContainer(ref _leftCopy);
            DestroyContainer(ref _rightCopy);
        }

        private void OnDestroy()
        {
            Clear();
        }

        /// <summary>
        /// Maps a world-space point to the canonical <see cref="HexCoord"/>
        /// that contains it. Used by Slice 9 to resolve raycast hit points
        /// on wrap-copy tiles back to the data layer.
        /// </summary>
        /// <param name="worldPoint">A point in world space; only X and Z are used.</param>
        /// <param name="map">The world map providing the canonical normalize operation.</param>
        public HexCoord WorldToCoord(Vector3 worldPoint, WorldMap map)
        {
            // Reverse HexMesh.WorldPosition: world Z increases with row,
            // world X increases with column (plus an OddRowOffset on odd
            // rows). Round to the nearest tile center; map normalization
            // covers any out-of-range column from the wrap copies.
            float row = worldPoint.z / HexMesh.VerticalSpacing;
            int rowInt = Mathf.RoundToInt(row);
            float xOffset = (rowInt & 1) == 1 ? HexMesh.OddRowOffset : 0f;
            float col = (worldPoint.x - xOffset) / HexMesh.HorizontalSpacing;
            int colInt = Mathf.RoundToInt(col);

            HexCoord raw = HexCoordExtensions.FromOffsetOddR(colInt, rowInt);
            return map != null ? map.Normalize(raw) : raw;
        }

        private GameObject BuildCopyContainer(string name, Vector3 localOffset)
        {
            var container = new GameObject(name);
            container.transform.SetParent(transform, worldPositionStays: false);
            container.transform.localPosition = localOffset;

            Transform sourceRoot = _renderer.transform;
            int sourceChildCount = sourceRoot.childCount;
            for (int i = 0; i < sourceChildCount; i++)
            {
                Transform sourceChild = sourceRoot.GetChild(i);
                if (sourceChild == null) continue;
                CloneTileInto(sourceChild, container.transform);
            }
            return container;
        }

        private static void CloneTileInto(Transform sourceChild, Transform copyContainer)
        {
            // Instantiate copies all components (MeshFilter, MeshRenderer,
            // MeshCollider). It does NOT copy per-renderer property blocks
            // — those live outside the serialized state — so we replay
            // them manually below.
            GameObject clone = Object.Instantiate(sourceChild.gameObject, copyContainer, worldPositionStays: false);
            clone.name = sourceChild.name; // strip the "(Clone)" suffix

            CopyPropertyBlock(sourceChild.gameObject, clone);
        }

        // MaterialPropertyBlock cannot be created in a static field
        // initializer — Unity throws TypeInitializationException for
        // the same reason it rejects MonoBehaviour field initializers.
        // Constructed lazily on first use.
        private static MaterialPropertyBlock s_blockBuffer;

        private static void CopyPropertyBlock(GameObject source, GameObject destination)
        {
            var sourceMR = source.GetComponent<MeshRenderer>();
            var destMR = destination.GetComponent<MeshRenderer>();
            if (sourceMR == null || destMR == null) return;

            if (s_blockBuffer == null) s_blockBuffer = new MaterialPropertyBlock();
            sourceMR.GetPropertyBlock(s_blockBuffer);
            destMR.SetPropertyBlock(s_blockBuffer);
        }

        private static void DestroyContainer(ref GameObject container)
        {
            if (container == null) return;
            if (Application.isPlaying)
            {
                Object.Destroy(container);
            }
            else
            {
                Object.DestroyImmediate(container);
            }
            container = null;
        }
    }
}
